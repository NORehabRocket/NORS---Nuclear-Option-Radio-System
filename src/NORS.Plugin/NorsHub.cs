using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using NORS.Common;
using NORS.Plugin.Audio;
using NORS.Plugin.Game;
using NORS.Plugin.Net;
using NORS.Plugin.Comms;
using NORS.Plugin.UI;
using UnityEngine;

namespace NORS.Plugin
{
    /// <summary>
    /// Central driver MonoBehaviour. Resolves the local player, manages the relay connection,
    /// runs push-to-talk capture, routes incoming voice through the propagation model into the
    /// playback engine, and feeds the radio panel. Every module call is guarded so a fault in one
    /// never breaks the others or the game.
    /// </summary>
    internal sealed class NorsHub : MonoBehaviour
    {
        private readonly LocalState _local = new LocalState();
        private readonly JamSense _jam = new JamSense();
        private readonly RelaySense _relay = new RelaySense();
        private readonly RadioSet _radios = new RadioSet();
        private readonly VoiceClient _client = new VoiceClient();          // relay transport
        private readonly SteamP2PTransport _p2p = new SteamP2PTransport();  // P2P transport
        private readonly VoiceCapture _capture = new VoiceCapture();
        private readonly VoicePlayback _playback = new VoicePlayback();
        private HostBanStore _hostBans;
        private readonly ModerationAuthority _moderation = new ModerationAuthority();
        private int _appliedModRevision = -1;
        private bool _appliedAsModerator;
        private RadioPanel _ui;
        private readonly FirstRunSetup _firstRun = new FirstRunSetup();
        private readonly MfdOverlay _mfd = new MfdOverlay();
        private readonly MfdBezelPage _mfdPage = new MfdBezelPage();

        private uint _clientId;
        private bool _socketUp;          // relay socket up
        private bool _p2pStarted;        // P2P transport up
        private bool _userWantsConnection;
        private bool _warnedNoPeers;
        private bool _relayHostUnknown;
        private string _resolvedRelayHost = "";

        private float _lastHello, _lastState, _lastPing, _lastBanBroadcast;
        private readonly List<ulong> _banScratch = new List<ulong>();
        private int _txFrames, _rxFrames;
        private int _loggedFaction = int.MinValue;
        private readonly int[] _rxBuf = new int[NorsProtocol.MaxRxFrequencies];
        private readonly List<string> _talkerNames = new List<string>();
        private readonly List<RosterEntry> _roster = new List<RosterEntry>();
        private readonly List<string> _notices = new List<string>();

        // Last transmission we had to drop because of a passcode mismatch (panel feedback).
        private int _cryptoBlockedFreqKHz;
        private bool _cryptoBlockedHasKey;
        private float _cryptoBlockedAt = -100f;

        // Last faction-secure drop (panel feedback).
        private int _factionBlockedMine, _factionBlockedTheirs;
        private bool _factionBlockedLegacy;
        private float _factionBlockedAt = -100f;

        private void Awake()
        {
            _radios.LoadFromConfig();
            _playback.Init(transform);

            _clientId = (uint)Guid.NewGuid().GetHashCode();
            if (_clientId == 0) _clientId = 1;

            _capture.OnFrame = OnEncodedFrame;
            _userWantsConnection = NorsConfig.AutoConnect.Value;
            _hostBans = new HostBanStore(Path.Combine(Paths.ConfigPath, "nors-host-bans.txt"));

            _ui = new RadioPanel(_radios)
            {
                ToggleConnect = () => _userWantsConnection = !_userWantsConnection,
                OnVoteKick = id => _client.SendVoteKick(id),
                OnAdminLogin = pw => _client.SendAdminAuth(pw),
                OnAdminKick = id => _client.SendAdminCommand(AdminOp.Kick, id, ""),
                OnAdminBan = id => _client.SendAdminCommand(AdminOp.Ban, id, ""),
                // P2P host moderation (by Steam id, no server needed):
                OnHostBan = sid => { if (_hostBans.Add(sid)) { _appliedModRevision = -1; BroadcastBansNow(); } },
                OnHostUnban = sid => { if (_hostBans.Remove(sid)) { _appliedModRevision = -1; BroadcastBansNow(); } },
                IsBanned = sid => _hostBans.Contains(sid),
                MyClientId = _clientId,
            };
            NorsApi.Hub = this;
            NorsPlugin.Log.LogInfo($"NORS hub ready (client {_clientId:X8}).");
        }

