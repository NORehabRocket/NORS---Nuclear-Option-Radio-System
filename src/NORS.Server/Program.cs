using System;
using System.IO;
using NORS.Common;
using NORS.Server.Core;

namespace NORS.Server
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            int port = NorsProtocol.DefaultPort;
            string name = "NORS Relay";
            string adminPass = null;
            bool voteKick = true;
            bool headless = false;
            int webPort = 8700;
            bool web = true;
            bool webLan = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port" or "-p" when i + 1 < args.Length: int.TryParse(args[++i], out port); break;
                    case "--name" or "-n" when i + 1 < args.Length: name = args[++i]; break;
                    case "--admin-pass" or "-a" when i + 1 < args.Length: adminPass = args[++i]; break;
                    case "--no-votekick": voteKick = false; break;
                    case "--headless": headless = true; break;
                    case "--web-port" when i + 1 < args.Length: int.TryParse(args[++i], out webPort); break;
                    case "--web-lan": webLan = true; break;
                    case "--no-web": web = false; break;
                    case "--help" or "-h":
                        Console.WriteLine("NORS relay server\n" +
                            "  --port <n>        UDP port (default 5555)\n" +
                            "  --name <s>        server name\n" +
                            "  --admin-pass <s>  admin password (in-game remote admin + web panel login)\n" +
                            "  --no-votekick     disable player vote-kick\n" +
                            "  --headless        run without the interactive console\n" +
                            "  --web-port <n>    web admin panel port (default 8700)\n" +
                            "  --web-lan         web panel reachable from other machines (default: localhost only)\n" +
                            "  --no-web          disable the web admin panel");
                        return;
                }
            }

            Console.WriteLine("============================================");
            Console.WriteLine(" Nuclear Option Radio System - Relay Server ");
            Console.WriteLine($" protocol v{NorsProtocol.Version}");
            Console.WriteLine("============================================");

            string banFile = Path.Combine(AppContext.BaseDirectory, "nors-bans.txt");

            RelayServer server;
            try
            {
                server = new RelayServer(port, name, banFile, adminPass, voteKick);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to bind UDP {port}: {e.Message}");
                Console.Error.WriteLine("Is another instance running, or the port blocked by the firewall?");
                Environment.ExitCode = 1;
                return;
            }

            server.Log += line => Console.WriteLine($"[NORS] {line}");
            server.Start();
            Console.WriteLine($"[NORS] Vote-kick: {(voteKick ? "on" : "off")}   Remote admin: {(server.RemoteAdminEnabled ? "enabled" : "disabled")}");

            WebAdmin webAdmin = null;
            if (web)
            {
                // The web panel always has a password: use --admin-pass, or mint one
                string webPass = adminPass;
                if (string.IsNullOrEmpty(webPass))
                {
                    webPass = Guid.NewGuid().ToString("N").Substring(0, 8);
                    Console.WriteLine($"[NORS] Web admin password (auto-generated, use --admin-pass to set one): {webPass}");
                }
                webAdmin = new WebAdmin(server, webPass);
                if (webAdmin.Start(webPort, webLan))
                    Console.WriteLine($"[NORS] Web admin panel: {webAdmin.Url}" +
                        (webLan ? "   (LAN-exposed: anyone with the password can administer)" : "   (localhost only; --web-lan to expose)"));
            }

            if (headless || Console.IsInputRedirected)
            {
                Console.WriteLine("[NORS] Headless mode. Press Ctrl+C to stop.");
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                return;
            }

            PrintHelp();
            RunConsole(server);
            webAdmin?.Stop();
            server.Stop();
        }

        private static void RunConsole(RelayServer server)
        {
            while (true)
            {
                Console.Write("> ");
                string line = Console.ReadLine();
                if (line == null) { System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); return; }
                line = line.Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLowerInvariant();

                try
                {
                    switch (cmd)
                    {
                        case "list" or "ls" or "who": ListClients(server); break;
                        case "bans": ListBans(server); break;
                        case "kick": AdminAct(server, parts, ban: false); break;
                        case "ban": AdminAct(server, parts, ban: true); break;
                        case "unban":
                            if (parts.Length < 2) { Console.WriteLine("usage: unban <steamId|ip>"); break; }
                            int n = server.Unban(parts[1]);
                            Console.WriteLine(n > 0 ? $"Removed {n} ban(s)." : "No matching ban.");
                            break;
                        case "stats":
                            Console.WriteLine($"clients={server.ClientCount}  voiceIn={server.VoiceReceived}  forwarded={server.VoiceForwarded}  bans={server.Bans.Count}");
                            break;
                        case "help" or "?": PrintHelp(); break;
                        case "quit" or "exit" or "stop": return;
                        default: Console.WriteLine($"Unknown command '{cmd}'. Type 'help'."); break;
                    }
                }
                catch (Exception e) { Console.WriteLine("Error: " + e.Message); }
            }
        }

        private static void AdminAct(RelayServer server, string[] parts, bool ban)
        {
            if (parts.Length < 2)
            {
                Console.WriteLine($"usage: {(ban ? "ban" : "kick")} <id|name> [reason]");
                return;
            }
            string reason = parts.Length >= 3 ? parts[2] : "";
            if (!ResolveClient(server, parts[1], out uint id, out string name))
            {
                Console.WriteLine($"No connected client matching '{parts[1]}'. Use 'list'.");
                return;
            }
            bool ok = ban ? server.Ban(id, reason) : server.Kick(id, reason);
            Console.WriteLine(ok ? $"{(ban ? "Banned" : "Kicked")} {name}." : "Client vanished.");
        }

        /// <summary>Accepts a numeric client id or a (case-insensitive) name match.</summary>
        private static bool ResolveClient(RelayServer server, string token, out uint id, out string name)
        {
            id = 0; name = null;
            var clients = server.GetClients();
            if (uint.TryParse(token, out uint parsed))
            {
                foreach (var c in clients)
                    if (c.Id == parsed) { id = c.Id; name = c.Name; return true; }
            }
            foreach (var c in clients)
                if (string.Equals(c.Name, token, StringComparison.OrdinalIgnoreCase)) { id = c.Id; name = c.Name; return true; }
            return false;
        }

        private static void ListClients(RelayServer server)
        {
            var clients = server.GetClients();
            if (clients.Count == 0) { Console.WriteLine("(no clients connected)"); return; }
            Console.WriteLine($"{"ID",-10} {"NAME",-18} {"ROOM (host)",-20} {"FAC",-5} {"IP",-16} {"FRQ",3} {"IDLE",6}");
            foreach (var c in clients)
                Console.WriteLine($"{c.Id,-10:X8} {Trunc((c.IsHost ? "*" : "") + c.Name, 18),-18} {c.Room,-20} {c.FactionId,-5} {c.Ip,-16} {c.FreqCount,3} {c.IdleSeconds,5:0.0}s");
        }

        private static void ListBans(RelayServer server)
        {
            var bans = server.Bans.Snapshot();
            if (bans.Count == 0) { Console.WriteLine("(no bans)"); return; }
            Console.WriteLine($"{"STEAMID",-20} {"IP",-16} {"NAME",-20} REASON");
            foreach (var b in bans)
                Console.WriteLine($"{b.SteamId,-20} {b.Ip,-16} {Trunc(b.Name, 20),-20} {b.Reason}");
        }

        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        private static void PrintHelp()
        {
            Console.WriteLine(
                "Commands:\n" +
                "  list                 show connected clients (with their IDs)\n" +
                "  kick  <id|name> [r]  disconnect a client (can rejoin)\n" +
                "  ban   <id|name> [r]  disconnect + ban (by Steam id, IP fallback)\n" +
                "  unban <steamId|ip>   remove a ban\n" +
                "  bans                 list bans\n" +
                "  stats                traffic counters\n" +
                "  help                 this help\n" +
                "  quit                 stop the server");
        }
    }
}
