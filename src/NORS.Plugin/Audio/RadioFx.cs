using System;
using NORS.Common;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Per-talker DSP that turns clean decoded voice into a radio-sounding signal:
    /// a band-pass voice character, additive static that grows as signal quality falls,
    /// and a soft clip. Stateful (filter memory) so it must be one instance per talker.
    /// </summary>
    internal sealed class RadioFx
    {
        // One-pole high-pass + low-pass states give a rough comms band-pass (~300 Hz – 3.4 kHz feel).
        private float _hpPrevIn, _hpPrevOut, _lpPrev;
        private readonly Random _rng = new Random();

        // Smoothed control values so quality changes don't click.
        private float _q = 1f;
        private float _vol = 1f;

        public void Process(float[] pcm, int n, float quality, float volume, Modulation mod, float staticLevel)
        {
            // Smooth toward the new targets across the frame.
            float qTarget = Mathf01(quality);
            float vTarget = Math.Max(0f, volume);

            // How much of the radio "voice character" band-pass to apply. Players who struggle to make
            // people out can dial this down (0 = clean, unprocessed voice) without losing the static
            // cue, which is a separate control.
            float filter = Mathf01(NorsConfig.FilterStrength.Value);

            // Makeup gain: the band-pass below sheds a lot of amplitude, so without a big boost the
            // voice ends up far quieter than the additive static (the #1 user complaint). Push it well
            // above unity and let the limiter catch peaks (gives it radio "crunch"). Dry voice needs
            // none of that, so scale the makeup with the filter or turning it down blows the level out.
            float wetGain = 2.8f + 0.7f * _q;                   // loud; thins only slightly when weak
            float voiceGain = 1f + (wetGain - 1f) * filter;
            // Static stays clearly UNDER the voice. AM is noisier than FM (FM's main advantage).
            float noiseGain = (1f - _q) * staticLevel * (mod == Modulation.AM ? 0.45f : 0.22f);

            const float hpA = 0.92f;   // high-pass coefficient
            const float lpA = 0.45f;   // low-pass smoothing

            for (int i = 0; i < n; i++)
            {
                // glide controls
                _q += (qTarget - _q) * 0.02f;
                _vol += (vTarget - _vol) * 0.05f;

                float x = pcm[i];

                // high-pass
                float hp = hpA * (_hpPrevOut + x - _hpPrevIn);
                _hpPrevIn = x;
                _hpPrevOut = hp;

                // low-pass
                _lpPrev += lpA * (hp - _lpPrev);
                float band = _lpPrev;

                // Blend filtered against the dry sample so the effect is continuously dialable.
                // Filter state keeps running either way, so moving the slider never pops.
                float shaped = x + (band - x) * filter;

                // additive static (more when weak)
                float noise = (float)(_rng.NextDouble() * 2.0 - 1.0) * noiseGain;

                float outSample = (shaped * voiceGain + noise) * _vol;

                // Hard limit. The old soft-clip squashed anything over 1 down to ~0.5-0.6, which
                // actually made loud voice quieter; clamping keeps it loud (and clips = radio crunch).
                if (outSample > 1f) outSample = 1f;
                else if (outSample < -1f) outSample = -1f;

                pcm[i] = outSample;
            }
        }

        private static float Mathf01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
