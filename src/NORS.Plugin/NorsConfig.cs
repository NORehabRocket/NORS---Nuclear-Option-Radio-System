using BepInEx.Configuration;
using NORS.Common;
using UnityEngine;

namespace NORS.Plugin
{
    /// <summary>How voice is carried. P2P = direct over Steam (no server). Relay = a standalone relay host.</summary>
    internal enum VoiceTransport { P2P, Relay }

    internal enum MfdCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Which HUD gauge the readout sits next to on the HUD glass.</summary>
    internal enum MfdHudAnchor { Fuel, Throttle }


    /// <summary>Where NORS draws. Hud = on the HUD glass (reliable, every aircraft). Overlay = on a
    /// cockpit MFD panel/radar. BezelPage = a page on the map menu. Auto = bezel if available else MFD.</summary>
    internal enum MfdMode { Auto, BezelPage, Overlay, Hud }

    internal static class NorsConfig
    {
        // ---- General ----
        public static ConfigEntry<bool> MasterEnabled;
        public static ConfigEntry<VoiceTransport> Transport;
        public static ConfigEntry<string> ServerHost;
        public static ConfigEntry<int> ServerPort;
        public static ConfigEntry<string> PlayerNameOverride;
        public static ConfigEntry<bool> AutoConnect;
        public static ConfigEntry<string> AdminPassword;

        // ---- UI ----
        public static ConfigEntry<bool> CompactRadios;

        // ---- Input ----
        public static ConfigEntry<bool> PttSetupDone;
        public static ConfigEntry<KeyCode> PttKey;
        public static ConfigEntry<KeyCode> PanelKey;
        public static ConfigEntry<KeyCode> CycleTxRadioKey;
        public static ConfigEntry<KeyCode> TuneUpKey;
        public static ConfigEntry<KeyCode> TuneDownKey;

        // ---- Audio ----
        public static ConfigEntry<string> MicDevice;
        public static ConfigEntry<float> MicGain;
        public static ConfigEntry<float> ReceiveVolume;
        public static ConfigEntry<float> StaticLevel;
        public static ConfigEntry<bool> OpenAir3D;
        public static ConfigEntry<float> CullSeconds;
        public static ConfigEntry<bool> MicMonitor;

        // ---- MFD readout (on the cockpit tactical screen) ----
        public static ConfigEntry<bool> MfdEnabled;
        public static ConfigEntry<MfdMode> MfdDisplayMode;
        public static ConfigEntry<MfdHudAnchor> MfdHudAnchor;
        public static ConfigEntry<MfdCorner> MfdHudCorner;
        public static ConfigEntry<float> MfdHudOffsetX;
        public static ConfigEntry<float> MfdHudOffsetY;
        public static ConfigEntry<bool> MfdHudVerbose;
        public static ConfigEntry<KeyCode> MfdToggleKey;
        public static ConfigEntry<string> MfdPageLabel;
        public static ConfigEntry<MfdCorner> MfdCornerPos;
        public static ConfigEntry<float> MfdWidthFrac;
        public static ConfigEntry<float> MfdHeightFrac;
        public static ConfigEntry<float> MfdFontScale;
        public static ConfigEntry<bool> MfdShowTalkers;
        public static ConfigEntry<bool> MfdAutoHide;
        public static ConfigEntry<float> MfdShowSeconds;

        // ---- Radios (frequencies stored in MHz for readability) ----
        public static ConfigEntry<float> Radio1Freq;
        public static ConfigEntry<Modulation> Radio1Mod;
        public static ConfigEntry<float> Radio2Freq;
        public static ConfigEntry<Modulation> Radio2Mod;
        public static ConfigEntry<float> Radio3Freq;
        public static ConfigEntry<Modulation> Radio3Mod;
        public static ConfigEntry<float> Radio4Freq;
        public static ConfigEntry<Modulation> Radio4Mod;
        public static ConfigEntry<float> Radio5Freq;
        public static ConfigEntry<Modulation> Radio5Mod;
        public static ConfigEntry<float> Radio6Freq;
        public static ConfigEntry<Modulation> Radio6Mod;
        public static ConfigEntry<string> Radio1Crypto;
        public static ConfigEntry<string> Radio2Crypto;
        public static ConfigEntry<string> Radio3Crypto;
        public static ConfigEntry<string> Radio4Crypto;
        public static ConfigEntry<string> Radio5Crypto;
        public static ConfigEntry<string> Radio6Crypto;

