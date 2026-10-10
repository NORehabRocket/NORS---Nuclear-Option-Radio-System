using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NORS.Common;

namespace NORS.Server.Core
{
    /// <summary>
    /// Frequency-routed UDP voice relay with admin control (kick / ban). Clients announce which
    /// frequencies they monitor; a Voice datagram is forwarded verbatim to every other client
    /// monitoring its transmit frequency. All terrain/range/crypto attenuation happens client-side.
    /// Runs its receive loop on a background thread so a UI or console can drive it.
    /// </summary>
    public sealed class RelayServer
    {
        public string ServerName { get; }
        public int Port { get; }
        public BanList Bans { get; }

        public long VoiceReceived { get; private set; }
        public long VoiceForwarded { get; private set; }
        public bool Running { get; private set; }

        /// <summary>Players can call a vote to kick another player; passes on a strict majority.</summary>
        public bool VoteKickEnabled { get; set; } = true;

        /// <summary>Fraction of eligible voters needed to pass a vote-kick (strict majority by default).</summary>
        public float VoteRatio { get; set; } = 0.5f;

        public bool RemoteAdminEnabled => !string.IsNullOrEmpty(_adminPassword);

        private const double VoteWindowSeconds = 45.0;
        private const double RosterInterval = 2.0;
        // A full roster remains below MaxDatagram with 32 UTF-8 bytes per name.
        public const int MaxClients = 32;
        private const int MaxTrackedEndpoints = 256;
        private readonly Dictionary<IPEndPoint, RateBucket> _rates = new Dictionary<IPEndPoint, RateBucket>();
        private readonly RateBucket _globalRate = new RateBucket(8000);
        private readonly RateBucket _egressRate = new RateBucket(16 * 1024 * 1024);

        private sealed class RateBucket
        {
            public double Tokens, Last;
            public RateBucket(double capacity) { Tokens = capacity; }
            public bool Take(double now, double rate, double capacity, double cost = 1)
            {
                Tokens = Math.Min(capacity, Tokens + Math.Max(0, now - Last) * rate);
                Last = now;
                if (Tokens < cost) return false;
                Tokens -= cost;
                return true;
            }
        }

        private bool AdmitPacket(IPEndPoint from)
        {
            double now = Now;
            if (!_globalRate.Take(now, 4000, 8000)) return false;
            if (!_rates.TryGetValue(from, out var rate))
            {
                if (_rates.Count >= MaxTrackedEndpoints) return false;
                rate = new RateBucket(200) { Last = now };
                _rates[new IPEndPoint(from.Address, from.Port)] = rate;
            }
            return rate.Take(now, 100, 200);
        }

        private bool Owns(uint id, IPEndPoint from, out ClientSession session)
        {
            return _byId.TryGetValue(id, out session) && session.EndPoint.Equals(from);
        }

        private static void RequireEnd(ref PacketReader r)
        {
            if (!r.End) throw new FormatException("Unexpected packet suffix.");
        }

        private static string RosterName(string name)
        {
            // Limit bytes, not UTF-16 characters, without cutting a surrogate pair.
            while (Encoding.UTF8.GetByteCount(name) > NorsProtocol.MaxPlayerNameLen)
            {
                int n = name.Length - 1;
                if (n > 0 && char.IsLowSurrogate(name[n]) && char.IsHighSurrogate(name[n - 1])) n--;
                name = name.Substring(0, n);
            }
            return name;
        }

        /// <summary>Human-readable log lines (joins/leaves/kicks). Raised from the network thread.</summary>
        public event Action<string> Log;

        private sealed class VoteState
        {
            public readonly HashSet<uint> Voters = new HashSet<uint>();
            public double Start;
            public string TargetName;
        }

        private readonly Dictionary<uint, VoteState> _votes = new Dictionary<uint, VoteState>();

        // Called with _lock held; votes belong to a session's current identity/membership.
        private void InvalidateVotes(uint id)
        {
            _votes.Remove(id);
            foreach (var vote in _votes.Values) vote.Voters.Remove(id);
        }

        private void RemoveSession(uint id)
        {
            _byId.Remove(id);
            InvalidateVotes(id);
        }
        private readonly string _adminPassword;

        private readonly Socket _socket;
        private readonly Dictionary<uint, ClientSession> _byId = new Dictionary<uint, ClientSession>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly PacketWriter _scratch = new PacketWriter();   // network thread only
        private readonly PacketWriter _admin = new PacketWriter();     // admin thread only
        private readonly object _lock = new object();
        private readonly object _adminLock = new object();
        private Thread _thread;
        private volatile bool _run;

