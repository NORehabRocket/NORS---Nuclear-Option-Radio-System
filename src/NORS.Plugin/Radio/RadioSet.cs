using System.Collections.Generic;
using NORS.Common;

namespace NORS.Plugin.Comms
{
    /// <summary>A single tunable radio.</summary>
    internal sealed class Radio
    {
        public string Label;
        public int FreqKHz;
        public Modulation Mod;
        public bool Rx = true;       // monitoring this frequency
        public float Volume = 1f;
        public bool Secure = true;   // transmit encrypted (faction-only)

        // Passcode channel (SRS-style): audio is scrambled with the passcode hash,
        // so only radios holding the same passcode decode it — separates a net even
        // from same-faction players. Empty = normal channel.
        public string Crypto = "";
        public uint CryptoHash;
        public byte KeyId;           // 0 = clear, 1 = faction-secure (legacy), 2..255 = passcode

        public void SetCrypto(string passcode)
        {
            Crypto = passcode ?? "";
            CryptoHash = Crypto.Length == 0 ? 0u : Scramble.Fnv1a(Crypto);
            KeyId = Scramble.KeyId(Crypto);
        }

        public bool HasCrypto => KeyId >= 2;

        public float FreqMHz => FreqKHz / 1000f;
    }

    /// <summary>
    /// The player's stack of radios plus which one is selected for transmit. Mirrors a real
    /// multi-radio cockpit: every Rx radio is monitored simultaneously; you transmit on one.
    /// </summary>
    internal sealed class RadioSet
    {
        public readonly List<Radio> Radios = new List<Radio>();
        public int TxIndex;

        /// <summary>Channel spacing when tuning with the keys (kHz). 25 kHz is the classic aviation step.</summary>
        public int StepKHz = 25;
        public int MinKHz = 30_000;     // 30 MHz
        public int MaxKHz = 400_000;    // 400 MHz

        public Radio Tx => (TxIndex >= 0 && TxIndex < Radios.Count) ? Radios[TxIndex] : null;

        public void LoadFromConfig()
        {
            Radios.Clear();
            Radios.Add(Make("R1", NorsConfig.Radio1Freq.Value, NorsConfig.Radio1Mod.Value, NorsConfig.Radio1Crypto.Value));
            Radios.Add(Make("R2", NorsConfig.Radio2Freq.Value, NorsConfig.Radio2Mod.Value, NorsConfig.Radio2Crypto.Value));
            Radios.Add(Make("R3", NorsConfig.Radio3Freq.Value, NorsConfig.Radio3Mod.Value, NorsConfig.Radio3Crypto.Value));
            Radios.Add(Make("R4", NorsConfig.Radio4Freq.Value, NorsConfig.Radio4Mod.Value, NorsConfig.Radio4Crypto.Value));
            Radios.Add(Make("R5", NorsConfig.Radio5Freq.Value, NorsConfig.Radio5Mod.Value, NorsConfig.Radio5Crypto.Value));
            Radios.Add(Make("R6", NorsConfig.Radio6Freq.Value, NorsConfig.Radio6Mod.Value, NorsConfig.Radio6Crypto.Value));
            TxIndex = 0;
        }

        private static Radio Make(string label, float mhz, Modulation mod, string crypto)
        {
            var r = new Radio
            {
                Label = label,
                FreqKHz = (int)System.Math.Round(mhz * 1000f),
                Mod = mod,
                Secure = NorsConfig.FactionSecureByDefault.Value,
            };
            r.SetCrypto((crypto ?? "").Trim());
            return r;
        }

        public void CycleTx()
        {
            if (Radios.Count == 0) return;
            // Skip disabled radios when cycling.
            for (int i = 0; i < Radios.Count; i++)
            {
                TxIndex = (TxIndex + 1) % Radios.Count;
                if (Radios[TxIndex].Mod != Modulation.Disabled) break;
            }
        }

        public void Tune(Radio r, int deltaSteps)
        {
            if (r == null) return;
            int v = r.FreqKHz + deltaSteps * StepKHz;
            if (v < MinKHz) v = MinKHz;
            if (v > MaxKHz) v = MaxKHz;
            r.FreqKHz = v;
        }

        /// <summary>Fills <paramref name="buffer"/> with the frequencies we monitor; returns the count.</summary>
        public int GetRxFrequencies(int[] buffer)
        {
            int n = 0;
            for (int i = 0; i < Radios.Count && n < buffer.Length; i++)
            {
                var r = Radios[i];
                if (r.Rx && r.Mod != Modulation.Disabled)
                    buffer[n++] = r.FreqKHz;
            }
            return n;
        }

        /// <summary>Finds the monitoring radio tuned to <paramref name="freqKHz"/> (or null).</summary>
        public Radio FindReceiver(int freqKHz)
        {
            for (int i = 0; i < Radios.Count; i++)
            {
                var r = Radios[i];
                if (r.Rx && r.Mod != Modulation.Disabled && r.FreqKHz == freqKHz)
                    return r;
            }
            return null;
        }
    }
}
