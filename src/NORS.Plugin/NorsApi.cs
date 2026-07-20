using System;
using UnityEngine;

namespace NORS.Plugin
{
    /// <summary>
    /// Public integration surface for sibling mods (TOWER ATC uses it via
    /// reflection — no compile-time reference either way). Main-thread only.
    ///
    /// External PTT is deadline-based as a stuck-key guard: SetPtt(true) arms
    /// transmit for a few seconds and must be refreshed while held (a browser
    /// button repeats it); SetPtt(false) or silence releases. The regular PTT
    /// key always works in parallel (either source keys the radio).
    /// </summary>
    public static class NorsApi
    {
        public const int Version = 3;

        /// <summary>Seconds one SetPtt(true) keeps transmit armed without a refresh.</summary>
        public const float ExternalPttHoldSeconds = 8f;

        internal static NorsHub Hub;

        private static float _extPttUntil;

        public static bool Available => Hub != null;

        internal static bool ExternalPttHeld => Time.unscaledTime < _extPttUntil;

        /// <summary>Key/release the TX radio from outside NORS (refresh while held).</summary>
        public static void SetPtt(bool down)
        {
            _extPttUntil = down ? Time.unscaledTime + ExternalPttHoldSeconds : 0f;
        }

        /// <summary>True while the local mic is actually transmitting.</summary>
        public static bool Transmitting => Hub != null && Hub.ApiTransmitting;

        /// <summary>Callsigns currently heard on any monitored frequency.</summary>
        public static string[] GetTalkers()
        {
            return Hub != null ? Hub.ApiTalkers() : Array.Empty<string>();
        }

        public static int RadioCount => Hub != null ? Hub.ApiRadioCount : 0;

        /// <summary>"label|mhz|modulation|rx01|tx01" for display, or null.</summary>
        public static string GetRadioInfo(int index)
        {
            return Hub != null ? Hub.ApiRadioInfo(index) : null;
        }

        /// <summary>Tune the current TX radio (MHz, clamped to the radio's band).</summary>
        public static bool TuneTx(float mhz)
        {
            return Hub != null && Hub.ApiTuneTx(mhz);
        }

        /// <summary>Make radio <paramref name="index"/> the transmit radio (fails on disabled radios).</summary>
        public static bool SelectTx(int index)
        {
            return Hub != null && Hub.ApiSelectTx(index);
        }

        /// <summary>Tune a specific radio (MHz, clamped to the band).</summary>
        public static bool TuneRadio(int index, float mhz)
        {
            return Hub != null && Hub.ApiTuneRadio(index, mhz);
        }

        /// <summary>Turn monitoring of a specific radio on/off.</summary>
        public static bool SetRx(int index, bool rx)
        {
            return Hub != null && Hub.ApiSetRx(index, rx);
        }

        /// <summary>Set/clear a radio's channel passcode (empty clears; see Radio.SetCrypto).</summary>
        public static bool SetCrypto(int index, string passcode)
        {
            return Hub != null && Hub.ApiSetCrypto(index, passcode);
        }
    }
}
