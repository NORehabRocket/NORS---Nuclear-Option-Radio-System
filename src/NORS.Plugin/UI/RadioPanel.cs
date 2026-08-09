using System;
using System.Collections.Generic;
using NORS.Common;
using NORS.Plugin.Comms;
using UnityEngine;

namespace NORS.Plugin.UI
{
    /// <summary>IMGUI radio panel: tune radios, pick the transmit radio, see who's talking and connection state.</summary>
    internal sealed class RadioPanel
    {
        public bool Visible;

        // Pushed in by the hub each frame.
        public bool SocketUp;
        public bool Connected;
        public bool InGame;
        public bool Transmitting;
        public string ServerName = "";
        public string LastError = "";
        public string Callsign = "";
        public string FactionName = "";
        public float MicLevel;
        public List<string> Talkers;

        // Diagnostics.
        public int TxFrames;
        public int RxFrames;
        public int TalkerCount;
        public string MicInfo = "";

        // Moderation / roster.
        public List<RosterEntry> Roster;
        public List<string> Notices;
        public uint MyClientId;
        public int MyFactionId;
        public bool AdminAuthed;
        public bool LocalIsHost;
        public bool CanModerate;   // host OR a configured moderator
        public string AdminPassword = "";

        // P2P mode (voice direct over Steam; host moderates by Steam id, no relay).
        public bool P2PMode;
        public bool P2PNoPeers;      // players present but no Steam ids -> P2P can't reach anyone
        public int P2POtherPlayers;
        public bool P2PUdpServer;    // server runs the plain UDP transport (the actual cause)

        // Auto transport selection.
        public bool AutoMode;
        public string AutoStatus = "";
        public bool AutoProbing;
        public bool AutoStuck;

        // Faction-secure drop feedback + the diagnostics readout.
        public bool FactionBlocked;
        public int FactionBlockedMine, FactionBlockedTheirs;
        public bool FactionBlockedLegacy;   // one end is pre-0.7.6, so we compared NetIds
        public int MyStableFactionId;
        public bool UdpTransport;
        public int PeerCount;
        public int OtherPlayers;
        private bool _showDiag;
        public ulong MySteamId;
        public Action<ulong> OnHostBan;
        public Action<ulong> OnHostUnban;
        public Func<ulong, bool> IsBanned;

        public Action ToggleConnect;
        public Action<uint> OnVoteKick;
        public Action<string> OnAdminLogin;
        public Action<uint> OnAdminKick;
        public Action<uint> OnAdminBan;

        // Passcode ("encryption") feedback + inline editor.
        public float CryptoBlockedMhz;      // >0 = we just dropped traffic we can't decode
        public bool CryptoBlockedHasKey;    // true = we have a key, it just doesn't match

        private string _adminPwField;
        private Vector2 _scroll;
        private int _cryptoEditIndex = -1;  // which radio's passcode row is open
        private string _cryptoField = "";

        private readonly RadioSet _radios;
        private const int WindowId = 0x4E4F5253; // 'NORS'
        private Rect _window = new Rect(40, 80, 470, 0);
        private GUIStyle _hdr;

        public RadioPanel(RadioSet radios) { _radios = radios; }

        public void Render()
        {
            if (!Visible) return;
            // Draw() flips the global GUI.enabled to lock the panel while chat is open —
            // restore it no matter what, or a fault in here would grey out every other
            // mod's IMGUI too.
            Theme.Build();
            bool prevEnabled = GUI.enabled;
            try { _window = GUILayout.Window(WindowId, _window, Draw, "NORS  ·  RADIO", Theme.Window); }
            finally { GUI.enabled = prevEnabled; }
        }

        private void Draw(int id)
        {
            if (_hdr == null) _hdr = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            // While the player is typing in the game's chat box the whole panel goes
            // read-only: you can still see your frequencies, but nothing can be
            // clicked or typed into (the admin password field would otherwise eat
            // keystrokes, and stray clicks could retune a radio mid-message).
            bool chatOpen = GameInput.ChatOpen;
            if (chatOpen)
            {
                GUILayout.Label("<color=#ffd24a>⌨ Chat open — radio controls locked</color>",
                    new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold });
            }
            GUI.enabled = !chatOpen;

            // --- status ---
            string status =
                !SocketUp ? "<color=#bbbbbb>Disconnected</color>" :
                !Connected ? "<color=#ffd24a>Connecting…</color>" :
                $"<color=#7CFC7C>Connected: {ServerName}</color>";
            var rich = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold };
            GUILayout.Label(status, rich);
            if (!string.IsNullOrEmpty(LastError))
                GUILayout.Label($"<color=#ff8080>{LastError}</color>", rich);

