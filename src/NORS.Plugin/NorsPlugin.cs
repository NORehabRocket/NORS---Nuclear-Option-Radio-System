using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace NORS.Plugin
{
    [BepInPlugin(Guid, "Nuclear Option Radio System", "0.7.0")]
    public class NorsPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.dsr.nors";

        internal static NorsPlugin Instance;
        internal static ManualLogSource Log;

        private GameObject _hub;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            NorsConfig.Init(Config);

            _hub = new GameObject("NorsHub") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(_hub);
            _hub.AddComponent<NorsHub>();

            Log.LogInfo("Nuclear Option Radio System v0.7.0 online. Press " +
                        NorsConfig.PanelKey.Value + " for the radio panel.");
        }
    }

    /// <summary>
    /// Keyboard helpers on the legacy <see cref="Input"/> API. The new Input System
    /// (Keyboard.current) didn't deliver keys on some clients while BepInEx/legacy input did,
    /// so we poll legacy input — which is the same backend ConfigurationManager uses.
    /// </summary>
    internal static class Keys
    {
        private static bool _warned;

        public static bool Pressed(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            try { return Input.GetKeyDown(k); }
            catch (Exception e) { WarnOnce(e); return false; }
        }

        public static bool Held(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            try { return Input.GetKey(k); }
            catch (Exception e) { WarnOnce(e); return false; }
        }

        private static void WarnOnce(Exception e)
        {
            if (_warned) return;
            _warned = true;
            NorsPlugin.Log.LogWarning("NORS: legacy input unavailable (" + e.Message + ").");
        }
    }
}
