using System;
using System.Reflection;
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

        /// <summary>
        /// Legacy faction id: the HQ's network id. Kept ONLY so we stay intelligible to pre-0.7.6
        /// clients — do not gate on it when <see cref="StableFactionId"/> is available on both ends.
        /// A NetId is per-spawn runtime state, so two clients can disagree about it (mission
        /// reloads, respawns, join order), and secure channels then went silently one-way.
        /// </summary>
        public int FactionId { get; private set; }

        /// <summary>
        /// Faction identity that is identical on every client by construction: the game's own
        /// Encyclopedia lookup index for the Faction asset — the same value the game itself puts
        /// on the wire for a faction (see DefinitionWriters.WriteNetworkDefinition). Offset by 1
        /// so 0 always means "unknown". Content-defined, so it survives respawns, mission reloads
        /// and join order, which the HQ NetId does not.
        /// </summary>
        public int StableFactionId { get; private set; }

        /// <summary>Stable per-player Steam id, used by the server for robust (NAT-proof) bans. 0 if unknown.</summary>
        public ulong SteamId { get; private set; }

        /// <summary>The game host's Steam id = the "server"/room this client belongs to on the relay. 0 if unknown.</summary>
        public ulong RoomId { get; private set; }

        /// <summary>True if the local player is the game host (and thus moderator of its room).</summary>
        public bool IsHost { get; private set; }

        public string Callsign { get; private set; } = "Pilot";

        /// <summary>Steam ids of the OTHER players in this game session (P2P voice targets).</summary>
        public readonly System.Collections.Generic.List<ulong> Peers = new System.Collections.Generic.List<ulong>();

        /// <summary>Other players seen in the session (excluding us), regardless of Steam id.</summary>
        public int OtherPlayerCount;

        /// <summary>
        /// True when there are other players but NONE of them expose a Steam id, so Steam P2P
        /// has nobody to address. The game only replicates Player.CSteamID when the client
        /// authenticated over Steam transport or is the host — see <see cref="UdpTransport"/>.
        /// </summary>
        public bool PeersUnavailable => OtherPlayerCount > 0 && Peers.Count == 0;

        /// <summary>
        /// True when this session runs over the game's plain UDP socket instead of Steam's.
        /// This is the whole reason P2P voice dies on some servers: the server picks the auth
        /// path from its own socket factory, and the UDP path builds AuthData without a Steam
        /// id, so BasePlayer.SteamID is never set and never replicated to anyone. A dedicated
        /// server started with '-socket SteamGameServer' takes the Steam path instead and P2P
        /// works normally — so this flag lets us name the real cause instead of blaming
        /// "dedicated servers".
        /// </summary>
        public bool UdpTransport { get; private set; }

        /// <summary>
        /// The game server's address as this client is actually connected to it, or empty when it
        /// can't be known. Only available over the UDP socket: on a Steam socket the client
        /// connects to a Steam id and the real address is hidden behind Steam's relay network.
        /// That is exactly the right coverage, because a Steam-socket server shares Steam ids and
        /// therefore works on P2P anyway — it's the UDP servers that need to find a relay.
        /// </summary>
        public string ServerAddress { get; private set; } = "";

        /// <summary>The game server's port as connected, or 0 when unknown (Steam socket).</summary>
        public int ServerPort { get; private set; }

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
            FactionId = HQ != null ? (int)HQ.NetId : 0;   // legacy, for pre-0.7.6 peers only
            StableFactionId = ResolveStableFaction(Faction);

            string overrideName = NorsConfig.PlayerNameOverride.Value;
            Callsign = !string.IsNullOrEmpty(overrideName)
                ? overrideName
                : (!string.IsNullOrEmpty(PlayerNames.Get(player)) ? PlayerNames.Get(player) : "Pilot");

            try { SteamId = player.CSteamID.m_SteamID; } catch { SteamId = 0; }
            // On servers that don't use Steam transport the game never fills in CSteamID —
            // not even our own. Ask Steam directly so we at least know who *we* are
            // (peer discovery needs an id to announce).
            if (SteamId == 0)
            {
                try { SteamId = Steamworks.SteamUser.GetSteamID().m_SteamID; } catch { SteamId = 0; }
            }

            IsHost = player.IsHostPlayer;
            UdpTransport = ResolveUdpTransport();
            ResolveServerEndpoint();
            RoomId = ResolveRoomId(player);
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
        /// Pulls the connected address out of Mirage's UDP socket factory, which stores it in
        /// public Address/Port fields that NetworkClient.Connect fills in before connecting.
        /// Read reflectively so we don't take a hard reference on the transport assembly.
        /// </summary>
        private void ResolveServerEndpoint()
        {
            ServerAddress = ""; ServerPort = 0;
            try
            {
                var client = NetworkManagerNuclearOption.i?.Client;
                if (client == null) return;
                object socket = client.GetType()
                    .GetProperty("SocketFactory", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(client);
                if (socket == null) return;
                var type = socket.GetType();
                string addr = type.GetField("Address", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(socket) as string;
                object portObj = type.GetField("Port", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(socket);
                if (string.IsNullOrEmpty(addr)) return;
                // A locally hosted game reports localhost; there is no remote relay to find there.
                if (addr == "localhost" || addr == "127.0.0.1" || addr == "::1") return;
                ServerAddress = addr;
                if (portObj != null) ServerPort = Convert.ToInt32(portObj);
            }
            catch { ServerAddress = ""; ServerPort = 0; }
        }

        /// <summary>
        /// The Encyclopedia lookup index the game assigns to every Faction asset at load, offset
        /// by 1 so 0 stays reserved for "unknown". Assigned from the same ordered asset list on
        /// every client, so it needs no replication to agree.
        /// </summary>
        private static int ResolveStableFaction(Faction faction)
        {
            if (faction == null) return 0;
            try
            {
                int? index = (faction as INetworkDefinition)?.LookupIndex;
                return index.HasValue ? index.Value + 1 : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Which socket the client is actually running on. Matched by type name rather than by
        /// referencing the transport assembly, so a moved/renamed socket class degrades to
        /// "assume Steam" (no scary message) instead of failing to compile or throwing.
        /// </summary>
        private static bool ResolveUdpTransport()
        {
            try
            {
                var client = NetworkManagerNuclearOption.i?.Client;
                if (client == null) return false;
                // Read it reflectively: the property's type lives in Mirage.SocketLayer, and we'd
                // rather not take a hard reference on another assembly just to read a type name.
                object socket = client.GetType()
                    .GetProperty("SocketFactory", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(client);
                if (socket == null) return false;
                return socket.GetType().Name.IndexOf("Steam", System.StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Which voice room we belong to. Everyone on the same game server must agree, and it must
        /// never be 0 — the relay treats 0 as "not in a session yet" and forwards nothing, which is
        /// why relay voice was dead on dedicated servers: there is no host player there, so the old
        /// host-Steam-id room was always 0.
        ///
        /// Derived from the game server's endpoint, which every client on that server sees
        /// identically, so two servers sharing one relay can't hear each other. Falls back to the
        /// host's Steam id for player-hosted lobbies, where no endpoint is visible.
        /// </summary>
        private ulong ResolveRoomId(Player local)
        {
            if (!string.IsNullOrEmpty(ServerAddress))
                return NorsProtocol.RoomFor(ServerAddress, ServerPort);
            ulong host = ResolveRoom(local);
            return host != 0 ? host : 1UL;
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
            OtherPlayerCount = 0;
            try
            {
                foreach (var p in UnitRegistry.playerLookup.Values)
                {
                    if (p == null) continue;
                    ulong sid = 0;
                    try { sid = p.CSteamID.m_SteamID; } catch { }
                    // Without a Steam id we can't tell ourselves apart by id, so fall back to
                    // the Player reference — otherwise we'd count ourselves as "another player".
                    bool self = sid != 0 ? sid == SteamId : ReferenceEquals(p, local);
                    if (!self) OtherPlayerCount++;
                    int fac = p.HQ != null ? (int)p.HQ.NetId : 0;
                    string nm = self ? Callsign : (!string.IsNullOrEmpty(PlayerNames.Get(p)) ? PlayerNames.Get(p) : "Pilot");
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