        private void OnDestroy()
        {
            if (NorsApi.Hub == this) NorsApi.Hub = null;
            try { _client.Stop(); } catch { }
            try { _p2p.Stop(); } catch { }
            try { _mfd.Teardown(); } catch { }
            try { _mfdPage.Teardown(); } catch { }
            try { _playback.Clear(); } catch { }
        }

        private readonly TransportSelector _auto = new TransportSelector();

        /// <summary>
        /// Which transport we are actually using this frame. In Auto the selector decides by
        /// trying the relay and falling back; in P2P/Relay the player has overridden it.
        /// </summary>
        private bool P2P
        {
            get
            {
                switch (NorsConfig.Transport.Value)
                {
                    case VoiceTransport.P2P: return true;
                    case VoiceTransport.Relay: return false;
                    default: return !_auto.WantsRelay;
                }
            }
        }

        /// <summary>
        /// Runs the Auto decision. Kept out of ManageTransport so the two override modes cost
        /// nothing and behave exactly as they did before Auto existed.
        /// </summary>
        private void TickAutoTransport()
        {
            if (NorsConfig.Transport.Value != VoiceTransport.Auto) return;
            _auto.Tick(
                Time.unscaledTime,
                _local.InGame,
                relayCandidate: !string.IsNullOrEmpty(ResolveRelayHost()),
                relayConnected: _client.Connected,
                // "P2P is viable", not "someone is here to talk to". Flying alone is normal and
                // must not be reported as a broken transport; only "others present, none of them
                // reachable" counts against P2P.
                p2pUsable: !_local.PeersUnavailable);
        }

        /// <summary>
        /// PTT from the key OR an external caller (NorsApi, e.g. the DarkSkies ATC web panel).
        /// The chat-open gate (PR #1) applies to the KEYBOARD only — the browser PTT button
        /// isn't part of the keyboard-vs-chatbox conflict, so it keeps working while typing.
        /// </summary>
        private static bool PttHeld =>
            (Keys.Held(NorsConfig.PttKey.Value) && !GameInput.ChatOpen)
            || NorsApi.ExternalPttHeld;

        // ---- NorsApi backing (kept internal; the public surface is NorsApi) ----

        internal bool ApiTransmitting => _capture.Transmitting;

        internal string[] ApiTalkers()
        {
            return _talkerNames.Count == 0 ? Array.Empty<string>() : _talkerNames.ToArray();
        }

        internal int ApiRadioCount => _radios.Radios.Count;

        internal string ApiRadioInfo(int index)
        {
            var list = _radios.Radios;
            if (index < 0 || index >= list.Count) return null;
            var r = list[index];
            return $"{r.Label}|{r.FreqMHz:0.000}|{r.Mod}|{(r.Rx ? 1 : 0)}|{(index == _radios.TxIndex ? 1 : 0)}|{(r.HasCrypto ? 1 : 0)}";
        }

        internal bool ApiSetCrypto(int index, string passcode)
        {
            if (index < 0 || index >= _radios.Radios.Count) return false;
            _radios.Radios[index].SetCrypto((passcode ?? "").Trim());
            return true;
        }

        internal bool ApiTuneTx(float mhz)
        {
            var tx = _radios.Tx;
            if (tx == null) return false;
            int khz = (int)Math.Round(mhz * 1000f);
            if (khz < _radios.MinKHz) khz = _radios.MinKHz;
            if (khz > _radios.MaxKHz) khz = _radios.MaxKHz;
            tx.FreqKHz = khz;
            return true;
        }

        internal bool ApiSelectTx(int index)
        {
            if (index < 0 || index >= _radios.Radios.Count) return false;
            if (_radios.Radios[index].Mod == Modulation.Disabled) return false;
            _radios.TxIndex = index;
            return true;
        }

        internal bool ApiTuneRadio(int index, float mhz)
        {
            if (index < 0 || index >= _radios.Radios.Count) return false;
            int khz = (int)Math.Round(mhz * 1000f);
            if (khz < _radios.MinKHz) khz = _radios.MinKHz;
            if (khz > _radios.MaxKHz) khz = _radios.MaxKHz;
            _radios.Radios[index].FreqKHz = khz;
            return true;
        }

