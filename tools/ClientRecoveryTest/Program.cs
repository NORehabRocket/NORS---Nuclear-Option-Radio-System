using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using NORS.Common;
using NORS.Plugin.Net;
using NORS.Server.Core;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const uint Id = 100;
    private const string Password = "offline-test-only";
    private static int passed, failed, assertions;

    public static int Main()
    {
        try
        {
            string own = Command("readlink", "/proc/self/ns/net");
            if (own == Command("readlink", "/proc/1/ns/net"))
                throw new Exception("REFUSED: network namespace equals PID 1 namespace");
            Command("ip", "link set lo up");
            Console.WriteLine("ISOLATED_NETNS=" + own);
            Case("immediate and bounded connected Hello heartbeat", Heartbeats);
            Case("relay restart recovers without State admission", Restart);
            Case("expired session recovers without State admission", Timeout);
            Case("Hello revocation and reauthentication", () => Revocation(true));
            Case("State revocation and reauthentication", () => Revocation(false));
            Case("kicked heartbeat suppression and explicit restart", Kicked);
            Console.WriteLine($"RESULT passed={passed} failed={failed} assertions={assertions}");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.WriteLine("HARNESS_ERROR " + e); return 2; }
    }

    private static string Command(string command, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(command, arguments) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        });
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception(command + ": " + error);
        return output.Trim();
    }

    private static void Case(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static void Await(Func<bool> condition, string message)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > 2500) { Check(false, message); return; }
            Thread.Sleep(5);
        }
        Check(true, message);
    }

    private sealed class Fixture : IDisposable
    {
        internal RelayServer Server;
        internal readonly VoiceClient Client = new VoiceClient();
        internal readonly string Dir = Path.Combine(Path.GetTempPath(), "nors-client-recovery-" + Guid.NewGuid());
        internal readonly int Port;

        internal Fixture()
        {
            Directory.CreateDirectory(Dir);
            Server = MakeServer(0);
            var socket = (Socket)typeof(RelayServer).GetField("_socket", Private).GetValue(Server);
            Port = ((IPEndPoint)socket.LocalEndPoint).Port;
            Server.Start();
            Client.Start("127.0.0.1", Port, Id);
        }

        private RelayServer MakeServer(int port) => new RelayServer(port, "Recovery test",
            Path.Combine(Dir, "bans.txt"), Password, false);

        internal void Heartbeat(double now, string name = "Pilot", ulong steam = 1000,
            ulong room = 1, int faction = 1) => Client.MaintainSession(now, steam, room, false, faction, name);

        internal void Join()
        {
            Heartbeat(0);
            Await(() => Client.Connected && Server.ClientCount == 1, "initial Hello admission failed");
        }

        internal void State(ulong steam = 1000, ulong room = 1, int faction = 1) =>
            Client.SendState(steam, room, false, faction, 0, 0, 0, new[] { 251000 }, 1, "Pilot");

        internal void Login()
        {
            Client.SendAdminAuth(Password);
            Await(() => Client.AdminAuthed, "password reauthentication failed");
        }

        internal bool Notice(string text)
        {
            while (Client.TryDequeueNotice(out var notice))
                if (notice.Contains(text)) return true;
            return false;
        }

        internal void AssertStateCannotAdmit()
        {
            State();
            // The subsequent reply is a receive-loop barrier, not a fixed sleep.
            Client.SendAdminAuth(Password);
            Await(() => Notice("Session not found"), "State admitted an unknown client or auth reply missing");
            Check(Server.ClientCount == 0, "State recreated membership without Hello");
        }

        internal void RestartRelay()
        {
            Server.Stop();
            Server = MakeServer(Port);
            Server.Start();
        }

        internal void Expire()
        {
            var gate = typeof(RelayServer).GetField("_lock", Private).GetValue(Server);
            lock (gate)
            {
                var sessions = (Dictionary<uint, ClientSession>)typeof(RelayServer)
                    .GetField("_byId", Private).GetValue(Server);
                sessions[Id].LastSeen = Server.Now - NorsProtocol.ClientTimeoutSeconds - 1;
            }
            Await(() => Server.ClientCount == 0, "real maintenance sweep did not expire session");
        }

        public void Dispose()
        {
            Client.Stop();
            Server.Stop();
            Directory.Delete(Dir, true);
        }
    }

    private static void Heartbeats()
    {
        using var f = new Fixture();
        f.Join();
        f.Heartbeat(NorsProtocol.HelloRetryInterval / 2.0, "Too early");
        f.Client.SendAdminAuth("wrong");
        Await(() => f.Notice("Incorrect admin password"), "heartbeat ordering barrier missing");
        Check(f.Server.GetClients()[0].Name == "Pilot", "heartbeat ignored retry interval");
        f.Heartbeat(NorsProtocol.HelloRetryInterval, "Refreshed");
        Await(() => f.Server.GetClients()[0].Name == "Refreshed", "connected client did not refresh Hello");
        Check(f.Client.Connected, "heartbeat disconnected client");
        f.Login();
        f.Heartbeat(NorsProtocol.HelloRetryInterval * 2, "Same identity");
        f.Client.SendAdminCommand(AdminOp.Kick, 999, "absent target");
        Await(() => f.Notice("Target not found"), "unchanged heartbeat revoked server authority");
        Check(f.Client.AdminAuthed, "unchanged heartbeat revoked cached authority");
    }

    private static void Restart()
    {
        using var f = new Fixture();
        f.Join();
        f.RestartRelay();
        Check(f.Client.Connected, "fixture unexpectedly reset client on relay restart");
        f.AssertStateCannotAdmit();
        f.Heartbeat(NorsProtocol.HelloRetryInterval);
        Await(() => f.Server.ClientCount == 1, "connected client failed to rejoin restarted relay");
        f.State();
        Await(() => f.Server.GetClients()[0].FreqCount == 1, "state failed after restart admission");
        Check(!f.Client.Kicked, "restart recovery became a kick");
    }

    private static void Timeout()
    {
        using var f = new Fixture();
        f.Join();
        f.Expire();
        Check(f.Client.Connected, "fixture unexpectedly reset client after server timeout");
        f.AssertStateCannotAdmit();
        f.Heartbeat(NorsProtocol.HelloRetryInterval);
        Await(() => f.Server.ClientCount == 1, "connected client failed to recover expired session");
        f.State();
        Await(() => f.Server.GetClients()[0].FreqCount == 1, "state failed after timeout recovery");
    }

    private static void Revocation(bool hello)
    {
        using var f = new Fixture();
        f.Join();
        ulong steam = 1000, room = 1;
        int faction = 1;
        for (int change = 0; change < 3; change++)
        {
            f.Login();
            if (change == 0) steam++;
            else if (change == 1) room++;
            else faction++;
            if (hello) f.Heartbeat((change + 1) * NorsProtocol.HelloRetryInterval, steam: steam, room: room, faction: faction);
            else f.State(steam, room, faction);
            Await(() => !f.Client.AdminAuthed, "membership change did not revoke cached client authority");
            Check(f.Server.ClientCount == 1 && f.Client.Connected, "revocation removed session");

            // Model a lost notification by restoring only the client's stale cached flag.
            f.Client.AdminAuthed = true;
            f.Client.SendAdminCommand(AdminOp.Kick, Id, "must be denied");
            Await(() => !f.Client.AdminAuthed, "denial did not repair stale cached authority");
            Check(f.Server.ClientCount == 1 && !f.Client.Kicked, "revoked admin command executed");
            f.Login();
            Check(f.Client.AdminAuthed, "client cannot reauthenticate after revocation");
        }
    }

    private static void Kicked()
    {
        using var f = new Fixture();
        f.Join();
        Check(f.Server.Kick(Id, "test kick"), "operator kick failed");
        Await(() => f.Client.Kicked && !f.Client.Connected, "real client did not observe kick");
        f.Heartbeat(NorsProtocol.HelloRetryInterval * 10);
        f.Client.SendAdminAuth(Password);
        Await(() => f.Notice("Session not found"), "kicked heartbeat readmitted client or reply missing");
        Check(f.Server.ClientCount == 0, "kicked client was readmitted automatically");
        f.Client.Start("127.0.0.1", f.Port, Id);
        f.Heartbeat(0);
        Await(() => f.Client.Connected && f.Server.ClientCount == 1, "Start failed to reset heartbeat timestamp");
        Check(!f.Client.Kicked, "explicit reconnect retained kick flag");
    }
}
