using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NORS.Common;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Per-player listening volume, remembered between sessions.
    ///
    /// Purely local: it scales what *you* hear from someone, and nothing about it is sent
    /// anywhere. That makes it the personal counterpart to the host-authority mute/ban in
    /// <see cref="Net.HostBanStore"/> — every listener can turn down a player who runs hot
    /// without anyone needing moderator rights, and without kicking them.
    ///
    /// Identity is the problem this class exists to solve. The obvious key, VoiceHeader.ClientId,
    /// is a fresh <c>Guid.NewGuid().GetHashCode()</c> every launch, so a slider saved against it
    /// would reattach to a random stranger next session. Steam ids are permanent, but the relay
    /// roster reports 0 for them (only the game-derived roster carries them), and voice frames
    /// identify the talker by callsign. So:
    ///
    ///   * the hot path looks up the callsign, a single dictionary hit per frame;
    ///   * the file records the Steam id as the real identity when one is known;
    ///   * when a roster arrives, a known Steam id whose stored name has changed re-points the
    ///     callsign lookup, so a rename carries the setting with it rather than silently
    ///     resetting the player to full volume.
    /// </summary>
    internal sealed class PlayerVolumes
    {
        /// <summary>Loudest a player can be boosted to. Above ~1.5 the radio FX limiter just clips.</summary>
        public const float MaxGain = 1.5f;

        private sealed class Entry
        {
            public ulong SteamId;   // 0 when we have never seen one for this player
            public string Name;
            public float Gain;
        }

        private readonly string _path;

        /// <summary>Hot path: callsign -> gain. Only holds players we actually have a setting for.</summary>
        private readonly Dictionary<string, float> _live =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<ulong, Entry> _byId = new Dictionary<ulong, Entry>();
        private readonly Dictionary<string, Entry> _byName =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public PlayerVolumes(string path)
        {
            _path = path;
            Load();
        }

        public int Count => _byId.Count + _byName.Count;

        /// <summary>
        /// Gain for a talker, by the callsign carried in the voice frame. 1 = untouched, which is
        /// the answer for everyone the player has never adjusted.
        /// </summary>
        public float Get(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return 1f;
            return _live.TryGetValue(callsign, out float g) ? g : 1f;
        }

        public float GetFor(ulong steamId, string name)
        {
            if (steamId != 0 && _byId.TryGetValue(steamId, out var e)) return e.Gain;
            if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var n)) return n.Gain;
            return 1f;
        }

        /// <summary>
        /// Sets a player's gain and persists it. A gain of exactly 1 clears the entry instead of
        /// storing it, so the file stays the list of deliberate choices rather than growing a row
        /// for every player ever met.
        /// </summary>
        public void Set(ulong steamId, string name, float gain)
        {
            if (string.IsNullOrEmpty(name) && steamId == 0) return;
            gain = Clamp(gain);

            bool isDefault = Math.Abs(gain - 1f) < 0.001f;

            if (isDefault)
            {
                if (steamId != 0) _byId.Remove(steamId);
                if (!string.IsNullOrEmpty(name)) _byName.Remove(name);
                if (!string.IsNullOrEmpty(name)) _live.Remove(name);
            }
            else
            {
                var e = new Entry { SteamId = steamId, Name = name ?? "", Gain = gain };
                if (steamId != 0)
                {
                    _byId[steamId] = e;
                    // A player can only have one identity: drop any name-only row we had for them
                    // before their Steam id was known, or the two would drift apart.
                    if (!string.IsNullOrEmpty(name)) _byName.Remove(name);
                }
                else
                {
                    _byName[name] = e;
                }
                if (!string.IsNullOrEmpty(name)) _live[name] = gain;
            }

            Save();
        }

        /// <summary>
        /// Reconciles stored Steam ids against the current roster. Cheap and idempotent — the hub
        /// can call it on a timer. Picks up renames, and fills in the callsign lookup for players
        /// whose setting was loaded from disk before we knew what they were called.
        /// </summary>
        public void SyncRoster(List<RosterEntry> roster)
        {
            if (roster == null || roster.Count == 0) return;

            bool dirty = false;
            for (int i = 0; i < roster.Count; i++)
            {
                var p = roster[i];
                if (p.SteamId == 0 || string.IsNullOrEmpty(p.Name)) continue;
                if (!_byId.TryGetValue(p.SteamId, out var e)) continue;

                if (!string.Equals(e.Name, p.Name, StringComparison.Ordinal))
                {
                    // Renamed (or first time we have seen a name for this id).
                    if (!string.IsNullOrEmpty(e.Name)) _live.Remove(e.Name);
                    e.Name = p.Name;
                    dirty = true;
                }
                if (!_live.ContainsKey(p.Name)) _live[p.Name] = e.Gain;
            }

            if (dirty) Save();
        }

        private static float Clamp(float g)
        {
            if (g < 0f) return 0f;
            if (g > MaxGain) return MaxGain;
            return g;
        }

        // File format, one player per line, tab separated:
        //     <steamid>\t<gain>\t<name>
        // steamid is 0 when unknown. Invariant culture throughout, or a machine with a comma
        // decimal separator writes "0,35" and reads it back as 35.
        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    if (!ulong.TryParse(parts[0].Trim(), out ulong id)) continue;
                    if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float gain)) continue;

                    string name = parts[2];
                    var e = new Entry { SteamId = id, Name = name, Gain = Clamp(gain) };

                    if (id != 0) _byId[id] = e;
                    else if (!string.IsNullOrEmpty(name)) _byName[name] = e;
                    else continue;

                    if (!string.IsNullOrEmpty(name)) _live[name] = e.Gain;
                }
            }
            catch (Exception e)
            {
                NorsPlugin.Log.LogWarning("NORS: could not read player volumes: " + e.Message);
            }
        }

        private void Save()
        {
            try
            {
                var lines = new List<string>(_byId.Count + _byName.Count + 1)
                {
                    "# NORS per-player volume. steamid <tab> gain <tab> name. Delete a line to reset that player."
                };
                foreach (var e in _byId.Values) lines.Add(Format(e));
                foreach (var e in _byName.Values) lines.Add(Format(e));
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch (Exception e)
            {
                NorsPlugin.Log.LogWarning("NORS: could not save player volumes: " + e.Message);
            }
        }

        private static string Format(Entry e) =>
            e.SteamId.ToString(CultureInfo.InvariantCulture) + "\t" +
            e.Gain.ToString("0.###", CultureInfo.InvariantCulture) + "\t" +
            (e.Name ?? "").Replace('\t', ' ');
    }
}