        internal bool ApiSetRx(int index, bool rx)
        {
            if (index < 0 || index >= _radios.Radios.Count) return false;
            _radios.Radios[index].Rx = rx;
            return true;
        }

        private void Update()
        {
            if (!NorsConfig.MasterEnabled.Value)
            {
                if (_socketUp) Disconnect();
                return;
            }

            Guard(() => _local.Resolve(), "Resolve");
            // Release the mic entirely when out of the mission (the transport paths only gate transmit,
            // they no longer stop the device — keeping it open mid-mission is what kills the PTT hitch).
            if (!_local.InGame) Guard(() => _capture.Stop(), "MicStop");
            Guard(() => _jam.Tick(_local.Aircraft), "Jam");
            Guard(() => _relay.Tick(Time.time), "Relay");
            Guard(LogFactionChange, "FactionLog");
            Guard(HandleInput, "Input");
            Guard(ManageTransport, "Transport");
            Guard(DrainIncoming, "Incoming");
            Guard(() => _playback.Update(), "Playback");
            Guard(UpdateUiState, "UiState");
            Guard(UpdateMfd, "MFD");
        }

        private void OnGUI()
        {
            if (!NorsConfig.MasterEnabled.Value || _ui == null) return;
            if (_firstRun.ShouldShow(_local.InGame)) Guard(() => _firstRun.Render(), "FirstRun");
            Guard(() => _ui.Render(), "UI");
        }

        // ---------------- input ----------------

        private void HandleInput()
        {
            if (GameInput.ChatOpen)
                return;
            if (Keys.Pressed(NorsConfig.PanelKey.Value)) _ui.Visible = !_ui.Visible;
            if (Keys.Pressed(NorsConfig.CycleTxRadioKey.Value)) _radios.CycleTx();
            if (Keys.Pressed(NorsConfig.TuneUpKey.Value)) _radios.Tune(_radios.Tx, +1);
            if (Keys.Pressed(NorsConfig.TuneDownKey.Value)) _radios.Tune(_radios.Tx, -1);
        }

        // ---------------- transport lifecycle ----------------

        private void ManageTransport()
        {
            TickAutoTransport();
            if (P2P) ManageP2P();
            else ManageRelay();
        }

        // ---------------- P2P (Steam) ----------------

        private void ManageP2P()
        {
            if (_socketUp) Disconnect();   // switched away from relay

            if (!_local.InGame)
            {
                if (_p2pStarted) StopP2P();
                _capture.SetActive(false);
                return;
            }

            if (!_p2pStarted) StartP2P();
            if (!_p2p.Ready) { _capture.SetActive(false); return; }

            _p2p.SetPeers(_local.Peers);

            // No addressable peers: say so plainly rather than transmitting into the void.
            if (_local.PeersUnavailable)
            {
                if (!_warnedNoPeers)
                {
                    _warnedNoPeers = true;
                    NorsPlugin.Log.LogWarning(
                        $"NORS: {_local.OtherPlayerCount} other player(s) here, but nobody has a Steam id, so P2P has " +
                        "nobody to send to. " + (_local.UdpTransport
                            ? "This server runs the plain UDP transport, so the game never sends player Steam ids. Ask the " +
                              "operator to launch it with '-socket SteamGameServer', or set Transport = Relay."
                            : "Set Transport = Relay, or use a Steam-hosted lobby."));
                }
            }
            else _warnedNoPeers = false;

            // Apply every authority's bans (ours included if we are one), then re-broadcast ours.
            // This runs for everyone now, not just the host: on a dedicated server there IS no host
            // player, so gating it on IsHost meant nobody sent bans and nobody applied them.
            _moderation.Sync(NorsConfig.Moderators.Value);
            bool amMod = CanModerate;
            if (_moderation.Revision != _appliedModRevision || amMod != _appliedAsModerator)
            {
                _appliedModRevision = _moderation.Revision;
                _appliedAsModerator = amMod;
                _moderation.BuildEffective(_hostBans, amMod, _banScratch);
                _p2p.SetIgnored(_banScratch);
            }

            if (amMod)
            {
                float t = Time.unscaledTime;
                if (t - _lastBanBroadcast >= 3f) { _lastBanBroadcast = t; BroadcastBansNow(); }
            }

            // Push to talk.
            Radio tx = _radios.Tx;
            bool ptt = PttHeld && tx != null && tx.Mod != Modulation.Disabled;
            _capture.SetActive(ptt);
            _capture.Tick(NorsConfig.MicGain.Value);
        }

