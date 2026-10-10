using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NORS.Common;
using NORS.Server.Core;

// SDK console referencing the real Common/Core projects; run only in an isolated network namespace.
// No game process, microphone, Steam client or public endpoint is involved.
internal static class RelayRegressionTests
{
    private static int passed;
    private static int failed;
    private static int checks;
    private static string filter;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Handle = typeof(RelayServer).GetMethod("Handle", Private);
    private static readonly MethodInfo Sweep = typeof(RelayServer).GetMethod("Sweep", Private);
    private static readonly MethodInfo Roster = typeof(RelayServer).GetMethod("BroadcastRoster", Private);
    private static readonly FieldInfo Sessions = typeof(RelayServer).GetField("_byId", Private);

    private static string RunCommand(string command, string arguments)
    {
        var p = Process.Start(new ProcessStartInfo(command, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        });
        string result = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception(command + ": " + error);
        return result.Trim();
    }

    public static int Main(string[] args)
    {
        try
        {
            filter = args.Length == 0 ? null : args[0];
            string own = RunCommand("readlink", "/proc/self/ns/net");
            string host = RunCommand("readlink", "/proc/1/ns/net");
            if (own == host) throw new Exception("REFUSED: network namespace equals PID 1 namespace");
            RunCommand("ip", "link set lo up");
            Console.WriteLine("ISOLATED_NETNS=" + own);
            Console.WriteLine("CORE_SHA256=" + Hash(typeof(RelayServer).Assembly.Location));
            Console.WriteLine("COMMON_SHA256=" + Hash(typeof(PacketReader).Assembly.Location));
            Case("blank admin denies claimed host and votes", NoUntrustedModeration);
            Case("password operator flow and endpoint authority", OperatorFlow);
            Case("enabled voice vote authority, majority and no bans", EnabledVotes);
            Case("vote membership changes invalidate prior ineligible votes", VoteMembershipChanges);
            Case("session identity and endpoint spoof rejection", Spoofing);
            Case("byte-identical voice and room/frequency filters", Routing);
            Case("all mandatory truncation boundaries retain state", Truncations);
            Case("malformed shapes and complete validation", Malformed);
            Case("32 sessions and bounded UTF8 roster", AdmissionAndRoster);
            Case("admission packet rates and recovery", RateLimits);
            Case("resource global, egress and limiter table bounds", ResourceBounds);
            Case("timeout releases endpoint and session capacity", Reconnect);
            Case("real receive loop survives malformed and routes voice", ReceiveLoop);
            if (passed + failed == 0) throw new Exception("No test groups matched the filter");
            Console.WriteLine("RESULT passed=" + passed + " failed=" + failed + " assertions=" + checks);
            return failed == 0 ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.WriteLine("HARNESS_ERROR " + e);
            return 2;
        }
    }

    private static void Case(string name, Action test)
    {
        if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] Packet(Action<PacketWriter> build)
    {
        var w = new PacketWriter(); build(w);
        return w.Buffer.Take(w.Length).ToArray();
    }

    private static byte[] Hello(uint id, ulong steam = 0, ulong room = 1, bool host = false, string name = "Pilot")
    {
        return Packet(w => Packets.WriteHello(w, id, steam == 0 ? 1000UL + id : steam, room, host, 1, name));
    }

    private static byte[] State(uint id, ulong steam = 0, ulong room = 1, bool host = false, int freq = 251000)
    {
        return Packet(w => Packets.WriteState(w, id, steam == 0 ? 1000UL + id : steam, room, host,
            1, 12f, 34f, 56f, new[] { freq }, 1, "Pilot"));
    }

    private static byte[] Voice(uint id, int freq = 251000)
    {
        return Packet(w => Packets.WriteVoice(w, id, 7, freq, Modulation.AM, 1, 0,
            12f, 34f, 56f, new byte[] { 0, 255, 17, 93, 201, 1 }, 0, 6, "Pilot", 0, 1));
    }

    private static byte[] IdPacket(PacketType type, uint id)
    {
        return Packet(w => { Packets.WriteHeader(w, type); w.UInt(id); });
    }

    private static byte[] Auth(uint id, string password)
    {
        return Packet(w => Packets.WriteAdminAuth(w, id, password));
    }