        // ---- Propagation model ----
        public static ConfigEntry<bool> TerrainLOS;
        public static ConfigEntry<int> TerrainMask;
        public static ConfigEntry<float> AmRangeKm;
        public static ConfigEntry<float> FmRangeKm;
        public static ConfigEntry<float> MinGroundRangeKm;
        public static ConfigEntry<float> HorizonFactor;
        public static ConfigEntry<bool> FactionSecureByDefault;
        public static ConfigEntry<bool> JamAffectsRadio;
        public static ConfigEntry<float> JamReference;
        public static ConfigEntry<float> JamRadioEffect;
        public static ConfigEntry<bool> RelayViaRadome;
        public static ConfigEntry<bool> RelayGroundStations;
        public static ConfigEntry<float> RelayMinAltitudeMeters;
        public static ConfigEntry<float> RelayQualityFactor;
        public static ConfigEntry<bool> DeadTalkFromBase;

        public static void Init(ConfigFile cfg)
        {
            MasterEnabled = cfg.Bind("General", "MasterEnabled", true, "Master switch for the NORS radio system.");
            Transport = cfg.Bind("General", "Transport", VoiceTransport.P2P,
                "P2P = voice goes directly between players over Steam (no server, no port forwarding). " +
                "Relay = everyone connects to a standalone NORS relay host (central admin/bans). Both players must match.");
            ServerHost = cfg.Bind("General", "ServerHost", "127.0.0.1",
                "Hostname or IP of the NORS relay server everyone connects to (like an SRS server address).");
            ServerPort = cfg.Bind("General", "ServerPort", NorsProtocol.DefaultPort, "UDP port of the relay server.");
            PlayerNameOverride = cfg.Bind("General", "CallsignOverride", "",
                "Optional radio callsign. Leave blank to use your in-game player name.");
            AutoConnect = cfg.Bind("General", "AutoConnect", true,
                "Automatically connect to the relay when you spawn into a mission.");
            AdminPassword = cfg.Bind("General", "AdminPassword", "",
                "If the relay has remote admin enabled, set its admin password here to unlock in-game kick/ban from the radio panel.");

            CompactRadios = cfg.Bind("UI", "CompactRadios", true,
                "Radio panel layout: one compact line per radio (recommended with 6 radios). " +
                "Turn off for the taller two-row layout with a larger volume slider.");

            PttKey = cfg.Bind("Input", "PushToTalk", KeyCode.None,
                "Hold to transmit on the selected radio. Unbound by default (T would fight the game's chat key) — the first-launch popup asks you to pick one.");
            PttSetupDone = cfg.Bind("Input", "PttSetupDone", false,
                "Internal: the first-launch push-to-talk setup was completed. Set to false to see the setup popup again.");
            PanelKey = cfg.Bind("Input", "TogglePanel", KeyCode.F7, "Show / hide the radio panel.");
            CycleTxRadioKey = cfg.Bind("Input", "CycleTxRadio", KeyCode.Y, "Cycle which radio you transmit on.");
            TuneUpKey = cfg.Bind("Input", "TuneUp", KeyCode.Period, "Tune the selected radio up one step.");
            TuneDownKey = cfg.Bind("Input", "TuneDown", KeyCode.Comma, "Tune the selected radio down one step.");

            MicDevice = cfg.Bind("Audio", "MicDevice", "",
                "Microphone device name. Leave blank for the system default. Press the panel key to see available devices.");
            MicGain = cfg.Bind("Audio", "MicGain", 2.5f,
                new ConfigDescription("Input gain applied to your microphone.", new AcceptableValueRange<float>(0.1f, 10f)));
            ReceiveVolume = cfg.Bind("Audio", "ReceiveVolume", 1.6f,
                new ConfigDescription("Master volume of incoming radio voice.", new AcceptableValueRange<float>(0f, 4f)));
            StaticLevel = cfg.Bind("Audio", "StaticLevel", 0.25f,
                new ConfigDescription("How much radio static/noise is mixed in as signal quality drops.", new AcceptableValueRange<float>(0f, 1f)));
            OpenAir3D = cfg.Bind("Audio", "OpenAir3D", false,
                "If true, voice is positioned in 3D world space (you hear the jet's direction). If false, classic in-headset radio (recommended).");
            CullSeconds = cfg.Bind("Audio", "CullSeconds", 3f,
                "Seconds of silence before a talker's audio voice is torn down.");
            MicMonitor = cfg.Bind("Audio", "MicMonitor", false,
                "Sidetone: hear your own transmission (decoded locally). Use it to test that your mic -> playback chain works without needing a second player.");

            MfdEnabled = cfg.Bind("MFD", "Enabled", true,
                "Draw a NORS readout (radios + who's talking) on the cockpit MFD, so you don't need the F7 panel open.");
            MfdDisplayMode = cfg.Bind("MFD", "Mode", MfdMode.Hud,
                "Currently the readout always draws on the HUD (near FUEL/THROTTLE). The Overlay (cockpit MFD panel) and BezelPage (map menu) modes are temporarily disabled while their placement is fixed, so this setting has no effect for now.");
            MfdHudAnchor = cfg.Bind("MFD", "HudAnchor", global::NORS.Plugin.MfdHudAnchor.Fuel,
                "Which HUD gauge the readout sits next to: Fuel or Throttle (Throttle usually has more free space and fewer other mods).");
            MfdHudCorner = cfg.Bind("MFD", "HudCorner", MfdCorner.BottomLeft,
                "Fallback only: which HUD corner the readout uses if the chosen gauge can't be found.");
            MfdHudOffsetX = cfg.Bind("MFD", "HudOffsetX", 0f,
                new ConfigDescription("Nudge the HUD readout horizontally (pixels; + = right). Use this to move it off other mods' fuel/timer overlays.", new AcceptableValueRange<float>(-2000f, 2000f)));
            MfdHudOffsetY = cfg.Bind("MFD", "HudOffsetY", 0f,
                new ConfigDescription("Nudge the HUD readout vertically (pixels; + = up).", new AcceptableValueRange<float>(-2000f, 2000f)));
            MfdHudVerbose = cfg.Bind("MFD", "HudVerbose", false,
                "Off (default) = compact: just the active radio + who's transmitting. On = list all radios with the NORS header.");
            MfdToggleKey = cfg.Bind("MFD", "ToggleKey", KeyCode.None,
                "Optional key to show/hide the cockpit readout (so it isn't permanently over a busy screen). None = always shown.");
            MfdPageLabel = cfg.Bind("MFD", "PageLabel", "RADIO", "Label shown for the NORS page when using BezelPage mode (on the map menu).");
            MfdCornerPos = cfg.Bind("MFD", "Corner", MfdCorner.TopRight,
                "Fallback only: which corner of the MFD screen NORS uses on aircraft where the systems panel can't be found.");
            MfdWidthFrac = cfg.Bind("MFD", "WidthFraction", 0.40f,
                new ConfigDescription("Fallback only: panel width as a fraction of the MFD screen.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            MfdHeightFrac = cfg.Bind("MFD", "HeightFraction", 0.50f,
                new ConfigDescription("Fallback only: panel height as a fraction of the MFD screen.", new AcceptableValueRange<float>(0.1f, 1f)));
            MfdFontScale = cfg.Bind("MFD", "FontScale", 1.0f,
                new ConfigDescription("Scales the on-screen readout text size.", new AcceptableValueRange<float>(0.4f, 3f)));
            MfdShowTalkers = cfg.Bind("MFD", "ShowTalkers", true, "List who is currently transmitting on the readout.");
            MfdAutoHide = cfg.Bind("MFD", "AutoHide", true,
                "Only show the readout briefly when you use the radio (transmit, change frequency/band, switch radio) or when receiving, then hide it. Off = always shown.");
            MfdShowSeconds = cfg.Bind("MFD", "ShowSeconds", 6f,
                new ConfigDescription("How long the readout stays up after radio activity (AutoHide mode).", new AcceptableValueRange<float>(1f, 30f)));

            Radio1Freq = cfg.Bind("Radios", "Radio1Freq", 251.000f, "Radio 1 frequency in MHz (UHF military by default).");
            Radio1Mod = cfg.Bind("Radios", "Radio1Mod", Modulation.AM, "Radio 1 modulation.");
            Radio2Freq = cfg.Bind("Radios", "Radio2Freq", 124.000f, "Radio 2 frequency in MHz (VHF aviation by default).");
            Radio2Mod = cfg.Bind("Radios", "Radio2Mod", Modulation.AM, "Radio 2 modulation.");
            Radio3Freq = cfg.Bind("Radios", "Radio3Freq", 40.000f, "Radio 3 frequency in MHz (VHF-FM tactical by default).");
            Radio3Mod = cfg.Bind("Radios", "Radio3Mod", Modulation.FM, "Radio 3 modulation.");
            Radio4Freq = cfg.Bind("Radios", "Radio4Freq", 68.000f, "Radio 4 frequency in MHz (VHF-FM ground net by default).");
            Radio4Mod = cfg.Bind("Radios", "Radio4Mod", Modulation.FM, "Radio 4 modulation.");
            Radio5Freq = cfg.Bind("Radios", "Radio5Freq", 143.500f, "Radio 5 frequency in MHz.");
            Radio5Mod = cfg.Bind("Radios", "Radio5Mod", Modulation.AM, "Radio 5 modulation.");
            Radio6Freq = cfg.Bind("Radios", "Radio6Freq", 243.000f, "Radio 6 frequency in MHz (GUARD by default).");
            Radio6Mod = cfg.Bind("Radios", "Radio6Mod", Modulation.AM, "Radio 6 modulation.");

            string cryptoHelp = "Passcode for this radio's channel (SRS-style encryption). Everyone who should " +
                "hear this net enters the SAME passcode; everyone else (even same faction) gets noise. Empty = normal channel.";
            Radio1Crypto = cfg.Bind("Radios", "Radio1Crypto", "", cryptoHelp);
            Radio2Crypto = cfg.Bind("Radios", "Radio2Crypto", "", cryptoHelp);
            Radio3Crypto = cfg.Bind("Radios", "Radio3Crypto", "", cryptoHelp);
            Radio4Crypto = cfg.Bind("Radios", "Radio4Crypto", "", cryptoHelp);
            Radio5Crypto = cfg.Bind("Radios", "Radio5Crypto", "", cryptoHelp);
            Radio6Crypto = cfg.Bind("Radios", "Radio6Crypto", "", cryptoHelp);

            TerrainLOS = cfg.Bind("Propagation", "TerrainLineOfSight", true,
                "Block / degrade voice when terrain is between transmitter and receiver (raycast).");
            TerrainMask = cfg.Bind("Propagation", "TerrainLayerMask", 8256,
                "Physics layer mask used for line-of-sight checks. 8256 = the terrain/ground mask the game itself uses.");
            AmRangeKm = cfg.Bind("Propagation", "AmRangeKm", 250f,
                new ConfigDescription("Max AM (VHF/UHF) range at altitude.", new AcceptableValueRange<float>(10f, 1000f)));
            FmRangeKm = cfg.Bind("Propagation", "FmRangeKm", 120f,
                new ConfigDescription("Max FM range at altitude.", new AcceptableValueRange<float>(10f, 1000f)));
            MinGroundRangeKm = cfg.Bind("Propagation", "MinGroundRangeKm", 8f,
                new ConfigDescription("Short-range floor that works even on the deck / behind terrain.", new AcceptableValueRange<float>(1f, 50f)));
            HorizonFactor = cfg.Bind("Propagation", "HorizonFactor", 1.0f,
                new ConfigDescription("Scales the radio-horizon distance (4.12*(sqrt(h_tx)+sqrt(h_rx)) km).", new AcceptableValueRange<float>(0.5f, 2f)));
            FactionSecureByDefault = cfg.Bind("Propagation", "FactionSecureByDefault", true,
                "If true, radios transmit encrypted by default so only your own faction can decode them.");
            JamAffectsRadio = cfg.Bind("Propagation", "JamAffectsRadio", true,
                "Radar jamming interferes with the radio: enemy jamming pods aimed at you, and your own active ECM/jammer, add static to received voice.");
            JamReference = cfg.Bind("Propagation", "JamReferenceAmount", 0.5f,
                new ConfigDescription("Jamming/ECM strength that counts as 'full' radio interference. Lower = jamming bites sooner. Tune if the effect feels too weak or too strong.",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            JamRadioEffect = cfg.Bind("Propagation", "JamRadioEffect", 0.9f,
                new ConfigDescription("How much full jamming degrades the radio (1 = nearly unintelligible static).",
                    new AcceptableValueRange<float>(0f, 1f)));
            RelayViaRadome = cfg.Bind("Propagation", "RelayViaRadome", true,
                "Aircraft carrying a Radome (e.g. the EW-1 Medusa AEW) act as airborne radio relays: when flown high they let transmissions hop over terrain and past normal range, AWACS-style. Friendly relays only.");
            RelayMinAltitudeMeters = cfg.Bind("Propagation", "RelayMinAltitudeMeters", 800f,
                new ConfigDescription("How high (m above sea level) a Radome aircraft must be to act as a relay.",
                    new AcceptableValueRange<float>(0f, 10000f)));
            RelayGroundStations = cfg.Bind("Propagation", "RelayGroundStations", true,
                "Friendly ground/naval radar stations, carriers, and airbase towers also act as radio relays. " +
                "They sit low, so they only cover their local area (unlike a high-flying Radome) — good for around-base and fleet comms.");
            RelayQualityFactor = cfg.Bind("Propagation", "RelayQualityFactor", 0.85f,
                new ConfigDescription("Clarity retained through a relay hop (1 = no loss).",
                    new AcceptableValueRange<float>(0.2f, 1f)));
            DeadTalkFromBase = cfg.Bind("Propagation", "DeadTalkFromBase", true,
                "While dead / on the respawn screen, your radio transmits and receives from your faction's airbase " +
                "instead of nowhere — so you can still coordinate, but only with people your base can reach.");
        }
    }
}