        private void StartP2P()
        {
            _p2p.Start();
            _p2pStarted = true;
            _lastBanBroadcast = 0f;
            NorsPlugin.Log.LogInfo(_p2p.Ready
                ? $"NORS P2P voice active (steam {_p2p.SelfId})."
                : $"NORS P2P unavailable: {_p2p.LastError}");
        }

        private void StopP2P()
        {
            _p2p.Stop();
            _p2pStarted = false;
            _playback.Clear();
            _capture.SetActive(false);
            _moderation.ClearReceived();   // authorities are per-session
        }

        /// <summary>
        /// Whether WE may mute/ban for everyone: the game host in a player-hosted lobby, or anyone
        /// listed in Moderation/Moderators. The latter is the only path that works on a dedicated
        /// server, where no player is ever flagged as the host.
        /// </summary>
        private bool CanModerate =>
            _local.IsHost || _moderation.IsAuthority(_local.SteamId, _local.RoomId);

        private void BroadcastBansNow()
        {
            if (!CanModerate || !_p2p.Ready) return;
            var mine = new List<ulong>();
            _hostBans.CopyTo(mine);
            _p2p.BroadcastBanList(mine.ToArray(), mine.Count);
        }

        private void OnHostBanListReceived(ulong from, ulong[] ids)
        {
            // Whose bans count is decided here, not by network position. Unions with any other
            // authority's list on the next tick so two moderators don't overwrite each other.
            _moderation.Accept(from, ids, _local.RoomId);
        }

        // ---------------- relay ----------------

        private void ManageRelay()
        {
            if (_p2pStarted) StopP2P();   // switched away from P2P

            // The server kicked/banned/rejected us: stop auto-reconnecting and stay out until the
            // player manually reconnects from the panel.
            if (_socketUp && _client.Kicked)
            {
                NorsPlugin.Log.LogWarning($"NORS removed by server: {_client.KickReason}");
                _userWantsConnection = false;
                Disconnect();
                return;
            }

            if (NorsConfig.AutoConnect.Value && _local.InGame && !_client.Kicked)
                _userWantsConnection = true;

            if (_userWantsConnection && !_socketUp) Connect();
            else if (!_userWantsConnection && _socketUp) Disconnect();

            if (!_socketUp) { _capture.SetActive(false); return; }

            float now = Time.unscaledTime;

            // Retry Hello until acknowledged.
            if (!_client.Connected)
            {
                if (now - _lastHello >= NorsProtocol.HelloRetryInterval)
                {
                    _lastHello = now;
                    _client.SendHello(_local.SteamId, _local.RoomId, _local.IsHost, _local.FactionId, _local.Callsign);
                }
                _capture.SetActive(false);
                return;
            }

            // Connected: periodic state + ping.
            if (now - _lastState >= NorsProtocol.StateSendInterval)
            {
                _lastState = now;
                int n = _radios.GetRxFrequencies(_rxBuf);
                var g = _local.HasPosition ? _local.Global : default;
                _client.SendState(_local.SteamId, _local.RoomId, _local.IsHost, _local.FactionId, g.x, g.y, g.z, _rxBuf, n, _local.Callsign);
            }
            if (now - _lastPing >= NorsProtocol.PingInterval)
            {
                _lastPing = now;
                _client.SendPing();
            }

            // Push to talk.
            Radio tx = _radios.Tx;
            bool ptt = PttHeld && tx != null && tx.Mod != Modulation.Disabled;
            _capture.SetActive(ptt);
            _capture.Tick(NorsConfig.MicGain.Value);
        }

        /// <summary>
        /// Where the relay is. "auto" (the default) means the game server we're already connected
        /// to — a server running NORS hosts voice in-process, so its own address is the answer and
        /// nobody has to type an IP. Falls back to whatever is configured if the address can't be
        /// read, which is the case on Steam-socket servers (where P2P works anyway).
        /// </summary>
        private string ResolveRelayHost()
        {
            string configured = (NorsConfig.ServerHost.Value ?? "").Trim();
            bool auto = configured.Length == 0 || configured.Equals("auto", StringComparison.OrdinalIgnoreCase);
            if (!auto) return configured;
            return _local.ServerAddress ?? "";
        }

