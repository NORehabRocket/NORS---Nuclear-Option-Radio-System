using System;
using System.Collections.Generic;
using System.IO;

namespace NORS.Plugin.Net
{
    /// <summary>
    /// The set of Steam ids a game host has muted/banned from voice, persisted on the host's machine.
    /// In P2P mode the host broadcasts this set; every client drops voice from those ids. Bans survive
    /// restarts; this is the host's own list (each host moderates its own sessions).
    /// </summary>
    internal sealed class HostBanStore
    {
        private readonly string _path;
        private readonly HashSet<ulong> _ids = new HashSet<ulong>();

        public HostBanStore(string path) { _path = path; Load(); }

        public int Count => _ids.Count;
        public bool Contains(ulong id) => _ids.Contains(id);

        public bool Add(ulong id)
        {
            if (id == 0 || !_ids.Add(id)) return false;
            Save();
            return true;
        }

        public bool Remove(ulong id)
        {
            if (!_ids.Remove(id)) return false;
            Save();
            return true;
        }

        public void CopyTo(List<ulong> outList)
        {
            outList.Clear();
            outList.AddRange(_ids);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path))
                    if (ulong.TryParse(line.Trim(), out var id) && id != 0) _ids.Add(id);
            }
            catch { }
        }

        private void Save()
        {
            try { File.WriteAllLines(_path, Array.ConvertAll(new List<ulong>(_ids).ToArray(), x => x.ToString())); }
            catch { }
        }
    }
}
