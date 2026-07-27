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
        public string AdminPassword = "";

        // P2P mode (voice direct over Steam; host moderates by Steam id, no relay).
        public bool P2PMode;
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
                            if (LocalIsHost && p.SteamId != 0)
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
                GUILayout.Label(LocalIsHost
                    ? "<color=#7CFC7C>You host this game — Ban/Unban moderate your players</color>"
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
            GUILayout.Label($"<color=#8fb8ff>mic {MicInfo}  ·  TX {TxFrames}  ·  RX {RxFrames}  ·  talkers {TalkerCount}</color>", rich);

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