        public RelayServer(int port, string serverName, string banFilePath, string adminPassword = null, bool voteKickEnabled = true)
        {
            Port = port;
            ServerName = serverName;
            Bans = new BanList(banFilePath);
            _adminPassword = adminPassword;
            VoteKickEnabled = voteKickEnabled;

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                _socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch { /* non-Windows */ }
        }

        public double Now => _clock.Elapsed.TotalSeconds;

        public int ClientCount { get { lock (_lock) return _byId.Count; } }

        public void Start()
        {
            if (_run) return;
            _run = true;
            Running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "NORS-relay" };
            _thread.Start();
            Emit($"Relay '{ServerName}' listening on UDP {Port}.");
        }

        public void Stop()
        {
            _run = false;
            Running = false;
            try { _socket.Close(); } catch { }
            try { _thread?.Join(300); } catch { }
        }

        private void Loop()
        {
            var buffer = new byte[NorsProtocol.MaxDatagram];
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            double nextSweep = Now + 1.0;
            double nextRoster = Now + RosterInterval;
            double nextBanSync = Now + BanSyncInterval;

            while (_run)
            {
                try
                {
                    if (!_socket.Poll(200_000, SelectMode.SelectRead))
                    {
                        if (Now >= nextSweep) { Sweep(); nextSweep = Now + 1.0; }
                        if (Now >= nextRoster) { BroadcastRoster(); nextRoster = Now + RosterInterval; }
                        if (Now >= nextBanSync) { SyncBans(); nextBanSync = Now + BanSyncInterval; }
                        continue;
                    }

                    int len = _socket.ReceiveFrom(buffer, ref any);
                    if (len >= 2) Handle(buffer, len, (IPEndPoint)any);
                }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { /* transient */ }

                if (Now >= nextSweep) { Sweep(); nextSweep = Now + 1.0; }
                if (Now >= nextRoster) { BroadcastRoster(); nextRoster = Now + RosterInterval; }
                if (Now >= nextBanSync) { SyncBans(); nextBanSync = Now + BanSyncInterval; }
            }
        }

        private void Handle(byte[] buffer, int len, IPEndPoint from)
        {
            if (len < 2 || len > NorsProtocol.MaxDatagram || !AdmitPacket(from)) return;
            try { HandlePacket(buffer, len, from); }
            catch (FormatException) { /* Invalid datagrams must not mutate sessions or stop the loop. */ }
        }

        private void HandlePacket(byte[] buffer, int len, IPEndPoint from)
        {
            var r = new PacketReader(buffer, 0, len);
            byte version = r.Byte();
            var type = (PacketType)r.Byte();

            if (version != NorsProtocol.Version)
            {
                SendAdmin(from, w => Packets.WriteReject(w, $"Protocol mismatch (server v{NorsProtocol.Version}, you v{version})"));
                return;
            }

            switch (type)
            {
                case PacketType.Hello: OnHello(ref r, from); break;
                case PacketType.State: OnState(ref r, from); break;
                case PacketType.Voice: OnVoice(buffer, len, ref r, from); break;
                case PacketType.Ping: OnPing(ref r, from); break;
                case PacketType.Bye: OnBye(ref r, from); break;
                case PacketType.VoteKick: OnVoteKick(ref r, from); break;
                case PacketType.AdminAuth: OnAdminAuth(ref r, from); break;
                case PacketType.AdminCommand: OnAdminCommand(ref r, from); break;
            }
        }

