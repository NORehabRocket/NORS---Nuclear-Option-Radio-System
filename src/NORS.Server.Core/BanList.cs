using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

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
    /// IP when no Steam id is known. Stored as a simple pipe-delimited text file.
    ///
    /// The file doubles as the sync mechanism between servers: point several relays at the same
    /// path — a shared folder on one machine, or the same bind mount in several Docker containers —
    /// and a ban on any of them applies to all of them, with no central service and nothing to
    /// authenticate. Writes take an exclusive lock and re-read before modifying, so two servers
    /// banning at the same moment can't overwrite each other's entry; readers poll for changes.
    /// </summary>
    public sealed class BanList
    {
        /// <summary>How long to keep trying for the file lock before giving up on a write.</summary>
        private const int LockAttempts = 20;
        private const int LockWaitMs = 25;

        private readonly List<BanEntry> _entries = new List<BanEntry>();
        private readonly string _path;
        private readonly object _lock = new object();

        private DateTime _seenStampUtc;
        private long _seenLength = -1;

        /// <summary>Raised when the file changed underneath us — i.e. another server banned someone.</summary>
        public event Action ExternallyChanged;

        public BanList(string path)
        {
            _path = path;
            lock (_lock) { ReadFromDisk(); }
        }

        public string Path => _path;

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
            BanEntry entry = null;
            Mutate(list =>
            {
                // De-dupe on the effective key within the same scope.
                string key = steamId != 0 ? steamId.ToString() : ip;
                list.RemoveAll(e => e.Key == key && e.Room == room);

                entry = new BanEntry
                {
                    SteamId = steamId,
                    Ip = ip,
                    Name = name,
                    Reason = string.IsNullOrEmpty(reason) ? "" : reason,
                    WhenUtc = DateTime.UtcNow,
                    Room = room,
                };
                list.Add(entry);
            });
            return entry;
        }

        /// <summary>Removes ban(s) matching a key that is either a Steam id or an IP. Returns count removed.</summary>
        public int Remove(string key)
        {
            int removed = 0;
            Mutate(list =>
            {
                if (ulong.TryParse(key, out var sid))
                    removed = list.RemoveAll(e => e.SteamId == sid || e.Ip == key);
                else
                    removed = list.RemoveAll(e => e.Ip == key);
            });
            return removed;
        }

        /// <summary>
        /// Picks up bans written by another server sharing this file. Cheap enough to call on a
        /// timer: it only touches the disk when the file's timestamp or length actually moved.
        /// </summary>
        public bool SyncFromDisk()
        {
            lock (_lock)
            {
                if (!FileChanged()) return false;
                ReadFromDisk();
            }
            try { ExternallyChanged?.Invoke(); } catch { }
            return true;
        }

        public List<BanEntry> Snapshot()
        {
            lock (_lock) return new List<BanEntry>(_entries);
        }

        public int Count { get { lock (_lock) return _entries.Count; } }

        // ---------------- internals ----------------

        /// <summary>
        /// Read-modify-write under an exclusive file lock, so a concurrent ban on another server
        /// is merged rather than lost. We re-read inside the lock precisely because our in-memory
        /// copy may be stale by the time someone clicks Ban.
        /// </summary>
        private void Mutate(Action<List<BanEntry>> change)
        {
            lock (_lock)
            {
                FileStream fs = OpenExclusive();
                if (fs == null)
                {
                    // Couldn't get the lock: still apply locally so moderation isn't blocked by a
                    // busy share, and let the next sync reconcile.
                    change(_entries);
                    return;
                }
                try
                {
                    var fresh = new List<BanEntry>();
                    Parse(ReadAllText(fs), fresh);
                    change(fresh);

                    fs.SetLength(0);
                    fs.Position = 0;
                    byte[] bytes = Encoding.UTF8.GetBytes(Render(fresh));
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);

                    _entries.Clear();
                    _entries.AddRange(fresh);
                }
                catch { change(_entries); }
                finally { try { fs.Dispose(); } catch { } }

                StampFile();
            }
        }

        private FileStream OpenExclusive()
        {
            for (int i = 0; i < LockAttempts; i++)
            {
                try
                {
                    return new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) { Thread.Sleep(LockWaitMs); }        // another server is writing
                catch (UnauthorizedAccessException) { return null; }
            }
            return null;
        }

        private bool FileChanged()
        {
            try
            {
                var fi = new FileInfo(_path);
                if (!fi.Exists) return _seenLength > 0;   // deleted out from under us
                return fi.LastWriteTimeUtc != _seenStampUtc || fi.Length != _seenLength;
            }
            catch { return false; }
        }

        private void StampFile()
        {
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists) { _seenStampUtc = fi.LastWriteTimeUtc; _seenLength = fi.Length; }
            }
            catch { }
        }

        private void ReadFromDisk()
        {
            try
            {
                _entries.Clear();
                if (!File.Exists(_path)) { _seenLength = 0; return; }
                for (int i = 0; i < LockAttempts; i++)
                {
                    try
                    {
                        using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            Parse(ReadAllText(fs), _entries);
                        break;
                    }
                    catch (IOException) { Thread.Sleep(LockWaitMs); }   // mid-write, try again
                }
            }
            catch { /* ignore a malformed or unreadable ban file */ }
            StampFile();
        }

        private static string ReadAllText(FileStream fs)
        {
            fs.Position = 0;
            using (var sr = new StreamReader(fs, Encoding.UTF8, true, 4096, leaveOpen: true))
                return sr.ReadToEnd();
        }

        private static void Parse(string text, List<BanEntry> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Split('\n'))
            {
                string l = line.Trim('\r', ' ', '\t');
                if (string.IsNullOrEmpty(l) || l.StartsWith("#")) continue;
                var p = l.Split('|');
                if (p.Length < 4) continue;
                into.Add(new BanEntry
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

        private static string Render(List<BanEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("# NORS ban list  -  steamId|ip|name|reason|utc|room (room 0 = global)\n");
            sb.Append("# Shared by every server pointed at this file; edits are picked up within seconds.\n");
            foreach (var e in entries)
                sb.Append($"{e.SteamId}|{e.Ip}|{Sanitize(e.Name)}|{Sanitize(e.Reason)}|{e.WhenUtc:o}|{e.Room}\n");
            return sb.ToString();
        }

        private static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }
}