        /// <summary>
        /// Which port the relay is on. 0 in config means "work it out": a server hosting voice
        /// in-process binds its game port + RelayPortOffset, so a client on game port 7778 looks
        /// for voice on 8778 and lands on that server's relay rather than the one next door.
        /// </summary>
        private int ResolveRelayPort()
        {
            int configured = NorsConfig.ServerPort.Value;
            if (configured > 0) return configured;
            return NorsProtocol.RelayPortFor(_local.ServerPort);
        }

        private void Connect()
        {
            string host = ResolveRelayHost();
            if (string.IsNullOrEmpty(host))
            {
                // Nothing to connect to yet. Don't spin: wait until we're in a game and the
                // address resolves, or until the player sets ServerHost by hand.
                _relayHostUnknown = true;
                return;
            }
            _relayHostUnknown = false;
            _resolvedRelayHost = host;

            int port = ResolveRelayPort();
            _client.Start(host, port, _clientId);
            _socketUp = true;
            _lastHello = 0f;
            NorsPlugin.Log.LogInfo($"NORS connecting to {host}:{port}" +
                                   (host == _local.ServerAddress ? " (this game server)" : "") + "...");
        }

        private void Disconnect()
        {
            _client.Stop();
            _playback.Clear();
            _capture.SetActive(false);
            _socketUp = false;
        }

        // ---------------- voice in/out ----------------

        private byte[] _cryptoScratch = new byte[4096];

        private void OnEncodedFrame(byte[] data, int len, uint seq)
        {
            Radio tx = _radios.Tx;
            if (tx == null || tx.Mod == Modulation.Disabled) return;

            // Passcode channel (key id 2..255) scrambles the payload itself, so wrong-key
            // listeners and older clients get noise; plain Secure (1) stays faction-gated.
            byte crypto = tx.HasCrypto ? tx.KeyId : (tx.Secure ? (byte)1 : (byte)0);
            if (tx.HasCrypto)
            {
                if (_cryptoScratch.Length < len) _cryptoScratch = new byte[len];
                System.Buffer.BlockCopy(data, 0, _cryptoScratch, 0, len);
                Scramble.Apply(_cryptoScratch, 0, len, tx.CryptoHash ^ seq);
                data = _cryptoScratch;
            }
            var g = _local.HasPosition ? _local.Global : default;

            // Our own jam level rides along so receivers garble us when WE are being jammed
            // (so a jammed transmitter doesn't come through clear to everyone else).
            byte txJam = (byte)Mathf.Clamp(
                Mathf.RoundToInt(_jam.Level01(NorsConfig.JamReference.Value) * 255f), 0, 255);

            if (P2P)
            {
                if (!_p2p.Ready) return;
                _p2p.SendVoice(_clientId, seq, tx.FreqKHz, tx.Mod, _local.FactionId, crypto,
                    g.x, g.y, g.z, data, len, _local.Callsign, txJam, _local.StableFactionId);
            }
            else
            {
                if (!_client.Connected) return;
                _client.SendVoice(seq, tx.FreqKHz, tx.Mod, _local.FactionId, crypto,
                    g.x, g.y, g.z, data, len, _local.Callsign, txJam, _local.StableFactionId);
            }
            _txFrames++;

            // Sidetone: hear ourselves locally to verify the whole audio chain.
            if (NorsConfig.MicMonitor.Value) _playback.Monitor(data, len);
        }

        private void DrainIncoming()
        {
            if (P2P)
            {
                if (_p2pStarted) _p2p.Receive(ProcessIncoming, OnHostBanListReceived);
                return;
            }

            if (!_socketUp) return;
            while (_client.TryDequeueVoice(out VoiceHeader v))
                ProcessIncoming(v);

            while (_client.TryDequeueNotice(out string notice))
            {
                NorsPlugin.Log.LogInfo("NORS: " + notice);
                _notices.Add(notice);
                if (_notices.Count > 6) _notices.RemoveAt(0);
            }

            _client.GetRoster(_roster);
        }

