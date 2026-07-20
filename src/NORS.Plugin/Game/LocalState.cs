using NORS.Common;
using NuclearOption.Networking;
using UnityEngine;

namespace NORS.Plugin.Game
{
    /// <summary>
    /// Resolves everything NORS needs from the game each frame: the local player's identity,
    /// faction (for the secure-net key id), and physical position (for range + terrain LOS).
    /// Positions are exchanged in the game's origin-independent <see cref="GlobalPosition"/> frame
    /// so they stay consistent across clients despite the floating-origin shifts.
    /// </summary>
    internal sealed class LocalState
    {
        public bool InGame { get; private set; }
        public bool HasPosition { get; private set; }
        public Aircraft Aircraft { get; private set; }
        public FactionHQ HQ { get; private set; }
        public Faction Faction { get; private set; }

        public Vector3 WorldPos { get; private set; }
        public GlobalPosition Global { get; private set; }
        public float AltitudeMeters { get; private set; }

        /// <summary>Stable faction id (FNV-1a of the faction name) shared by all clients on that faction.</summary>
        public int FactionId { get; private set; }

        /// <summary>Stable per-player Steam id, used by the server for robust (NAT-proof) bans. 0 if unknown.</summary>
        public ulong SteamId { get; private set; }

        /// <summary>The game host's Steam id = the "server"/room this client belongs to on the relay. 0 if unknown.</summary>
        public ulong RoomId { get; private set; }

        /// <summary>True if the local player is the game host (and thus moderator of its room).</summary>
        public bool IsHost { get; private set; }

        public string Callsign { get; private set; } = "Pilot";

        /// <summary>Steam ids of the OTHER players in this game session (P2P voice targets).</summary>
        public readonly System.Collections.Generic.List<ulong> Peers = new System.Collections.Generic.List<ulong>();

        /// <summary>All players in the session (incl. self) for the P2P panel roster.</summary>
        public readonly System.Collections.Generic.List<RosterEntry> SessionRoster = new System.Collections.Generic.List<RosterEntry>();

        public void Resolve()
        {
            HasPosition = false;
            Aircraft = null;

            if (!GameManager.GetLocalPlayer<Player>(out var player) || player == null)
            {
                InGame = false;
                HQ = null; Faction = null; FactionId = 0;
                RoomId = 0; IsHost = false;
                return;
            }

            InGame = true;
            HQ = player.HQ;
            Faction = HQ != null ? HQ.faction : null;
            // Key the faction off the HQ's network id: it's unique per faction and identical across
            // all clients. Hashing the faction *name* proved unreliable (collided across factions).
            FactionId = HQ != null ? (int)HQ.NetId : 0;

            string overrideName = NorsConfig.PlayerNameOverride.Value;
            Callsign = !string.IsNullOrEmpty(overrideName)
                ? overrideName
                : (!string.IsNullOrEmpty(player.PlayerName) ? player.PlayerName : "Pilot");

            try { SteamId = player.CSteamID.m_SteamID; } catch { SteamId = 0; }

            IsHost = player.IsHostPlayer;
            RoomId = ResolveRoom(player);
            BuildSession(player);

            // Prefer the flown aircraft; fall back to a dismounted pilot if there is one.
            if (GameManager.GetLocalAircraft(out var ac) && ac != null && !ac.disabled)
            {
                Aircraft = ac;
                SetPosition(ac.transform.position);
            }
            else if (GameManager.GetLocalPilotDismounted(out var pilot) && pilot != null)
            {
                SetPosition(pilot.transform.position);
            }
            else if (NorsConfig.DeadTalkFromBase.Value)
            {
                // Dead / respawn screen: your radio operates from your faction's airbase, so you can
                // still coordinate — but only with people the base can actually reach. Without this
                // we'd transmit from GlobalPosition(0,0,0) (the world origin), which is nonsense.
                try
                {
                    if (HQ != null && HQ.TryGetNearestAirbase(HQ.transform.position, out var ab)
                        && ab != null && ab.center != null)
                        SetPosition(ab.center.position);
                    else if (HQ != null)
                        SetPosition(HQ.transform.position);
                }
                catch { }
            }
        }

        /// <summary>
        /// The room id is the game host's Steam id, shared by everyone in the session. If we're the
        /// host it's our own id; otherwise we find the host player (IsHostPlayer) in the registry.
        /// </summary>
        private static ulong ResolveRoom(Player local)
        {
            try
            {
                if (local.IsHostPlayer) return local.CSteamID.m_SteamID;
                foreach (var p in UnitRegistry.playerLookup.Values)
                    if (p != null && p.IsHostPlayer) return p.CSteamID.m_SteamID;
            }
            catch { }
            return 0;
        }

        /// <summary>Enumerate the session's players for P2P: peer Steam ids + a display roster.</summary>
        private void BuildSession(Player local)
        {
            Peers.Clear();
            SessionRoster.Clear();
            try
            {
                foreach (var p in UnitRegistry.playerLookup.Values)
                {
                    if (p == null) continue;
                    ulong sid = 0;
                    try { sid = p.CSteamID.m_SteamID; } catch { }
                    bool self = sid != 0 && sid == SteamId;
                    int fac = p.HQ != null ? (int)p.HQ.NetId : 0;
                    string nm = self ? Callsign : (!string.IsNullOrEmpty(p.PlayerName) ? p.PlayerName : "Pilot");
                    SessionRoster.Add(new RosterEntry { ClientId = 0, SteamId = sid, FactionId = fac, Name = nm });
                    if (sid != 0 && !self) Peers.Add(sid);
                }
            }
            catch { }
        }

        private void SetPosition(Vector3 world)
        {
            WorldPos = world;
            Global = world.ToGlobalPosition();
            AltitudeMeters = Global.y; // global y is altitude above the world datum (sea level)
            HasPosition = true;
        }
    }
}
