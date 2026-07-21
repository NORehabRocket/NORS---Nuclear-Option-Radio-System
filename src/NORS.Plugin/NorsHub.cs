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
        private RadioPanel _ui;
        private readonly MfdOverlay _mfd = new MfdOverlay();
        private readonly MfdBezelPage _mfdPage = new MfdBezelPage();

        private uint _clientId;
        private bool _socketUp;          // relay socket up
        private bool _p2pStarted;        // P2P transport up
        private bool _userWantsConnection;

        private float _lastHello, _lastState, _lastPing, _lastBanBroadcast;
        private readonly List<ulong> _banScratch = new List<ulong>();
        private int _txFrames, _rxFrames;
        private int _loggedFaction = int.MinValue;
        private readonly int[] _rxBuf = new int[NorsProtocol.MaxRxFrequencies];
        private readonly List<string> _talkerNames = new List<string>();
        private readonly List<RosterEntry> _roster = new List<RosterEntry>();
        private readonly List<string> _notices = new List<string>();

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
                OnHostBan = sid => { if (_hostBans.Add(sid)) BroadcastBansNow(); },
                OnHostUnban = sid => { if (_hostBans.Remove(sid)) BroadcastBansNow(); },
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

        private static bool P2P => NorsConfig.Transport.Value == VoiceTransport.P2P;

        /// <summary>PTT from the key OR an external caller (NorsApi, e.g. TOWER's web panel).</summary>
        private static bool PttHeld => (Keys.Held(NorsConfig.PttKey.Value) || NorsApi.ExternalPttHeld) && !CursorManager.GetFlag(CursorFlags.Chat);

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
            Guard(() => _ui.Render(), "UI");
        }

        // ---------------- input ----------------

        private void HandleInput()
        {
            if (CursorManager.GetFlag(CursorFlags.Chat))
                return;
            if (Keys.Pressed(NorsConfig.PanelKey.Value)) _ui.Visible = !_ui.Visible;
            if (Keys.Pressed(NorsConfig.CycleTxRadioKey.Value)) _radios.CycleTx();
            if (Keys.Pressed(NorsConfig.TuneUpKey.Value)) _radios.Tune(_radios.Tx, +1);
            if (Keys.Pressed(NorsConfig.TuneDownKey.Value)) _radios.Tune(_radios.Tx, -1);
        }

        // ---------------- transport lifecycle ----------------

        private void ManageTransport()
        {
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

            // Host: drop voice from its own ban list and re-broadcast it to peers periodically.
            if (_local.IsHost)
            {
                _hostBans.CopyTo(_banScratch);
                _p2p.SetIgnored(_banScratch);
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
        }

        private void BroadcastBansNow()
        {
            if (!_local.IsHost || !_p2p.Ready) return;
            _hostBans.CopyTo(_banScratch);
            _p2p.BroadcastBanList(_banScratch.ToArray(), _banScratch.Count);
        }

        private void OnHostBanListReceived(ulong[] ids)
        {
            // A non-host client applies the host's mute/ban set.
            _p2p.SetIgnored(ids);
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

        private void Connect()
        {
            _client.Start(NorsConfig.ServerHost.Value, NorsConfig.ServerPort.Value, _clientId);
            _socketUp = true;
            _lastHello = 0f;
            NorsPlugin.Log.LogInfo($"NORS connecting to {NorsConfig.ServerHost.Value}:{NorsConfig.ServerPort.Value}...");
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
                    g.x, g.y, g.z, data, len, _local.Callsign, txJam);
            }
            else
            {
                if (!_client.Connected) return;
                _client.SendVoice(seq, tx.FreqKHz, tx.Mod, _local.FactionId, crypto,
                    g.x, g.y, g.z, data, len, _local.Callsign, txJam);
            }
            _txFrames++;

            // Sidetone: hear ourselves locally to verify the whole audio chain.
            if (NorsConfig.MicMonitor.Value) _playback.Monitor(data, len);
        }

        private void DrainIncoming()
        {
            if (P2P)
            {
                if (_p2pStarted) _p2p.Receive(ProcessIncoming, _local.RoomId, OnHostBanListReceived);
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
            NorsPlugin.Log.LogInfo($"NORS faction = '{fn}' id={_local.FactionId}");
        }

        private void ProcessIncoming(VoiceHeader v)
        {
            if (v.ClientId == _clientId) return;

            Radio r = _radios.FindReceiver(v.TxFreqKHz);
            if (r == null) return;

            if (v.CryptoKeyId == 1)
            {
                // Legacy faction-secure: intelligible to own faction only.
                if (v.FactionId != _local.FactionId) return;
            }
            else if (v.CryptoKeyId >= 2)
            {
                // Passcode channel: our tuned radio must hold the matching passcode.
                // XOR is symmetric, so applying the keystream again restores the audio;
                // a wrong/absent passcode never even attempts playback.
                if (!r.HasCrypto || r.KeyId != v.CryptoKeyId) return;
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
            _ui.TxFrames = _txFrames;
            _ui.RxFrames = _rxFrames;
            _ui.MicInfo = _capture.Capturing ? _capture.DeviceLabel : (_capture.MicAvailable ? "ready" : "NO MIC");
            _ui.TalkerCount = _playback.ActiveTalkers;

            if (p2p)
            {
                _ui.SocketUp = _p2pStarted;
                _ui.Connected = _p2p.Ready;
                _ui.ServerName = "Steam P2P";
                _ui.LastError = _p2p.Ready ? "" : _p2p.LastError;
                _ui.Roster = _local.InGame ? _local.SessionRoster : EmptyRoster;
                _ui.AdminAuthed = false;
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
            if (Keys.Pressed(NorsConfig.MfdToggleKey.Value)) _mfdVisible = !_mfdVisible;

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