        private void LogFactionChange()
        {
            if (_local.FactionId == _loggedFaction) return;
            _loggedFaction = _local.FactionId;
            string fn = _local.Faction != null ? _local.Faction.factionName : "(none)";
            // Log both: if two players' stable ids ever differ for the same faction name,
            // that is the smoking gun and it needs to be in the log, not inferred.
            NorsPlugin.Log.LogInfo(
                $"NORS faction = '{fn}' stableId={_local.StableFactionId} legacyNetId={_local.FactionId}");
            if (_local.StableFactionId == 0 && _local.Faction != null)
                NorsPlugin.Log.LogWarning(
                    "NORS: faction has no Encyclopedia index — falling back to the legacy NetId for " +
                    "secure channels, which can disagree between clients.");
        }

        /// <summary>
        /// Faction-secure gate for CryptoKeyId 1 (every radio uses it when
        /// FactionSecureByDefault is on, which is the default — so this decides whether
        /// almost all traffic is heard at all).
        ///
        /// Prefers the content-defined faction id, which is identical on every client. Falls
        /// back to the legacy HQ NetId only when one end is pre-0.7.6 and didn't send one.
        /// Drops ONLY when both sides are known and differ: an unknown id (spawn menu, HQ not
        /// resolved yet, definition list not indexed) must never turn into silent silence.
        /// </summary>
        private bool FactionAllows(in VoiceHeader v)
        {
            int mine = _local.StableFactionId;
            int theirs = v.StableFactionId;
            if (mine == 0 || theirs == 0)
            {
                mine = _local.FactionId;      // legacy NetId path, only as good as it ever was
                theirs = v.FactionId;
            }
            if (mine == 0 || theirs == 0) return true;
            return mine == theirs;
        }

        private void ProcessIncoming(VoiceHeader v)
        {
            if (v.ClientId == _clientId) return;

            Radio r = _radios.FindReceiver(v.TxFreqKHz);
            if (r == null) return;

            if (v.CryptoKeyId == 1 && !FactionAllows(v))
            {
                // Never drop this silently — an unexplained faction mismatch is exactly how
                // secure channels went one-way for people without a single log line.
                _factionBlockedMine = _local.StableFactionId != 0 ? _local.StableFactionId : _local.FactionId;
                _factionBlockedTheirs = v.StableFactionId != 0 ? v.StableFactionId : v.FactionId;
                _factionBlockedLegacy = _local.StableFactionId == 0 || v.StableFactionId == 0;
                _factionBlockedAt = Time.unscaledTime;
                return;
            }
            else if (v.CryptoKeyId >= 2)
            {
                // Passcode channel: our tuned radio must hold the matching passcode.
                // XOR is symmetric, so applying the keystream again restores the audio;
                // a wrong/absent passcode never even attempts playback.
                if (!r.HasCrypto || r.KeyId != v.CryptoKeyId)
                {
                    // Tell the player WHY they're hearing nothing — a silent drop here is
                    // indistinguishable from "the mod is broken".
                    _cryptoBlockedFreqKHz = v.TxFreqKHz;
                    _cryptoBlockedHasKey = r.HasCrypto;
                    _cryptoBlockedAt = Time.unscaledTime;
                    return;
                }
                Scramble.Apply(v.Audio, 0, v.AudioLen, r.CryptoHash ^ v.Seq);
            }

            bool modMatch = r.Mod == v.Mod;
            var txGlobal = new GlobalPosition(v.X, v.Y, v.Z);
            Propagation p = RadioPropagation.Compute(
                _local.HasPosition, _local.WorldPos, _local.AltitudeMeters, txGlobal, v.Mod, modMatch);

            // Airborne relay (AWACS/Radome): if a friendly high-flying Radome aircraft can hear the
            // transmitter and reach us, route through it — it sees over terrain and far past our
            // horizon, so it can rescue/boost a direct path that's blocked or too distant.
            if (NorsConfig.RelayViaRadome.Value && _local.HasPosition)
                ApplyRelay(ref p, txGlobal, v.Mod, modMatch);

            if (!p.Audible) return;

            // Radar jamming garbles comms. It bites if EITHER end is jammed: our own receiver being
            // jammed (enemy pod on us / our ECM) OR the transmitter being jammed (rides in v.TxJam01),
            // so a jammed sender comes through garbled to everyone — take the worst of the two.
            if (NorsConfig.JamAffectsRadio.Value)
            {
                float jam01 = Mathf.Max(_jam.Level01(NorsConfig.JamReference.Value), v.TxJam01);
                float jam = jam01 * NorsConfig.JamRadioEffect.Value;
                if (jam > 0f)
                {
                    p.Quality = Mathf.Clamp01(p.Quality * (1f - jam));
                    p.Volume *= (1f - 0.35f * jam);
                }
            }

            _playback.OnFrame(v, p, r.Volume, v.Callsign, NorsConfig.OpenAir3D.Value);
            _rxFrames++;
        }