        private void OnHello(ref PacketReader r, IPEndPoint from)
        {
            Packets.ReadHello(ref r, out uint id, out ulong steamId, out ulong roomId, out bool isHost, out int factionId, out string name);
            RequireEnd(ref r);
            name = RosterName(name);
            bool adminRevoked = false;

            lock (_lock)
            {
                if (_byId.TryGetValue(id, out var existing) && !existing.EndPoint.Equals(from)) return;
            }

            if (Bans.IsBanned(steamId, from.Address.ToString(), roomId, out string banReason))
            {
                SendAdmin(from, w => Packets.WriteReject(w, "Banned" + (string.IsNullOrEmpty(banReason) ? "" : ": " + banReason)));
                return;
            }

            lock (_lock)
            {
                if (!_byId.TryGetValue(id, out var s))
                {
                    if (_byId.Count >= MaxClients) return;
                    foreach (var other in _byId.Values)
                        if (other.EndPoint.Equals(from)) return;
                    s = new ClientSession { Id = id };
                    _byId[id] = s;
                    Emit($"+ {name} joined ({from}) room {roomId}. Clients: {_byId.Count}");
                }
                if (s.SteamId != steamId || s.Room != roomId || s.FactionId != factionId)
                {
                    adminRevoked = s.IsAdmin;
                    s.IsAdmin = false;
                    InvalidateVotes(id);
                }
                s.EndPoint = new IPEndPoint(from.Address, from.Port);
                s.SteamId = steamId;
                s.Room = roomId;
                s.IsHost = false; // A wire claim is not proof of host authority.
                s.Name = name;
                s.FactionId = factionId;
                s.LastSeen = Now;
            }
            SendScratch(from, w => Packets.WriteHelloAck(w, ServerName));
            if (adminRevoked)
                SendAdmin(from, w => Packets.WriteAdminResult(w, false, true, "Session identity changed; log in again."));
        }

        private void OnState(ref PacketReader r, IPEndPoint from)
        {
            uint id = r.UInt();
            ulong steamId = r.ULong();
            ulong roomId = r.ULong();
            bool isHost = r.Bool();
            int factionId = r.Int();
            float x = r.Float(), y = r.Float(), z = r.Float();
            int count = r.Byte();
            if (count > NorsProtocol.MaxRxFrequencies) throw new FormatException("Too many frequencies.");
            var frequencies = new int[count];
            for (int i = 0; i < count; i++) frequencies[i] = r.Int();
            string name = RosterName(r.Str());
            RequireEnd(ref r);
            bool adminRevoked = false;

            lock (_lock)
            {
                if (!Owns(id, from, out var existing)) return;
            }

            if (Bans.IsBanned(steamId, from.Address.ToString(), roomId, out _))
            {
                SendScratch(from, w => Packets.WriteKicked(w, "Banned"));
                lock (_lock) RemoveSession(id);
                return;
            }

            lock (_lock)
            {
                if (!Owns(id, from, out var s)) return;
                if (s.SteamId != steamId || s.Room != roomId || s.FactionId != factionId)
                {
                    adminRevoked = s.IsAdmin;
                    s.IsAdmin = false;
                    InvalidateVotes(id);
                }
                s.SteamId = steamId;
                s.Room = roomId;
                s.IsHost = false;
                s.FactionId = factionId;
                s.X = x; s.Y = y; s.Z = z;
                s.LastSeen = Now;
                s.RxFrequencies.Clear();
                for (int i = 0; i < count; i++) s.RxFrequencies.Add(frequencies[i]);
                if (!string.IsNullOrEmpty(name)) s.Name = name;
            }
            if (adminRevoked)
                SendAdmin(from, w => Packets.WriteAdminResult(w, false, true, "Session identity changed; log in again."));
        }

        private void OnVoice(byte[] buffer, int len, ref PacketReader r, IPEndPoint from)
        {
            uint senderId = r.UInt();
            r.UInt();                 // seq
            int txFreq = r.Int();     // route key
            r.Byte(); r.Int(); r.Byte(); // modulation, faction, crypto
            r.Float(); r.Float(); r.Float();
            int audioLength = r.UShort();
            if (audioLength > NorsProtocol.MaxAudioBytes) throw new FormatException("Oversized audio.");
            r.Skip(audioLength);
            // Retain the published protocol's optional callsign/jam/faction suffixes.
            if (!r.End) r.Str();
            if (!r.End) r.Byte();
            if (!r.End) r.Int();
            RequireEnd(ref r);

            lock (_lock)
            {
                if (!Owns(senderId, from, out var sender)) return;
                VoiceReceived++;
                ulong room = sender.Room;
                if (room == 0) return;   // sender not in a session yet — don't leak across rooms

                foreach (var s in _byId.Values)
                {
                    if (s.Id == senderId) continue;
                    if (s.Room != room) continue;            // never cross rooms (separate "servers")
                    if (!s.RxFrequencies.Contains(txFreq)) continue;
                    if (!_egressRate.Take(Now, 8 * 1024 * 1024, 16 * 1024 * 1024, len)) break;
                    try { _socket.SendTo(buffer, 0, len, SocketFlags.None, s.EndPoint); VoiceForwarded++; }
                    catch (SocketException) { }
                }
            }
        }