    private static byte[] Admin(uint id, AdminOp op, uint target, string argument = "test")
    {
        return Packet(w => Packets.WriteAdminCommand(w, id, op, target, argument));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RelayServer Relay;
        internal readonly string Dir;
        internal readonly string BanFile;
        private readonly List<UdpClient> sockets = new List<UdpClient>();
        internal readonly UdpClient A;
        internal readonly UdpClient B;
        internal readonly UdpClient Alien;

        internal Fixture(string password = null, bool votes = false)
        {
            Dir = Path.Combine(Path.GetTempPath(), "nors-regression-" + Guid.NewGuid());
            Directory.CreateDirectory(Dir);
            BanFile = Path.Combine(Dir, "bans.txt");
            Relay = new RelayServer(0, "Offline regression", BanFile, password, votes);
            A = Socket(); B = Socket(); Alien = Socket();
        }

        internal UdpClient Socket()
        {
            var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            sockets.Add(socket); return socket;
        }

        internal Dictionary<uint, ClientSession> Entries
        {
            get { return (Dictionary<uint, ClientSession>)Sessions.GetValue(Relay); }
        }

        internal void Call(byte[] packet, UdpClient sender, int length = -1)
        {
            try { Handle.Invoke(Relay, new object[] { packet, length < 0 ? packet.Length : length, Endpoint(sender) }); }
            catch (TargetInvocationException e) { throw new Exception("Handle escaped exception", e.InnerException); }
        }

        internal void Join(uint id, UdpClient sender, ulong room = 1, bool host = false, string name = "Pilot")
        {
            Call(Hello(id, room: room, host: host, name: name), sender);
            Check(Entries.ContainsKey(id), "Join rejected for id " + id);
            Drain(sender);
        }

        internal void Setup()
        {
            Join(1, A); Join(2, B);
            Call(State(1), A); Call(State(2), B);
        }

        internal string Snapshot()
        {
            var s = new StringBuilder();
            foreach (var pair in Entries.OrderBy(x => x.Key))
            {
                ClientSession c = pair.Value;
                s.Append(c.Id).Append('|').Append(c.SteamId).Append('|').Append(c.Room).Append('|')
                    .Append(c.IsHost).Append('|').Append(c.IsAdmin).Append('|').Append(c.EndPoint).Append('|')
                    .Append(c.Name).Append('|').Append(c.FactionId).Append('|')
                    .Append(c.X.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(c.Y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(c.Z.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(c.LastSeen.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                    .Append(string.Join(",", c.RxFrequencies.OrderBy(x => x))).Append(';');
            }
            s.Append("bans=").Append(Relay.Bans.Count).Append('|')
                .Append(File.Exists(BanFile) ? Convert.ToBase64String(File.ReadAllBytes(BanFile)) : "absent");
            return s.ToString();
        }

        internal void Unchanged(byte[] packet, UdpClient sender, string why, int length = -1)
        {
            string before = Snapshot();
            long received = Relay.VoiceReceived, forwarded = Relay.VoiceForwarded;
            Call(packet, sender, length);
            Check(before == Snapshot(), why + ": session or ban state mutated");
            Check(received == Relay.VoiceReceived && forwarded == Relay.VoiceForwarded,
                why + ": voice counters mutated");
        }

        public void Dispose()
        {
            Relay.Stop();
            foreach (var socket in sockets) socket.Close();
            Directory.Delete(Dir, true);
        }
    }

    private static IPEndPoint Endpoint(UdpClient socket) { return (IPEndPoint)socket.Client.LocalEndPoint; }
    private static void Drain(UdpClient socket)
    {
        while (socket.Available > 0) { var from = new IPEndPoint(IPAddress.Any, 0); socket.Receive(ref from); }
    }

    private static byte[] Receive(UdpClient socket, PacketType type, int milliseconds = 300)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (!socket.Client.Poll(10000, SelectMode.SelectRead)) continue;
            var from = new IPEndPoint(IPAddress.Any, 0);
            byte[] p = socket.Receive(ref from);
            if (p.Length >= 2 && p[1] == (byte)type) return p;
        }
        return null;
    }

    private static void NoUntrustedModeration()
    {
        using (var f = new Fixture())
        {
            f.Join(1, f.A, host: true); f.Join(2, f.B);
            f.Call(State(1, host: true), f.A);
            Check(!f.Entries[1].IsHost, "Client host flag retained");
            Check(!f.Relay.RemoteAdminEnabled && !f.Relay.VoteKickEnabled, "No-admin policy not active");
            f.Unchanged(Auth(1, ""), f.A, "blank password auth");
            foreach (AdminOp op in new[] { AdminOp.Kick, AdminOp.Ban, AdminOp.Unban })
                f.Unchanged(Admin(1, op, 2), f.A, "claimed host " + op);
            f.Unchanged(Packet(w => Packets.WriteVoteKick(w, 1, 2)), f.A, "disabled vote");
            Check(!File.Exists(f.BanFile) && f.Relay.ClientCount == 2, "Unauthorized durable ban/removal");
        }
    }

    private static void OperatorFlow()
    {
        using (var f = new Fixture("operator-test-only"))
        {
            f.Setup();
            f.Unchanged(Auth(1, "wrong"), f.A, "wrong password");
            f.Unchanged(Auth(1, "operator-test-only"), f.Alien, "spoofed admin auth");
            f.Unchanged(Admin(1, AdminOp.Ban, 2), f.A, "unauthenticated admin");
            f.Call(Auth(1, "operator-test-only"), f.A);
            Check(f.Entries[1].IsAdmin, "Valid password failed");
            f.Unchanged(Admin(1, AdminOp.Kick, 2), f.Alien, "spoofed authenticated command");
            f.Call(Admin(1, AdminOp.Kick, 2), f.A);
            Check(!f.Entries.ContainsKey(2) && !File.Exists(f.BanFile), "Operator kick semantics");
            f.Join(2, f.B);
            f.Call(Admin(1, AdminOp.Ban, 2), f.A);
            Check(!f.Entries.ContainsKey(2) && File.Exists(f.BanFile) && f.Relay.Bans.Count == 1,
                "Operator ban did not persist");
            f.Call(Hello(2), f.B);
            Check(!f.Entries.ContainsKey(2), "Banned identity admitted");
            f.Call(Admin(1, AdminOp.Unban, 0, "1002"), f.A);
            Check(f.Relay.Bans.Count == 0, "Operator unban failed");
            f.Join(2, f.B);
        }
    }

    private static void Spoofing()
    {
        using (var f = new Fixture("operator-test-only"))
        {
            f.Setup();
            f.Call(Auth(1, "operator-test-only"), f.A);
            f.Unchanged(Hello(1), f.Alien, "conflicting Hello endpoint");
            f.Unchanged(State(1), f.Alien, "spoofed State endpoint");
            f.Unchanged(State(99), f.Alien, "State admission bypass");
            f.Unchanged(Voice(1), f.Alien, "spoofed voice");
            f.Unchanged(IdPacket(PacketType.Ping, 1), f.Alien, "spoofed Ping");
            f.Unchanged(IdPacket(PacketType.Bye, 1), f.Alien, "spoofed Bye");
            f.Unchanged(IdPacket(PacketType.Ping, 0), f.Alien, "health Ping session creation");
            Check(Receive(f.Alien, PacketType.Pong) != null, "No stateless health Pong");
            f.Relay.Bans.Add(9999, "", "banned", "offline", 0);
            f.Unchanged(Hello(1, steam: 9999), f.Alien, "banned spoof evicts victim");
            f.Unchanged(State(1, steam: 9999), f.Alien, "banned State evicts victim");
            f.Call(Hello(1, steam: 8888), f.A);
            Check(!f.Entries[1].IsAdmin, "Same-endpoint Hello identity change retained admin");
            f.Call(Auth(1, "operator-test-only"), f.A);
            f.Call(State(1, steam: 7777), f.A);
            Check(!f.Entries[1].IsAdmin, "Same-endpoint State identity change retained admin");
            f.Call(IdPacket(PacketType.Bye, 2), f.B);
            Check(!f.Entries.ContainsKey(2), "Legitimate Bye did not remove session");
        }
    }

    private static int Votes(Fixture f, uint target)
    {
        var votes = (System.Collections.IDictionary)typeof(RelayServer).GetField("_votes", Private).GetValue(f.Relay);
        if (!votes.Contains(target)) return 0;
        object entry = votes[target];
        return ((HashSet<uint>)entry.GetType().GetField("Voters").GetValue(entry)).Count;
    }

    private static byte[] Vote(uint voter, uint target)
    {
        return Packet(w => Packets.WriteVoteKick(w, voter, target));
    }

    private static void EnabledVotes()
    {
        using (var f = new Fixture(votes: true))
        {
            f.Setup();
            f.Call(Vote(1, 2), f.A);
            Check(f.Relay.ClientCount == 2 && Votes(f, 2) == 1, "One voter bypassed minimum2");
            f.Unchanged(Admin(1, AdminOp.Ban, 2), f.A, "vote enabled must not enable bans");
        }
        using (var f = new Fixture(votes: true))
        {
            f.Setup();
            var c = f.Socket(); var d = f.Socket(); var target = f.Socket();
            var crossRoom = f.Socket(); var crossFaction = f.Socket();
            f.Join(3, c); f.Join(4, d); f.Join(5, target);
            f.Join(6, crossRoom, room: 2); f.Join(7, crossFaction);
            byte[] otherFaction = State(7);
            Array.Copy(BitConverter.GetBytes(2), 0, otherFaction, 23, 4);
            f.Call(otherFaction, crossFaction);
            f.Call(Vote(1, 5), f.Alien);
            Check(Votes(f, 5) == 0, "Wrong endpoint cast vote");
            f.Call(Vote(6, 5), crossRoom); f.Call(Vote(7, 5), crossFaction);
            Check(Votes(f, 5) == 0, "Cross-room/faction vote counted");
            f.Call(Vote(5, 5), target);
            Check(Votes(f, 5) == 0, "Self vote counted");
            f.Call(Vote(1, 5), f.A); f.Call(Vote(1, 5), f.A);
            Check(Votes(f, 5) == 1, "Repeated vote counted twice");
            f.Call(Vote(2, 5), f.B);
            Check(f.Entries.ContainsKey(5) && Votes(f, 5) == 2, "Half of4 incorrectly passed strict majority");
            f.Call(Vote(3, 5), c);
            Check(!f.Entries.ContainsKey(5), "Three of4 failed to kick");
            Check(Receive(target, PacketType.Kicked) != null, "Voice client did not receive kick");
            Check(f.Relay.Bans.Count == 0 && !File.Exists(f.BanFile), "Vote persisted a ban");
            f.Join(5, target);
            Check(f.Entries.ContainsKey(5), "Voice vote became persistent exclusion");
        }
    }

    private static void VoteMembershipChanges()
    {
        foreach (string change in new[] { "room", "faction", "identity", "rejoin" })
        {
            using (var f = new Fixture(votes: true))
            {
                f.Setup();
                var c = f.Socket(); var d = f.Socket(); var target = f.Socket();
                f.Join(3, c); f.Join(4, d); f.Join(5, target);
                f.Call(Vote(1, 5), f.A);
                if (change == "rejoin")
                {
                    f.Call(IdPacket(PacketType.Bye, 1), f.A);
                    f.Join(1, f.A);
                }
                else
                {
                    byte[] changed = State(1, steam: change == "identity" ? 8888UL : 1001UL,
                        room: change == "room" ? 2UL : 1UL);
                    if (change == "faction") Array.Copy(BitConverter.GetBytes(2), 0, changed, 23, 4);
                    f.Call(changed, f.A);
                }
                f.Call(Vote(2, 5), f.B);
                Check(f.Entries.ContainsKey(5), "Changed " + change + " voter supplied false majority");
                Check(Votes(f, 5) == 1, "Changed " + change + " retained an old vote");
                f.Call(Vote(3, 5), c);
                if (change == "identity" || change == "rejoin")
                {
                    Check(f.Entries.ContainsKey(5), "Two of4 passed after " + change);
                    f.Call(Vote(4, 5), d);
                }
                Check(!f.Entries.ContainsKey(5), "Eligible majority did not pass after " + change);
                Check(!File.Exists(f.BanFile), "Membership-change vote wrote ban file");
            }
        }
    }

    private static void Routing()
    {
        using (var f = new Fixture())
        {
            f.Setup();
            var otherRoom = f.Socket(); var otherFrequency = f.Socket();
            f.Join(3, otherRoom, room: 2); f.Call(State(3, room: 2), otherRoom);
            f.Join(4, otherFrequency); f.Call(State(4, freq: 252000), otherFrequency);
            byte[] voice = Voice(1);
            foreach (int length in new[] { voice.Length, 40, 46, 47 })
            {
                byte[] variant = voice.Take(length).ToArray();
                f.Call(variant, f.A);
                byte[] received = Receive(f.B, PacketType.Voice);
                Check(received != null && variant.SequenceEqual(received), "Voice bytes changed/layout rejected " + length);
            }
            Check(Receive(f.A, PacketType.Voice, 30) == null, "Sender received its voice");
            Check(Receive(otherRoom, PacketType.Voice, 30) == null, "Cross-room leak");
            Check(Receive(otherFrequency, PacketType.Voice, 30) == null, "Wrong-frequency leak");
            Check(f.Relay.VoiceForwarded == 4, "Incorrect fan-out count");
        }
    }

    private static void Truncations()
    {
        var packets = new[] { Hello(1), State(1), Voice(1), IdPacket(PacketType.Ping, 1),
            IdPacket(PacketType.Bye, 1), Auth(1, "operator-test-only"), Admin(1, AdminOp.Ban, 2),
            Packet(w => Packets.WriteVoteKick(w, 1, 2)) };
        foreach (byte[] p in packets)
        {
            for (int length = 0; length < p.Length; length++)
            {
                if (p[1] == (byte)PacketType.Voice && new[] { 40, 46, 47 }.Contains(length)) continue;
                // Fresh fixtures prevent rate rejection from masking parser bugs.
                using (var f = new Fixture("operator-test-only"))
                {
                    f.Setup(); f.Call(Auth(1, "operator-test-only"), f.A);
                    var reused = new byte[NorsProtocol.MaxDatagram];
                    for (int i = 0; i < reused.Length; i++) reused[i] = 0xA5;
                    Array.Copy(p, reused, p.Length);
                    f.Unchanged(reused, f.A, "truncated type " + p[1] + " length " + length, length);
                }
            }
        }
    }

    private static void Malformed()
    {
        var bad = new List<byte[]>();
        byte[] p = State(1); p[39] = 255; bad.Add(p);
        p = Hello(1); p[27] = 255; bad.Add(p);
        p = Voice(1); p[32] = 255; p[33] = 255; bad.Add(p);
        p = Hello(1); p[0] = 255; bad.Add(p);
        bad.Add(new byte[] { 2, 255 });
        bad.Add(IdPacket(PacketType.Bye, 1).Concat(new byte[] { 42 }).ToArray());
        p = new byte[1401]; Array.Copy(Hello(1), p, Hello(1).Length); bad.Add(p);
        foreach (byte[] invalid in bad)
        {
            using (var f = new Fixture())
            {
                f.Setup();
                f.Unchanged(invalid, f.A, "malformed shape type " + invalid[1]);
            }
        }
        // Standalone bounded reader must reject stale backing bytes beyond its slice.
        bool threw = false;
        try { var r = new PacketReader(new byte[1400], 0, 1); r.UInt(); }
        catch (FormatException) { threw = true; }
        Check(threw, "PacketReader UInt ignored slice end");
    }

    private static void AdmissionAndRoster()
    {
        using (var f = new Fixture())
        {
            // Euro signs are three-byte UTF8; cap must be bytes, not UTF16 characters.
            string longName = string.Concat(Enumerable.Repeat("\u20ac", 32));
            for (uint i = 1; i <= 32; i++) f.Join(i, i == 1 ? f.A : f.Socket(), name: longName);
            Check(f.Relay.ClientCount == 32, "Did not admit approved32");
            f.Unchanged(Hello(33), f.Alien, "33rd admission");
            Check(f.Entries.Values.All(x => Encoding.UTF8.GetByteCount(x.Name) <= 32), "Name byte cap exceeded");
            Roster.Invoke(f.Relay, null);
            byte[] roster = Receive(f.A, PacketType.Roster);
            Check(roster != null && roster.Length <= NorsProtocol.MaxDatagram, "Actual32 roster missing/oversized");
            var reader = new PacketReader(roster, 2, roster.Length - 2);
            Check(Packets.ReadRosterCount(ref reader) == 32, "Actual roster omitted clients");
            for (int i = 0; i < 32; i++)
                Check(!Packets.ReadRosterEntry(ref reader).Name.Contains("\ufffd"), "UTF8 name split");
            Check(reader.End, "Roster had invalid trailing bytes");
        }
    }

    private static void RateLimits()
    {
        using (var f = new Fixture())
        {
            f.Setup();
            for (int i = 0; i < 400; i++) f.Call(Voice(1), f.A);
            Check(f.Relay.VoiceForwarded > 0 && f.Relay.VoiceForwarded < 400, "Endpoint voice burst unbounded");
            long before = f.Relay.VoiceForwarded;
            Thread.Sleep(1100);
            f.Call(Voice(1), f.A);
            Check(f.Relay.VoiceForwarded == before + 1, "Rate admission never recovered");
        }
    }

    private static void SetBucket(object bucket, double tokens, double last)
    {
        bucket.GetType().GetField("Tokens").SetValue(bucket, tokens);
        bucket.GetType().GetField("Last").SetValue(bucket, last);
    }

    private static void ResourceBounds()
    {
        using (var f = new Fixture())
        {
            f.Setup();
            object global = typeof(RelayServer).GetField("_globalRate", Private).GetValue(f.Relay);
            SetBucket(global, 0, f.Relay.Now + 10);
            f.Unchanged(Hello(3), f.Alien, "exhausted global admission bucket");
            SetBucket(global, 0, f.Relay.Now - 1);
            f.Join(3, f.Alien);
            object egress = typeof(RelayServer).GetField("_egressRate", Private).GetValue(f.Relay);
            SetBucket(egress, 0, f.Relay.Now + 10);
            long forwarded = f.Relay.VoiceForwarded;
            f.Call(Voice(1), f.A);
            Check(f.Relay.VoiceForwarded == forwarded, "Exhausted egress budget forwarded voice");
            SetBucket(egress, 0, f.Relay.Now - 1);
            f.Call(Voice(1), f.A);
            Check(f.Relay.VoiceForwarded == forwarded + 1, "Egress budget did not refill");
        }
        using (var f = new Fixture())
        {
            for (int i = 0; i < 260; i++) f.Call(IdPacket(PacketType.Ping, 0), f.Socket());
            var rates = (System.Collections.IDictionary)typeof(RelayServer).GetField("_rates", Private).GetValue(f.Relay);
            Check(rates.Count == 256 && f.Relay.ClientCount == 0, "Limiter table unbounded/health created clients");
            f.Unchanged(Hello(1), f.A, "admission with full endpoint limiter table");
            foreach (object bucket in rates.Values) SetBucket(bucket, 0, f.Relay.Now - 31);
            Sweep.Invoke(f.Relay, null);
            Check(rates.Count == 0, "Expired limiter endpoints not reclaimed");
            f.Join(1, f.A);
        }
    }

    private static void Reconnect()
    {
        using (var f = new Fixture())
        {
            f.Setup();
            f.Unchanged(Hello(1), f.Alien, "pre-timeout NAT takeover");
            // Advance just the session age, then exercise the real sweep implementation.
            f.Entries[1].LastSeen = f.Relay.Now - NorsProtocol.ClientTimeoutSeconds - 1;
            Sweep.Invoke(f.Relay, null);
            Check(!f.Entries.ContainsKey(1) && f.Entries.ContainsKey(2), "Timeout removed wrong sessions");
            f.Join(1, f.Alien);
            Check(f.Entries[1].EndPoint.Equals(Endpoint(f.Alien)), "Timed-out ID could not reconnect");
            Check(!f.Entries[1].IsAdmin && !f.Entries[1].IsHost, "Authority survived reconnect");
        }
    }

    private static void ReceiveLoop()
    {
        using (var f = new Fixture())
        {
            var socket = (Socket)typeof(RelayServer).GetField("_socket", Private).GetValue(f.Relay);
            var destination = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)socket.LocalEndPoint).Port);
            f.Relay.Start();
            byte[] p = Hello(1); f.A.Send(p, p.Length, destination);
            Check(Receive(f.A, PacketType.HelloAck) != null, "Real Hello A failed");
            p = Hello(2); f.B.Send(p, p.Length, destination);
            Check(Receive(f.B, PacketType.HelloAck) != null, "Real Hello B failed");
            p = State(1); f.A.Send(p, p.Length, destination);
            p = State(2); f.B.Send(p, p.Length, destination);
            // Reuse-buffer attack after a complete packet, then a stateless health probe.
            f.Alien.Send(new byte[] { 2, (byte)PacketType.State }, 2, destination);
            p = IdPacket(PacketType.Ping, 0); f.Alien.Send(p, p.Length, destination);
            Check(Receive(f.Alien, PacketType.Pong) != null, "Receive loop died on malformed State");
            p = Voice(1); f.A.Send(p, p.Length, destination);
            byte[] received = Receive(f.B, PacketType.Voice);
            Check(received != null && p.SequenceEqual(received), "Real-loop byte-identical voice failed");
            Check(f.Relay.ClientCount == 2, "Malformed packet/health probe changed membership");
        }
    }
}
