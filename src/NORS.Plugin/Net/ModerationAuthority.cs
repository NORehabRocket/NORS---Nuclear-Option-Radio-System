using System;
using System.Collections.Generic;

namespace NORS.Plugin.Net
{
    /// <summary>
    /// Decides whose mute/ban decisions this client obeys, and whether this client may issue them.
    ///
    /// Until 0.7.6 the answer was simply "the game host": the host broadcast its ban set and clients
    /// honoured whatever arrived from the host's Steam id. That has no answer at all on a dedicated
    /// server, because the host connection there is the server process — <c>Player.IsHostPlayer</c>
    /// is false for every real player (NetworkManagerNuclearOption sets it from
    /// <c>networkPlayer.IsHost</c>), so nobody could send a ban list and nobody would have honoured
    /// one. Voice moderation was silently unavailable on exactly the servers that need it most.
    ///
    /// So authority is now a trust list instead of a network position: a set of Steam ids in config,
    /// which a community ships with its modpack. Each client independently decides whose bans it
    /// obeys, which means no one can mute a server just by being in it — they have to already be
    /// trusted by the people who'd be affected. The game host stays an implicit authority so
    /// player-hosted lobbies behave exactly as before with no configuration.
    ///
    /// Ban sets are tracked per authority and unioned, so two moderators acting at once add up
    /// instead of overwriting each other.
    /// </summary>
    internal sealed class ModerationAuthority
    {
        private readonly HashSet<ulong> _trusted = new HashSet<ulong>();
        private readonly Dictionary<ulong, HashSet<ulong>> _received = new Dictionary<ulong, HashSet<ulong>>();
        private readonly HashSet<ulong> _dedup = new HashSet<ulong>();
        private readonly List<ulong> _stale = new List<ulong>();
        private string _parsedFrom;

        /// <summary>Bumped whenever the effective ban set could have changed, so the hub can
        /// rebuild only then instead of every frame.</summary>
        public int Revision { get; private set; }

        /// <summary>Steam ids configured as moderators (does not include the implicit game host).</summary>
        public IReadOnlyCollection<ulong> Trusted => _trusted;

        /// <summary>
        /// Re-reads the configured moderator list. Cheap to call every frame: it only reparses when
        /// the config string actually changed, so editing it in F1 takes effect without a restart.
        /// </summary>
        public void Sync(string configValue)
        {
            configValue = configValue ?? "";
            if (configValue == _parsedFrom) return;
            _parsedFrom = configValue;
            Revision++;

            _trusted.Clear();
            foreach (string part in configValue.Split(',', ';', ' ', '\t', '\n', '\r'))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;
                if (ulong.TryParse(s, out ulong id) && id != 0) _trusted.Add(id);
                else NorsPlugin.Log.LogWarning($"NORS: ignoring unparseable moderator entry '{s}' (expected a Steam id).");
            }

            // Bans from someone who just lost trust must stop applying immediately.
            _stale.Clear();
            foreach (var kv in _received) if (!IsAuthority(kv.Key, 0)) _stale.Add(kv.Key);
            foreach (ulong id in _stale) _received.Remove(id);

            if (_trusted.Count > 0)
                NorsPlugin.Log.LogInfo($"NORS: honouring voice bans from {_trusted.Count} configured moderator(s).");
        }

        /// <summary>
        /// True if <paramref name="steamId"/> may moderate. <paramref name="hostId"/> is the game
        /// host's Steam id (0 on a dedicated server, where there is no host player).
        /// </summary>
        public bool IsAuthority(ulong steamId, ulong hostId)
        {
            if (steamId == 0) return false;
            if (hostId != 0 && steamId == hostId) return true;   // player-hosted: unchanged behaviour
            return _trusted.Contains(steamId);
        }

        /// <summary>Record a ban set from an authority. Ignored (and reported) from anyone else.</summary>
        public bool Accept(ulong from, ulong[] ids, ulong hostId)
        {
            if (!IsAuthority(from, hostId))
            {
                NorsPlugin.Log.LogWarning(
                    $"NORS: ignored a voice ban list from {from} — not a configured moderator. " +
                    "Add their Steam id to Moderation/Moderators if they should be one.");
                return false;
            }
            if (!_received.TryGetValue(from, out var set)) _received[from] = set = new HashSet<ulong>();
            set.Clear();
            if (ids != null) foreach (ulong id in ids) if (id != 0) set.Add(id);
            Revision++;
            return true;
        }

        /// <summary>Everyone banned by any authority, plus this client's own list, unioned.</summary>
        public void BuildEffective(HostBanStore own, bool ownIsAuthority, List<ulong> outList)
        {
            outList.Clear();
            _dedup.Clear();
            if (ownIsAuthority && own != null)
            {
                own.CopyTo(outList);
                foreach (ulong id in outList) _dedup.Add(id);
            }
            foreach (var kv in _received)
                foreach (ulong id in kv.Value)
                    if (_dedup.Add(id)) outList.Add(id);
        }

        /// <summary>Session-scoped: a new session must not inherit the last one's authorities.</summary>
        public void ClearReceived() { _received.Clear(); Revision++; }
    }
}
