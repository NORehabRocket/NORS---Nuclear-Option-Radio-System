using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using NORS.Server.Core;

namespace NORS.Server
{
    /// <summary>
    /// Browser admin panel for the relay: roster, kick/ban/unban, bans list, live
    /// log and traffic counters — everything the console/WinForms admin can do,
    /// from any device. Password login (token sessions); SSE for live updates.
    /// Runs on HttpListener so the relay stays dependency-free and cross-platform.
    /// </summary>
    internal sealed class WebAdmin
    {
        private readonly RelayServer _server;
        private readonly string _password;
        private readonly DateTime _started = DateTime.UtcNow;

        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
        private readonly object _logLock = new();
        private readonly List<string> _log = new(260);
        private long _logRev;

        public string Url { get; private set; }

        public WebAdmin(RelayServer server, string password)
        {
            _server = server;
            _password = password;
            server.Log += OnLog;
        }

        private void OnLog(string line)
        {
            lock (_logLock)
            {
                _log.Add($"[{DateTime.UtcNow:HH:mm:ss}] {line}");
                if (_log.Count > 250) _log.RemoveRange(0, _log.Count - 250);
                _logRev++;
            }
        }

        public bool Start(int port, bool lan)
        {
            try
            {
                _listener = new HttpListener();
                string host = lan ? "*" : "localhost";
                _listener.Prefixes.Add($"http://{host}:{port}/");
                _listener.Start();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[NORS] Web admin failed to start on port {port}: {e.Message}");
                _listener = null;
                return false;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "nors-web" };
            _acceptThread.Start();
            Url = $"http://{(lan ? "<this-host>" : "localhost")}:{port}/";
            return true;
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Close(); } catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { if (!_running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(ctx); }
                    catch { try { ctx.Response.Close(); } catch { } }
                });
            }
        }

        // ------------------------------------------------------------------

        private bool Authed(HttpListenerContext ctx)
        {
            string token = ctx.Request.QueryString["token"];
            if (string.IsNullOrEmpty(token))
            {
                string h = ctx.Request.Headers["Authorization"];
                if (h != null && h.StartsWith("Bearer ")) token = h.Substring(7);
            }
            if (string.IsNullOrEmpty(token)) return false;
            if (!_tokens.TryGetValue(token, out DateTime exp)) return false;
            if (DateTime.UtcNow > exp) { _tokens.TryRemove(token, out _); return false; }
            return true;
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            string path = req.Url.AbsolutePath;
            res.Headers["Cache-Control"] = "no-store";

            if (path == "/")
            {
                ServeHtml(res);
                return;
            }

            if (path == "/api/login")
            {
                string pass = req.QueryString["pass"] ?? "";
                if (pass == _password)
                {
                    string token = Guid.NewGuid().ToString("N");
                    _tokens[token] = DateTime.UtcNow.AddHours(12);
                    Json(res, new { ok = true, token });
                }
                else
                {
                    Thread.Sleep(400); // slow brute force a little
                    Json(res, new { ok = false });
                }
                return;
            }

            if (!path.StartsWith("/api/") || !Authed(ctx))
            {
                res.StatusCode = path.StartsWith("/api/") ? 401 : 404;
                res.Close();
                return;
            }

            switch (path)
            {
                case "/api/state":
                    Json(res, BuildState());
                    return;

                case "/api/events":
                    Sse(ctx);
                    return;

                case "/api/kick":
                case "/api/ban":
                {
                    uint.TryParse(req.QueryString["id"], out uint id);
                    string reason = req.QueryString["reason"] ?? "";
                    bool ok = path == "/api/ban" ? _server.Ban(id, reason) : _server.Kick(id, reason);
                    OnLog($"web admin: {(path == "/api/ban" ? "ban" : "kick")} #{id:X8} {(ok ? "ok" : "failed")}" +
                          (reason.Length > 0 ? $" ({reason})" : ""));
                    Json(res, new { ok });
                    return;
                }

                case "/api/unban":
                {
                    string key = req.QueryString["key"] ?? "";
                    int n = _server.Unban(key);
                    OnLog($"web admin: unban '{key}' removed {n}");
                    Json(res, new { ok = n > 0, removed = n });
                    return;
                }

                default:
                    res.StatusCode = 404;
                    res.Close();
                    return;
            }
        }

        private object BuildState()
        {
            var clients = _server.GetClients();
            var bans = _server.Bans.Snapshot();
            List<string> log;
            long rev;
            lock (_logLock) { log = _log.ToList(); rev = _logRev; }

            return new
            {
                ok = true,
                name = _server.ServerName,
                port = _server.Port,
                running = _server.Running,
                uptimeSec = (long)(DateTime.UtcNow - _started).TotalSeconds,
                voteKick = _server.VoteKickEnabled,
                voiceIn = _server.VoiceReceived,
                voiceFwd = _server.VoiceForwarded,
                clients = clients.Select(c => new
                {
                    id = c.Id,
                    idHex = c.Id.ToString("X8"),
                    steamId = c.SteamId.ToString(),
                    room = c.Room.ToString(),
                    host = c.IsHost,
                    name = c.Name,
                    faction = c.FactionId,
                    ip = c.Ip,
                    freqs = c.FreqCount,
                    idle = Math.Round(c.IdleSeconds, 1),
                }),
                bans = bans.Select(b => new
                {
                    key = b.Key,
                    steamId = b.SteamId.ToString(),
                    ip = b.Ip,
                    name = b.Name,
                    reason = b.Reason,
                }),
                log,
                logRev = rev,
            };
        }

        private void Sse(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            res.ContentType = "text/event-stream";
            res.SendChunked = true;
            res.KeepAlive = true;
            var stream = res.OutputStream;

            try
            {
                while (_running)
                {
                    byte[] data = Encoding.UTF8.GetBytes(
                        "data: " + JsonSerializer.Serialize(BuildState()) + "\n\n");
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                    Thread.Sleep(2000);
                }
            }
            catch { /* client went away */ }
            finally { try { res.Close(); } catch { } }
        }

        private static void Json(HttpListenerResponse res, object o)
        {
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(o);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
            res.Close();
        }

        private void ServeHtml(HttpListenerResponse res)
        {
            byte[] html;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("NORS.Server.admin.html"))
            {
                if (s == null)
                {
                    html = Encoding.UTF8.GetBytes("<html><body>admin.html resource missing</body></html>");
                }
                else
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    html = ms.ToArray();
                }
            }
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = html.Length;
            res.OutputStream.Write(html, 0, html.Length);
            res.Close();
        }
    }
}