        /// <summary>
        /// Two-hop path through a friendly Radome relay: transmitter -> relay (pure RF reception) and
        /// relay -> us (we're tuned, so modulation matters). The relay re-transmits at full power from
        /// its high vantage, so the second leg sets the volume; clarity is the worse of the two legs
        /// minus a small relay penalty. Replaces the direct path only when it beats it.
        /// </summary>
        private void ApplyRelay(ref Propagation p, GlobalPosition txGlobal, Modulation mod, bool modMatch)
        {
            var nodes = _relay.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                RelayNode n = nodes[i];
                if (n.FactionId != _local.FactionId) continue;   // only our own side's relays

                Propagation leg1 = RadioPropagation.Compute(true, n.World, n.AltM, txGlobal, mod, true);
                if (!leg1.Audible) continue;
                Propagation leg2 = RadioPropagation.Compute(
                    _local.HasPosition, _local.WorldPos, _local.AltitudeMeters, n.Global, mod, modMatch);
                if (!leg2.Audible) continue;

                if (leg2.Volume > p.Volume)
                {
                    p.Volume = leg2.Volume;
                    p.Quality = Mathf.Clamp01(Mathf.Min(leg1.Quality, leg2.Quality) * NorsConfig.RelayQualityFactor.Value);
                    p.Audible = p.Volume > 0.02f;
                }
            }
        }

        // ---------------- UI state ----------------

        private void UpdateUiState()
        {
            _playback.GetActive(_talkerNames);
            bool p2p = P2P;
            _ui.P2PMode = p2p;
            _ui.MicLevel = _capture.LastLevel;
            _ui.Transmitting = _capture.Transmitting;
            _ui.InGame = _local.InGame;
            _ui.Callsign = _local.Callsign;
            _ui.FactionName = _local.Faction != null ? _local.Faction.factionName : "(none)";
            _ui.Talkers = _talkerNames;
            _ui.Notices = _notices;
            _ui.MyFactionId = _local.FactionId;
            _ui.MySteamId = _local.SteamId;
            _ui.LocalIsHost = _local.IsHost;
            _ui.CanModerate = CanModerate;
            _ui.TxFrames = _txFrames;
            _ui.RxFrames = _rxFrames;
            _ui.MicInfo = _capture.Capturing ? _capture.DeviceLabel : (_capture.MicAvailable ? "ready" : "NO MIC");
            _ui.TalkerCount = _playback.ActiveTalkers;

            // Surface a recent crypto-mismatch drop for ~6 s so the player sees a reason
            // instead of silence ("someone is talking here but your passcode doesn't match").
            if (Time.unscaledTime - _cryptoBlockedAt < 6f)
            {
                _ui.CryptoBlockedMhz = _cryptoBlockedFreqKHz / 1000f;
                _ui.CryptoBlockedHasKey = _cryptoBlockedHasKey;
            }
            else _ui.CryptoBlockedMhz = 0f;

            // Same treatment for a faction-secure drop. This is the one that used to be silent.
            _ui.FactionBlocked = Time.unscaledTime - _factionBlockedAt < 6f;
            if (_ui.FactionBlocked)
            {
                _ui.FactionBlockedMine = _factionBlockedMine;
                _ui.FactionBlockedTheirs = _factionBlockedTheirs;
                _ui.FactionBlockedLegacy = _factionBlockedLegacy;
            }

            _ui.AutoMode = NorsConfig.Transport.Value == VoiceTransport.Auto;
            _ui.AutoStatus = _ui.AutoMode ? _auto.Describe(ResolveRelayPort()) : "";
            _ui.AutoProbing = _auto.Phase == AutoPhase.ProbingRelay;
            _ui.AutoStuck = _auto.Phase == AutoPhase.Stuck;
            _ui.MyStableFactionId = _local.StableFactionId;
            _ui.UdpTransport = _local.UdpTransport;
            _ui.PeerCount = _local.Peers.Count;
            _ui.OtherPlayers = _local.OtherPlayerCount;

            if (p2p)
            {
                _ui.SocketUp = _p2pStarted;
                _ui.Connected = _p2p.Ready;
                _ui.ServerName = "Steam P2P";
                _ui.LastError = _p2p.Ready ? "" : _p2p.LastError;
                _ui.Roster = _local.InGame ? _local.SessionRoster : EmptyRoster;
                _ui.AdminAuthed = false;
                _ui.P2PNoPeers = _local.InGame && _local.PeersUnavailable && !_ui.AutoProbing;
                _ui.P2POtherPlayers = _local.OtherPlayerCount;
                _ui.P2PUdpServer = _local.UdpTransport;
            }
            else
            {
                _ui.SocketUp = _socketUp;
                _ui.Connected = _client.Connected;
                _ui.ServerName = _client.ServerName;
                _ui.LastError = _client.LastError;
                _ui.Roster = _client.Connected ? _roster : EmptyRoster;
                _ui.AdminAuthed = _client.AdminAuthed;
                _ui.AdminPassword = NorsConfig.AdminPassword.Value;
            }
        }