        private void OnPing(ref PacketReader r, IPEndPoint from)
        {
            uint id = r.UInt();
            RequireEnd(ref r);
            lock (_lock)
            {
                if (_byId.TryGetValue(id, out var s))
                {
                    if (!s.EndPoint.Equals(from)) return;
                    s.LastSeen = Now;
                }
            }
            SendScratch(from, w => { Packets.WriteHeader(w, PacketType.Pong); });
        }

        private void OnBye(ref PacketReader r, IPEndPoint from)
        {
            uint id = r.UInt();
            RequireEnd(ref r);
            lock (_lock)
            {
                if (Owns(id, from, out var s))
                {
                    Emit($"- {s.Name} left. Clients: {_byId.Count - 1}");
                    RemoveSession(id);
                }
            }
        }

        private void OnVoteKick(ref PacketReader r, IPEndPoint from)
        {
            uint voter = r.UInt();
            uint target = r.UInt();
            RequireEnd(ref r);
            if (!VoteKickEnabled) return;

            string targetName = null, voterName = null;
            int votes = 0, needed = 0, factionId = 0;
            ulong room = 0;
            bool pass = false, valid = false, wrongFaction = false;
            IPEndPoint voterEp = null;

            lock (_lock)
            {
                if (Owns(voter, from, out var vs) && _byId.TryGetValue(target, out var ts)
                    && voter != target && vs.Room == ts.Room)   // only within the same server/room
                {
                    voterEp = vs.EndPoint;
                    if (vs.FactionId != ts.FactionId)
                    {
                        wrongFaction = true;          // can only vote-kick your own faction
                    }
                    else
                    {
                        valid = true;
                        voterName = vs.Name; targetName = ts.Name; factionId = ts.FactionId; room = ts.Room;
                        if (!_votes.TryGetValue(target, out var st) || Now - st.Start > VoteWindowSeconds)
                        {
                            st = new VoteState { Start = Now, TargetName = ts.Name };
                            _votes[target] = st;
                        }
                        st.Voters.RemoveWhere(v => !_byId.TryGetValue(v, out var member)
                            || v == target || member.Room != ts.Room || member.FactionId != ts.FactionId);
                        st.Voters.Add(voter);

                        // Eligible = same room + same faction, excluding the target.
                        int eligible = 0;
                        foreach (var s in _byId.Values)
                            if (s.Id != target && s.Room == room && s.FactionId == factionId) eligible++;

                        votes = st.Voters.Count;
                        needed = Math.Max(2, (int)Math.Floor(eligible * VoteRatio) + 1);
                        if (votes >= needed) { pass = true; _votes.Remove(target); }
                    }
                }
            }

            if (wrongFaction)
            {
                if (voterEp != null)
                    SendAdmin(voterEp, w => Packets.WriteNotice(w, "You can only vote-kick players on your own faction."));
                return;
            }
            if (!valid) return;

            if (pass)
            {
                Kick(target, "Vote-kicked by your faction");
                BroadcastNoticeToFaction(room, factionId, $"{targetName} was vote-kicked.");
            }
            else
            {
                BroadcastNoticeToFaction(room, factionId, $"{voterName} voted to kick {targetName} ({votes}/{needed}).");
            }
        }

        private void OnAdminAuth(ref PacketReader r, IPEndPoint from)
        {
            uint id = r.UInt();
            string pw = r.Str();
            RequireEnd(ref r);
            bool ok = false;
            string msg;

            if (!RemoteAdminEnabled) msg = "Remote admin is disabled on this server.";
            else if (pw == _adminPassword)
            {
                lock (_lock) { if (Owns(id, from, out var s)) { s.IsAdmin = true; ok = true; } }
                msg = ok ? "Admin authenticated." : "Session not found.";
                if (ok) Emit($"Admin authenticated: #{id:X8}");
            }
            else msg = "Incorrect admin password.";

            SendAdmin(from, w => Packets.WriteAdminResult(w, ok, true, msg));
        }