            // Auto mode picks the transport per server, so say which one it landed on —
            // "it just works" and "it silently isn't working" must not look the same.
            if (AutoMode && !string.IsNullOrEmpty(AutoStatus))
            {
                string colour = AutoStuck ? Theme.Hex(Theme.Red)
                    : AutoProbing ? Theme.Hex(Theme.Amber)
                    : Theme.Hex(Theme.Green);
                GUILayout.Label($"<color={colour}>◈ {AutoStatus}</color>", rich);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Callsign}  ·  {FactionName}");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(SocketUp ? "Disconnect" : "Connect", GUILayout.Width(100)))
                ToggleConnect?.Invoke();
            GUILayout.EndHorizontal();

            // --- mic level / PTT ---
            GUILayout.BeginHorizontal();
            GUILayout.Label(Transmitting ? "<color=#ff5050>● TX</color>" : "○ RX", rich, GUILayout.Width(46));
            GUILayout.Label(Bar(MicLevel, 22));
            GUILayout.EndHorizontal();

            // Steam P2P with no addressable peers: the mic works and TX lights up, but the
            // audio has nowhere to go. This is a server-type limitation, not a user error.
            if (AutoStuck)
            {
                GUILayout.Label(
                    $"<color={Theme.Hex(Theme.Red)}>⚠ NO VOICE ON THIS SERVER</color>\n" +
                    $"<color={Theme.Hex(Theme.Txt)}>It isn't running a NORS voice server, and it doesn't " +
                    "share Steam IDs, so direct peer-to-peer has nobody to reach either. Ask the operator " +
                    "to install NORS on the server — it's one folder and one port. Meanwhile you can set " +
                    "<b>Transport = Relay</b> with a <b>ServerHost</b> by hand if your group runs one.</color>",
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                GUILayout.Space(4);
            }
            else if (P2PNoPeers)
            {
                // Name the actual cause. It isn't "dedicated servers" in general — a dedicated
                // server started with '-socket SteamGameServer' authenticates over Steam and
                // shares Steam IDs, and P2P works there. Only the plain UDP transport can't.
                string body = P2PUdpServer
                    ? $"{P2POtherPlayers} other player(s) here, but this server runs the game's <b>plain UDP " +
                      "transport</b>, which never sends anyone's Steam ID. Peer-to-peer voice has no address to " +
                      "send to, so TX lights up and nobody hears you.\n" +
                      "<b>Best fix (server side):</b> ask the operator to start it with " +
                      "<b>-socket SteamGameServer</b>. Steam IDs are then shared and P2P works for everyone, " +
                      "no relay needed.\n" +
                      "<b>Fix you can do now:</b> F1 &gt; NORS &gt; General &gt; <b>Transport = Relay</b> plus " +
                      "<b>ServerHost</b>."
                    : $"{P2POtherPlayers} other player(s) here, but none of them has a Steam ID yet, so P2P has " +
                      "nobody to send to. If this doesn't clear in a few seconds, use F1 &gt; NORS &gt; General " +
                      "&gt; <b>Transport = Relay</b> plus <b>ServerHost</b>.";

                GUILayout.Label(
                    $"<color={Theme.Hex(Theme.Red)}>⚠ P2P CAN'T REACH ANYONE ON THIS SERVER</color>\n" +
                    $"<color={Theme.Hex(Theme.Txt)}>{body}</color>",
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                GUILayout.Space(4);
            }

            // Unbound PTT is the single most common "the radio doesn't work" cause —
            // never let it be silent.
            if (NorsConfig.PttKey.Value == KeyCode.None)
            {
                GUILayout.Label($"<color={Theme.Hex(Theme.Red)}>⚠ PUSH-TO-TALK IS NOT BOUND — you cannot transmit.</color>",
                    new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold, wordWrap = true });
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Bind CAPS LOCK", GUILayout.Width(130))) NorsConfig.PttKey.Value = KeyCode.CapsLock;
                if (GUILayout.Button("Bind ~", GUILayout.Width(80))) NorsConfig.PttKey.Value = KeyCode.BackQuote;
                if (GUILayout.Button("Bind MOUSE 4", GUILayout.Width(110))) NorsConfig.PttKey.Value = KeyCode.Mouse3;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Radios", _hdr);
            GUILayout.FlexibleSpace();
            // Recovery: the tune keys nudge the TX radio, so a stray key press can leave
            // you off-frequency with no obvious way back. One click restores the defaults.
            if (GUILayout.Button("Reset to defaults", GUILayout.Width(130)))
            {
                _radios.LoadFromConfig();
                NorsPlugin.Log.LogInfo("NORS: radios reset to configured defaults.");
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < _radios.Radios.Count; i++)
                DrawRadio(i, _radios.Radios[i]);

            // Passcode mismatch: someone IS transmitting here, we just can't decode it.
            if (CryptoBlockedMhz > 0f)
            {
                GUILayout.Label(
                    $"<color={Theme.Hex(Theme.Amber)}>LOCK  Encrypted traffic on {CryptoBlockedMhz:000.000} — " +
                    (CryptoBlockedHasKey ? "your passcode doesn't match." : "you have no passcode for it.") +
                    " Use LOCK on that radio.</color>",
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
            }

            // Faction-secure mismatch. Before 0.7.6 this dropped in total silence, which is
            // how "some people just can't hear each other" went undiagnosed for so long.
            if (FactionBlocked)
            {
                GUILayout.Label(
                    $"<color={Theme.Hex(Theme.Amber)}>⚠ Faction-secure traffic dropped — their faction id " +
                    $"{FactionBlockedTheirs} ≠ yours {FactionBlockedMine}.</color>" +
                    (FactionBlockedLegacy
                        ? $"<color={Theme.Hex(Theme.Dim)}>\nOne of you is on NORS 0.7.5 or older, so this " +
                          "compared unreliable network ids. Both update to 0.7.6+ and it stops happening.</color>"
                        : $"<color={Theme.Hex(Theme.Dim)}>\nYou really are on different factions — or set " +
                          "FactionSecureByDefault = false to talk across them.</color>"),
                    new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
            }

            // --- talkers ---
            GUILayout.Space(4);
            GUILayout.Label("Receiving", _hdr);
            if (Talkers == null || Talkers.Count == 0)
                GUILayout.Label("—");
            else
                foreach (var t in Talkers) GUILayout.Label("▸ " + t);

            // --- players / moderation ---
            GUILayout.Space(4);
            GUILayout.Label("Players", _hdr);
            if (Roster == null || Roster.Count == 0)
                GUILayout.Label("—");
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Min(150f, 24f * Roster.Count + 8f)));
                foreach (var p in Roster)
                {
                    bool me = P2PMode ? (p.SteamId != 0 && p.SteamId == MySteamId) : (p.ClientId == MyClientId);
                    bool sameFaction = p.FactionId == MyFactionId;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(me ? p.Name + " (you)" : (sameFaction ? p.Name : p.Name + " <color=#888888>[other]</color>"),
                        new GUIStyle(GUI.skin.label) { richText = true });
                    GUILayout.FlexibleSpace();
                    if (!me)
                    {
                        if (P2PMode)
                        {
                            // P2P: the game host moderates by Steam id (mute/ban), no relay/vote.
                            if (CanModerate && p.SteamId != 0)
                            {
                                bool banned = IsBanned != null && IsBanned(p.SteamId);
                                if (!banned && GUILayout.Button("Ban", GUILayout.Width(50))) OnHostBan?.Invoke(p.SteamId);
                                if (banned && GUILayout.Button("Unban", GUILayout.Width(58))) OnHostUnban?.Invoke(p.SteamId);
                            }
                        }
                        else
                        {
                            // Relay: faction vote-kick + (admin/host) kick/ban by client id.
                            if (sameFaction && GUILayout.Button("Vote", GUILayout.Width(50))) OnVoteKick?.Invoke(p.ClientId);
                            if (AdminAuthed || LocalIsHost)
                            {
                                if (GUILayout.Button("Kick", GUILayout.Width(50))) OnAdminKick?.Invoke(p.ClientId);
                                if (GUILayout.Button("Ban", GUILayout.Width(46))) OnAdminBan?.Invoke(p.ClientId);
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            // --- admin / host moderation ---
            GUILayout.BeginHorizontal();
            if (P2PMode)
            {
                GUILayout.Label(
                    LocalIsHost ? "<color=#7CFC7C>You host this game — Ban/Unban moderate your players</color>"
                    : CanModerate ? "<color=#7CFC7C>You are a listed moderator — Ban/Unban apply for everyone who lists you</color>"
                    // On a dedicated server there is no host player at all, so saying "the host
                    // moderates" would point people at somebody who doesn't exist.
                    : UdpTransport || MySteamId == 0
                        ? "<color=#999999>P2P voice — moderated by the Steam ids in Moderation/Moderators</color>"
                        : "<color=#999999>P2P voice — the game host moderates</color>", rich);
            }
            else if (AdminAuthed)
            {
                GUILayout.Label("<color=#7CFC7C>Master admin</color>", rich);
            }
            else if (LocalIsHost)
            {
                GUILayout.Label("<color=#7CFC7C>You host this server — Kick/Ban moderate your players</color>", rich);
            }
            else
            {
                if (_adminPwField == null) _adminPwField = AdminPassword ?? "";
                GUILayout.Label("Admin", GUILayout.Width(46));
                _adminPwField = GUILayout.PasswordField(_adminPwField, '*', GUILayout.Width(120));
                if (GUILayout.Button("Login", GUILayout.Width(60))) OnAdminLogin?.Invoke(_adminPwField);
            }
            GUILayout.EndHorizontal();

            // --- notices ---
            if (Notices != null && Notices.Count > 0)
            {
                GUILayout.Space(2);
                for (int i = Notices.Count - 1; i >= 0 && i >= Notices.Count - 4; i--)
                    GUILayout.Label("<color=#cfcfcf>• " + Notices[i] + "</color>", rich);
            }

            // --- diagnostics ---
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#8fb8ff>mic {MicInfo}  ·  TX {TxFrames}  ·  RX {RxFrames}  ·  talkers {TalkerCount}</color>", rich);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_showDiag ? "diag ▴" : "diag ▾", GUILayout.Width(56))) _showDiag = !_showDiag;
            GUILayout.EndHorizontal();

            // Everything needed to explain "we can't hear each other" from one screenshot:
            // who you are, which faction key you're using, and whether P2P has anyone to send to.
            if (_showDiag)
            {
                string faction = MyStableFactionId != 0
                    ? $"faction {MyStableFactionId}"
                    : $"<color={Theme.Hex(Theme.Amber)}>faction ?（legacy netid {MyFactionId}）</color>";
                GUILayout.Label(
                    $"<color=#999999>{(P2PMode ? "P2P" : "relay")} · " +
                    $"{(UdpTransport ? "UDP socket" : "steam socket")} · {faction} · " +
                    $"steam {(MySteamId != 0 ? MySteamId.ToString() : "—")}</color>", rich);
                GUILayout.Label(
                    $"<color=#999999>peers {PeerCount}/{OtherPlayers} · secure-by-default " +
                    $"{(NorsConfig.FactionSecureByDefault.Value ? "on" : "off")} · v{NorsPlugin.Version}</color>", rich);
            }

            GUILayout.Space(2);
            GUILayout.Label($"<color=#999999>PTT: {NorsConfig.PttKey.Value}   Cycle TX: {NorsConfig.CycleTxRadioKey.Value}   " +
                            $"Tune: {NorsConfig.TuneDownKey.Value}/{NorsConfig.TuneUpKey.Value}</color>", rich);

            GUI.enabled = true;
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawRadio(int index, Radio r)
        {
            bool isTx = index == _radios.TxIndex;
            GUIStyle rowStyle = isTx ? Theme.RowTx : (r.HasCrypto ? Theme.RowCrypto : Theme.Row);
            GUILayout.BeginVertical(rowStyle);

            if (NorsConfig.CompactRadios.Value) DrawRadioCompact(index, r, isTx);
            else DrawRadioFull(index, r, isTx);

            if (_cryptoEditIndex == index) DrawCryptoEditor(r);

            GUILayout.EndVertical();
        }

        /// <summary>One line per radio: TX · label · freq · mode · tune · RX · SEC · lock · volume.</summary>
        private void DrawRadioCompact(int index, Radio r, bool isTx)
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(isTx, "TX", GUILayout.Width(32)) && !isTx) _radios.TxIndex = index;

            GUILayout.Label($"<color={Theme.Hex(isTx ? Theme.Green : Theme.Cyan)}><b>{r.Label}</b></color>",
                Theme.Label, GUILayout.Width(24));
            GUILayout.Label($"{r.FreqMHz:000.000}", Theme.Value, GUILayout.Width(58));

            if (GUILayout.Button(ModLabel(r.Mod), GUILayout.Width(38))) r.Mod = NextMod(r.Mod);
            if (GUILayout.Button("–", GUILayout.Width(22))) _radios.Tune(r, -1);
            if (GUILayout.Button("+", GUILayout.Width(22))) _radios.Tune(r, +1);

            r.Rx = GUILayout.Toggle(r.Rx, "RX", GUILayout.Width(38));
            r.Secure = GUILayout.Toggle(r.Secure, "SEC", GUILayout.Width(40));

            // Passcode: lit when this channel is encrypted. Click to set or clear it —
            // without this there was no in-game way to tell (or undo) an encrypted radio.
            bool wantEdit = GUILayout.Toggle(_cryptoEditIndex == index,
                r.HasCrypto ? $"<color={Theme.Hex(Theme.Amber)}>LOCK</color>" : "lock",
                new GUIStyle(GUI.skin.button) { richText = true, fontSize = 10 }, GUILayout.Width(38));
            if (wantEdit && _cryptoEditIndex != index) { _cryptoEditIndex = index; _cryptoField = r.Crypto ?? ""; }
            else if (!wantEdit && _cryptoEditIndex == index) _cryptoEditIndex = -1;

            r.Volume = GUILayout.HorizontalSlider(r.Volume, 0f, 1f, GUILayout.MinWidth(40));
            GUILayout.EndHorizontal();
        }

        /// <summary>Original two-row layout (UI/CompactRadios = false): roomier controls.</summary>
        private void DrawRadioFull(int index, Radio r, bool isTx)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(isTx, isTx ? "TX" : "  ", GUILayout.Width(34)) && !isTx)
                _radios.TxIndex = index;
            GUILayout.Label($"<b>{r.Label}</b>  {r.FreqMHz:000.000}" +
                            (r.HasCrypto ? $"  <color={Theme.Hex(Theme.Amber)}>LOCK</color>" : ""), Theme.Label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("–", GUILayout.Width(26))) _radios.Tune(r, -1);
            if (GUILayout.Button("+", GUILayout.Width(26))) _radios.Tune(r, +1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(ModLabel(r.Mod), GUILayout.Width(52))) r.Mod = NextMod(r.Mod);
            r.Rx = GUILayout.Toggle(r.Rx, "RX", GUILayout.Width(40));
            r.Secure = GUILayout.Toggle(r.Secure, r.Secure ? "SECURE" : "CLEAR", GUILayout.Width(68));
            bool wantEdit = GUILayout.Toggle(_cryptoEditIndex == index, "LOCK", GUILayout.Width(52));
            if (wantEdit && _cryptoEditIndex != index) { _cryptoEditIndex = index; _cryptoField = r.Crypto ?? ""; }
            else if (!wantEdit && _cryptoEditIndex == index) _cryptoEditIndex = -1;
            GUILayout.Label("Vol", Theme.LabelDim, GUILayout.Width(26));
            r.Volume = GUILayout.HorizontalSlider(r.Volume, 0f, 1f, GUILayout.MinWidth(50));
            GUILayout.EndHorizontal();
        }

        /// <summary>Inline passcode editor for one radio. Empty passcode = normal open channel.</summary>
        private void DrawCryptoEditor(Radio r)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Passcode", Theme.LabelDim, GUILayout.Width(60));
            _cryptoField = GUILayout.TextField(_cryptoField ?? "", 32, GUILayout.MinWidth(90));
            if (GUILayout.Button("SET", GUILayout.Width(44)))
            {
                r.SetCrypto((_cryptoField ?? "").Trim());
                _cryptoEditIndex = -1;
                NorsPlugin.Log.LogInfo($"NORS: {r.Label} passcode {(r.HasCrypto ? "set" : "cleared")}.");
            }
            if (GUILayout.Button("CLEAR", GUILayout.Width(56)))
            {
                r.SetCrypto("");
                _cryptoField = "";
                _cryptoEditIndex = -1;
                NorsPlugin.Log.LogInfo($"NORS: {r.Label} passcode cleared.");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(r.HasCrypto
                    ? $"<color={Theme.Hex(Theme.Amber)}>Encrypted — only radios with this exact passcode hear this channel.</color>"
                    : $"<color={Theme.Hex(Theme.Dim)}>Empty = open channel: anyone on this frequency can hear you.</color>",
                Theme.Label);
        }

        private static string ModLabel(Modulation m) =>
            m == Modulation.AM ? "AM" : m == Modulation.FM ? "FM" : "OFF";

        private static Modulation NextMod(Modulation m) =>
            m == Modulation.AM ? Modulation.FM : m == Modulation.FM ? Modulation.Disabled : Modulation.AM;

        private static string Bar(float level, int width)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(level * width), 0, width);
            return "[" + new string('|', filled) + new string('·', width - filled) + "]";
        }
    }
}