        private bool _mfdVisible = true;
        private string _mfdLastSig;
        private float _mfdActiveUntil;
        private Aircraft _mfdLastAircraft;
        private readonly System.Text.StringBuilder _mfdSig = new System.Text.StringBuilder(96);

        /// <summary>Signature of the radio state so we can detect when the player changes freq/band/radio.</summary>
        private string RadioSignature()
        {
            _mfdSig.Length = 0;
            var list = _radios.Radios;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                _mfdSig.Append(r.FreqKHz).Append(':').Append((int)r.Mod).Append(r.Rx ? '+' : '-').Append('|');
            }
            _mfdSig.Append('T').Append(_radios.TxIndex);
            return _mfdSig.ToString();
        }

        private void UpdateMfd()
        {
            if (!GameInput.ChatOpen && Keys.Pressed(NorsConfig.MfdToggleKey.Value)) _mfdVisible = !_mfdVisible;

            // Force a clean rebind whenever the aircraft changes (or we leave one), so a readout can't
            // linger on a swapped-out cockpit/HUD.
            if (_local.Aircraft != _mfdLastAircraft)
            {
                _mfd.Teardown();
                _mfdPage.Teardown();
                _mfdLastAircraft = _local.Aircraft;
            }

            // Drive the auto-show: appear on radio activity (PTT / freq / band / radio switch) or RX.
            string sig = RadioSignature();
            bool changed = _mfdLastSig != null && sig != _mfdLastSig;
            _mfdLastSig = sig;
            Radio tx = _radios.Tx;
            bool ptt = PttHeld && tx != null && tx.Mod != Modulation.Disabled;
            bool rx = _talkerNames != null && _talkerNames.Count > 0;
            if (changed || ptt || rx) _mfdActiveUntil = Time.time + NorsConfig.MfdShowSeconds.Value;
            bool active = !NorsConfig.MfdAutoHide.Value || Time.time < _mfdActiveUntil;

            if (!NorsConfig.MfdEnabled.Value || !_mfdVisible || !active || _local == null || !_local.InGame)
            {
                _mfdPage.Teardown();
                _mfd.Teardown();
                return;
            }

            bool jammed = NorsConfig.JamAffectsRadio.Value
                          && _jam.Level01(NorsConfig.JamReference.Value) > 0.05f;

            // Overlay and BezelPage modes are temporarily disabled (still buggy) — always use the HUD
            // readout. The bezel-page renderer is kept dormant for when those are re-enabled.
            _mfdPage.Teardown();
            _mfd.Tick(_local, _radios, _talkerNames, jammed);
        }

        private static readonly List<RosterEntry> EmptyRoster = new List<RosterEntry>();

        private static void Guard(Action a, string where)
        {
            try { a(); }
            catch (Exception e) { NorsPlugin.Log.LogError($"[{where}] {e}"); }
        }
    }
}