        private void OnAdminCommand(ref PacketReader r, IPEndPoint from)
        {
            uint id = r.UInt();
            var op = (AdminOp)r.Byte();
            uint target = r.UInt();
            string arg = r.Str();
            RequireEnd(ref r);

            // Only an authenticated relay operator may moderate; room ownership is unverified.
            bool master;
            lock (_lock)
            {
                master = RemoteAdminEnabled && Owns(id, from, out var sender) && sender.IsAdmin;
            }
            if (!master) { SendAdmin(from, w => Packets.WriteAdminResult(w, false, true, "Not authorized. Log in again.")); return; }

            bool ok = true;
            string result;
            switch (op)
            {
                case AdminOp.Kick: ok = Kick(target, string.IsNullOrEmpty(arg) ? "Kicked by host" : arg); result = ok ? "Kicked." : "Target not found."; break;
                case AdminOp.Ban: ok = Ban(target, arg); result = ok ? "Banned." : "Target not found."; break;
                case AdminOp.Unban:
                    if (!master) { ok = false; result = "Only the server operator can unban."; }
                    else { int n = Unban(arg); ok = n > 0; result = ok ? $"Removed {n} ban(s)." : "No matching ban."; }
                    break;
                default: ok = false; result = "Unknown command."; break;
            }
            SendAdmin(from, w => Packets.WriteAdminResult(w, ok, false, result));
        }

        private readonly List<ClientSession> _roomScratch = new List<ClientSession>();

        private void BroadcastRoster()
        {
            lock (_lock)
            {
                if (_byId.Count == 0) return;

                // Send each client only the roster of its OWN room, so different servers can't see
                // (or vote/moderate) each other's players. Group by room.
                var rooms = new HashSet<ulong>();
                foreach (var s in _byId.Values) rooms.Add(s.Room);

                foreach (ulong room in rooms)
                {
                    _roomScratch.Clear();
                    foreach (var s in _byId.Values)
                        if (s.Room == room) _roomScratch.Add(s);

                    Packets.WriteRosterHeader(_scratch, _roomScratch.Count);
                    foreach (var s in _roomScratch)
                        Packets.WriteRosterEntry(_scratch, s.Id, s.FactionId, s.Name);
                    foreach (var s in _roomScratch)
                        try { _socket.SendTo(_scratch.Buffer, 0, _scratch.Length, SocketFlags.None, s.EndPoint); }
                        catch (SocketException) { }
                }
                return;
            }
        }

        private void BroadcastNotice(string message)
        {
            Emit(message);
            lock (_lock)
            {
                Packets.WriteNotice(_scratch, message);
                foreach (var s in _byId.Values)
                    try { _socket.SendTo(_scratch.Buffer, 0, _scratch.Length, SocketFlags.None, s.EndPoint); }
                    catch (SocketException) { }
            }
        }

        private void BroadcastNoticeToFaction(ulong room, int factionId, string message)
        {
            Emit(message);
            lock (_lock)
            {
                Packets.WriteNotice(_scratch, message);
                foreach (var s in _byId.Values)
                    if (s.Room == room && s.FactionId == factionId)
                        try { _socket.SendTo(_scratch.Buffer, 0, _scratch.Length, SocketFlags.None, s.EndPoint); }
                        catch (SocketException) { }
            }
        }

        private void Sweep()
        {
            var staleRates = new List<IPEndPoint>();
            foreach (var pair in _rates)
                if (Now - pair.Value.Last > 30) staleRates.Add(pair.Key);
            foreach (var endpoint in staleRates) _rates.Remove(endpoint);
            lock (_lock)
            {
                List<uint> dead = null;
                foreach (var kv in _byId)
                    if (Now - kv.Value.LastSeen > NorsProtocol.ClientTimeoutSeconds)
                    {
                        if (dead == null) dead = new List<uint>();
                        dead.Add(kv.Key);
                    }

                if (dead != null)
                    foreach (var id in dead)
                    {
                        Emit($"- {_byId[id].Name} timed out. Clients: {_byId.Count - 1}");
                        RemoveSession(id);
                    }

                // Expire stale votes and drop votes whose target left or whose voters left.
                if (_votes.Count > 0)
                {
                    List<uint> drop = null;
                    foreach (var kv in _votes)
                    {
                        if (Now - kv.Value.Start > VoteWindowSeconds || !_byId.ContainsKey(kv.Key))
                        {
                            if (drop == null) drop = new List<uint>();
                            drop.Add(kv.Key);
                        }
                        else
                            kv.Value.Voters.RemoveWhere(v => !_byId.ContainsKey(v));
                    }
                    if (drop != null) foreach (var t in drop) _votes.Remove(t);
                }
            }
        }

        // ---------------- admin API (callable from any thread) ----------------

