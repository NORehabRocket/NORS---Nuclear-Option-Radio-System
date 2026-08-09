using System;
using BepInEx;
using BepInEx.Logging;
using NORS.Plugin.Server;
using UnityEngine;

namespace NORS.Plugin
{
    [BepInPlugin(Guid, "Nuclear Option Radio System", Version)]
    public class NorsPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.dsr.nors";
        public const string Version = "0.7.7";

        internal static NorsPlugin Instance;
        internal static ManualLogSource Log;

        private GameObject _hub;
        private ServerHost _serverHost;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            NorsConfig.Init(Config);

            // A dedicated server has no local player, no microphone, no panel and (usually) no
            // Steam client. Starting the normal hub there would throw on the first frame, so the
            // server path is entirely separate: host the relay, touch nothing else.
            if (ServerMode.IsDedicatedServer)
            {
                if (NorsConfig.ServerHostRelay.Value)
                {
                    _serverHost = new ServerHost();
                    _serverHost.Start();
                }
                else
                {
                    Log.LogInfo("NORS: dedicated server detected, but Server/HostVoiceRelay is off - " +
                                "not hosting voice here.");
                }
                return;
            }

            _hub = new GameObject("NorsHub") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(_hub);
            _hub.AddComponent<NorsHub>();

            Log.LogInfo("Nuclear Option Radio System v" + Version + " online. Press " +
                        NorsConfig.PanelKey.Value + " for the radio panel.");
        }

        private void OnDestroy()
        {
            _serverHost?.Stop();
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
