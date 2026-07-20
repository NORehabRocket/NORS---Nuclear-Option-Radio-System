using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NORS.Server.Core
{
    public sealed class BanEntry
    {
        public ulong SteamId;     // 0 = match by IP instead
        public string Ip;
        public string Name;
        public string Reason;
        public DateTime WhenUtc;
        public ulong Room;        // 0 = global (master) ban; otherwise applies only to that room (host SteamID)

        public string Key => SteamId != 0 ? SteamId.ToString() : Ip;
    }

    /// <summary>
    /// Persistent ban list. Prefers banning by stable Steam id (NAT/reconnect proof); falls back to
    /// IP when no Steam id is known. Stored as a simple pipe-delimited text file next to the server.
    /// </summary>
    public sealed class BanList
    {
        private readonly List<BanEntry> _entries = new List<BanEntry>();
        private readonly string _path;
        private readonly object _lock = new object();

        public BanList(string path)
        {
            _path = path;
            Load();
        }

        /// <summary>Banned if a global entry (Room==0) matches, or a room-scoped entry for <paramref name="room"/> matches.</summary>
        public bool IsBanned(ulong steamId, string ip, ulong room, out string reason)
        {
            lock (_lock)
            {
                foreach (var e in _entries)
                {
                    if (e.Room != 0 && e.Room != room) continue;   // room ban that isn't this room
                    if (e.SteamId != 0 && e.SteamId == steamId) { reason = e.Reason; return true; }
                    if (e.SteamId == 0 && !string.IsNullOrEmpty(ip) && e.Ip == ip) { reason = e.Reason; return true; }
                }
            }
            reason = null;
            return false;
        }

        public BanEntry Add(ulong steamId, string ip, string name, string reason, ulong room)
        {
            lock (_lock)
            {
                // De-dupe on the effective key within the same scope.
                string key = steamId != 0 ? steamId.ToString() : ip;
                _entries.RemoveAll(e => e.Key == key && e.Room == room);

                var entry = new BanEntry
                {
                    SteamId = steamId,
                    Ip = ip,
                    Name = name,
                    Reason = string.IsNullOrEmpty(reason) ? "" : reason,
                    WhenUtc = DateTime.UtcNow,
                    Room = room,
                };
                _entries.Add(entry);
                Save();
                return entry;
            }
        }

        /// <summary>Removes ban(s) matching a key that is either a Steam id or an IP. Returns count removed.</summary>
        public int Remove(string key)
        {
            lock (_lock)
            {
                int removed;
                if (ulong.TryParse(key, out var sid))
                    removed = _entries.RemoveAll(e => e.SteamId == sid || e.Ip == key);
                else
                    removed = _entries.RemoveAll(e => e.Ip == key);
                if (removed > 0) Save();
                return removed;
            }
        }

        public List<BanEntry> Snapshot()
        {
            lock (_lock) return new List<BanEntry>(_entries);
        }

        public int Count { get { lock (_lock) return _entries.Count; } }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var p = line.Split('|');
                    if (p.Length < 4) continue;
                    _entries.Add(new BanEntry
                    {
                        SteamId = ulong.TryParse(p[0], out var sid) ? sid : 0,
                        Ip = p[1],
                        Name = p[2],
                        Reason = p[3],
                        WhenUtc = p.Length > 4 && DateTime.TryParse(p[4], out var dt) ? dt : DateTime.UtcNow,
                        Room = p.Length > 5 && ulong.TryParse(p[5], out var rm) ? rm : 0,  // old files: global
                    });
                }
            }
            catch { /* ignore a malformed ban file */ }
        }

        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# NORS ban list  —  steamId|ip|name|reason|utc|room (room 0 = global)");
                foreach (var e in _entries)
                    sb.AppendLine($"{e.SteamId}|{e.Ip}|{Sanitize(e.Name)}|{Sanitize(e.Reason)}|{e.WhenUtc:o}|{e.Room}");
                File.WriteAllText(_path, sb.ToString());
            }
            catch { /* best effort */ }
        }

        private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }
}