        public List<ClientInfo> GetClients()
        {
            var list = new List<ClientInfo>();
            lock (_lock)
            {
                foreach (var s in _byId.Values)
                    list.Add(new ClientInfo
                    {
                        Id = s.Id,
                        SteamId = s.SteamId,
                        Room = s.Room,
                        IsHost = s.IsHost,
                        Name = s.Name,
                        FactionId = s.FactionId,
                        Ip = s.EndPoint?.Address.ToString() ?? "?",
                        Port = s.EndPoint?.Port ?? 0,
                        FreqCount = s.RxFrequencies.Count,
                        IdleSeconds = Now - s.LastSeen,
                    });
            }
            return list;
        }

        /// <summary>Disconnects a client (it may reconnect). Returns true if found.</summary>
        /// <summary>How often we check whether another server sharing our ban file changed it.</summary>
        private const double BanSyncInterval = 3.0;

        /// <summary>
        /// Picks up bans made on other servers that share this ban file, and enforces them here:
        /// a ban is worthless if someone already connected keeps talking until they reconnect.
        /// </summary>
        private void SyncBans()
        {
            try
            {
                if (!Bans.SyncFromDisk()) return;
                Emit($"Ban list reloaded from disk ({Bans.Count} entries) - another server changed it.");
                EnforceBans();
            }
            catch { /* never let housekeeping kill the relay loop */ }
        }

        /// <summary>Disconnects any connected client covered by the current ban list.</summary>
        public int EnforceBans()
        {
            var offenders = new List<ClientSession>();
            lock (_lock)
            {
                foreach (var s in _byId.Values)
                    if (Bans.IsBanned(s.SteamId, s.Ip, s.Room, out _)) offenders.Add(s);
            }
            foreach (var s in offenders)
            {
                Bans.IsBanned(s.SteamId, s.Ip, s.Room, out string reason);
                lock (_lock) RemoveSession(s.Id);
                SendAdmin(s.EndPoint, w => Packets.WriteKicked(w, "Banned" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)));
                Emit($"Removed {s.Name} - banned on another server sharing this ban list.");
            }
            return offenders.Count;
        }

        public bool Kick(uint id, string reason)
        {
            IPEndPoint ep; string name;
            lock (_lock)
            {
                if (!_byId.TryGetValue(id, out var s)) return false;
                ep = s.EndPoint; name = s.Name;
                RemoveSession(id);
            }
            SendAdmin(ep, w => Packets.WriteKicked(w, string.IsNullOrEmpty(reason) ? "Kicked by host" : reason));
            Emit($"Kicked {name} (#{id}){(string.IsNullOrEmpty(reason) ? "" : ": " + reason)}.");
            return true;
        }

        /// <summary>
        /// Kicks and adds the client to the persistent ban list. <paramref name="room"/> 0 = global
        /// (master operator) ban; otherwise the ban applies only to that room (a host banning from
        /// their own session). Returns true if found.
        /// </summary>
        public bool Ban(uint id, string reason, ulong room = 0)
        {
            IPEndPoint ep; string name; ulong steamId; string ip;
            lock (_lock)
            {
                if (!_byId.TryGetValue(id, out var s)) return false;
                ep = s.EndPoint; name = s.Name; steamId = s.SteamId; ip = s.Ip;
                RemoveSession(id);
            }
            Bans.Add(steamId, ip, name, reason, room);
            SendAdmin(ep, w => Packets.WriteKicked(w, "Banned" + (string.IsNullOrEmpty(reason) ? "" : ": " + reason)));
            Emit($"Banned {name} (steam {steamId}, ip {ip}){(room != 0 ? $" from room {room}" : " globally")}{(string.IsNullOrEmpty(reason) ? "" : ": " + reason)}.");
            return true;
        }

        /// <summary>Removes a ban by Steam id or IP string. Returns count removed.</summary>
        public int Unban(string key)
        {
            int n = Bans.Remove(key);
            if (n > 0) Emit($"Unbanned {key} ({n} entr{(n == 1 ? "y" : "ies")}).");
            return n;
        }

        // ---------------- send helpers ----------------

        private void SendScratch(IPEndPoint to, Action<PacketWriter> build)
        {
            build(_scratch);
            try { _socket.SendTo(_scratch.Buffer, 0, _scratch.Length, SocketFlags.None, to); }
            catch (SocketException) { }
        }

        private void SendAdmin(IPEndPoint to, Action<PacketWriter> build)
        {
            if (to == null) return;
            lock (_adminLock)
            {
                build(_admin);
                try { _socket.SendTo(_admin.Buffer, 0, _admin.Length, SocketFlags.None, to); }
                catch (SocketException) { }
            }
        }

        private void Emit(string line) => Log?.Invoke(line);
    }
}
