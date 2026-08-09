using System;
using System.Collections.Generic;
using NORS.Common;
using Steamworks;

namespace NORS.Plugin.Net
{
    /// <summary>
    /// Peer-to-peer voice over Steam's own P2P networking (separate from the game's netcode). Voice
    /// frames are sent directly to the Steam ids of the other players in the session; Steam handles
    /// NAT traversal / relay, so there's no server, no port forwarding, no IP to configure.
    /// All reads/sends happen on the Unity main thread (Steam callbacks are pumped by the game).
    /// </summary>
    internal sealed class SteamP2PTransport
    {
        // Dedicated channel so we never collide with anything the game sends over Steam P2P.
        public const int Channel = 27;

        public bool Ready { get; private set; }
        public ulong SelfId { get; private set; }
        public string LastError { get; private set; } = "";

        private Callback<P2PSessionRequest_t> _sessionReq;
        private readonly PacketWriter _w = new PacketWriter();
        private readonly byte[] _rx = new byte[NorsProtocol.MaxDatagram];
        private readonly HashSet<ulong> _ignored = new HashSet<ulong>();   // host-muted/banned ids to drop

        private List<ulong> _peers = new List<ulong>();

        public void Start()
        {
            try
            {
                SelfId = SteamUser.GetSteamID().m_SteamID;
                _sessionReq = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
                SteamNetworking.AllowP2PPacketRelay(true);  // let Steam relay if a direct route fails
                Ready = SelfId != 0;
                if (!Ready) LastError = "Steam not available";
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Ready = false;
            }
        }

        public void Stop()
        {
            try { _sessionReq?.Dispose(); } catch { }
            _sessionReq = null;
            Ready = false;
            _ignored.Clear();
        }

        private void OnSessionRequest(P2PSessionRequest_t req)
        {
            // Accept voice sessions from anyone (we only ever read our own channel anyway).
            try { SteamNetworking.AcceptP2PSessionWithUser(req.m_steamIDRemote); } catch { }
        }

        /// <summary>The Steam ids of the other players in the session (the hub passes its live list).</summary>
        public void SetPeers(List<ulong> peers) { _peers = peers ?? new List<ulong>(); }

        /// <summary>Host-authority ignore set (muted/banned). Voice from these ids is dropped on receive.</summary>
        public void SetIgnored(IEnumerable<ulong> ids)
        {
            _ignored.Clear();
            if (ids != null) foreach (var id in ids) _ignored.Add(id);
        }

        public void SendVoice(uint clientId, uint seq, int txFreqKHz, Modulation mod, int factionId, byte crypto,
            float x, float y, float z, byte[] audio, int audioLen, string callsign, byte txJam, int stableFactionId)
        {
            if (!Ready || _peers.Count == 0) return;
            Packets.WriteVoice(_w, clientId, seq, txFreqKHz, mod, factionId, crypto, x, y, z, audio, 0, audioLen, callsign, txJam, stableFactionId);
            for (int i = 0; i < _peers.Count; i++)
            {
                ulong id = _peers[i];
                if (id == 0 || id == SelfId) continue;
                try { SteamNetworking.SendP2PPacket(new CSteamID(id), _w.Buffer, (uint)_w.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, Channel); }
                catch { }
            }
        }

        /// <summary>Host broadcasts the current mute/ban set to all peers (reliable).</summary>
        public void BroadcastBanList(ulong[] ids, int count)
        {
            if (!Ready) return;
            Packets.WriteBanList(_w, ids, count);
            for (int i = 0; i < _peers.Count; i++)
            {
                ulong id = _peers[i];
                if (id == 0 || id == SelfId) continue;
                try { SteamNetworking.SendP2PPacket(new CSteamID(id), _w.Buffer, (uint)_w.Length, EP2PSend.k_EP2PSendReliable, Channel); }
                catch { }
            }
        }

        /// <summary>
        /// Drains incoming P2P packets. Voice frames (not from an ignored id) go to <paramref name="onVoice"/>.
        /// A BanList is passed up WITH its sender id — deciding whose bans count is the hub's job now
        /// (see ModerationAuthority), because on a dedicated server there is no host to compare against.
        /// </summary>
        public void Receive(Action<VoiceHeader> onVoice, Action<ulong, ulong[]> onBanList)
        {
            if (!Ready) return;
            int guard = 0;
            while (guard++ < 256 && SteamNetworking.IsP2PPacketAvailable(out uint size, Channel))
            {
                if (!SteamNetworking.ReadP2PPacket(_rx, (uint)_rx.Length, out uint read, out CSteamID remote, Channel)) break;
                if (read < 2) continue;
                ulong from = remote.m_SteamID;

                var r = new PacketReader(_rx, 0, (int)read);
                if (r.Byte() != NorsProtocol.Version) continue;
                var type = (PacketType)r.Byte();

                if (type == PacketType.Voice)
                {
                    if (_ignored.Contains(from)) continue;     // host muted/banned this talker
                    onVoice(VoiceHeader.Read(ref r));
                }
                else if (type == PacketType.BanList)
                {
                    onBanList(from, Packets.ReadBanList(ref r));
                }
            }
        }
    }
}
