using System;
using NORS.Common;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Dependency-free voice codec used instead of Opus (Concentus fails to load on the game's Mono
    /// runtime because its netstandard build references System.Runtime.* contract assemblies that
    /// aren't present). Pipeline frames are 48 kHz mono (<see cref="NorsProtocol.FrameSamples"/>);
    /// on the wire we downsample to 16 kHz 16-bit linear PCM (≈256 kbps while transmitting) and
    /// reconstruct on decode. Same method shape as the old Opus wrapper so callers are unchanged.
    /// </summary>
    internal static class RawCodec
    {
        public const int Decimation = 3;                                  // 48 kHz -> 16 kHz
        public const int WireSamples = NorsProtocol.FrameSamples / Decimation; // 960 -> 320
        public const int WireBytes = WireSamples * 2;                      // 16-bit
    }

    internal sealed class RawEncoderStream
    {
        private readonly byte[] _out = new byte[RawCodec.WireBytes];

        /// <summary>Encodes one 48 kHz frame. Returns encoded length; bytes are in <paramref name="data"/>.</summary>
        public int Encode(float[] pcm, out byte[] data)
        {
            data = _out;
            int o = 0;
            for (int i = 0; i < RawCodec.WireSamples; i++)
            {
                // Average each group of 3 input samples (cheap anti-alias) then quantise to int16.
                int b = i * RawCodec.Decimation;
                float avg = (pcm[b] + pcm[b + 1] + pcm[b + 2]) * (1f / 3f);
                short s = ToShort(avg);
                _out[o++] = (byte)s;
                _out[o++] = (byte)(s >> 8);
            }
            return o;
        }

        private static short ToShort(float f)
        {
            if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
            return (short)(f * 32767f);
        }
    }

    internal sealed class RawDecoderStream
    {
        private readonly float[] _lo = new float[RawCodec.WireSamples + 1];

        /// <summary>Decodes one packet into <paramref name="pcm"/> (length >= FrameSamples). Returns samples produced.</summary>
        public int Decode(byte[] data, int len, float[] pcm)
        {
            int n = Math.Min(len, RawCodec.WireBytes) / 2;
            if (n <= 0)
            {
                Array.Clear(pcm, 0, NorsProtocol.FrameSamples);
                return NorsProtocol.FrameSamples;
            }

            for (int i = 0; i < n; i++)
            {
                short s = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
                _lo[i] = s * (1f / 32767f);
            }
            _lo[n] = _lo[n - 1]; // guard sample for interpolation

            // Linear upsample 16 kHz -> 48 kHz (integer ratio = 3).
            int outN = n * RawCodec.Decimation;
            if (outN > NorsProtocol.FrameSamples) outN = NorsProtocol.FrameSamples;
            for (int j = 0; j < outN; j++)
            {
                float pos = j / (float)RawCodec.Decimation;
                int i0 = (int)pos;
                float frac = pos - i0;
                pcm[j] = _lo[i0] * (1f - frac) + _lo[i0 + 1] * frac;
            }
            for (int j = outN; j < NorsProtocol.FrameSamples; j++) pcm[j] = 0f;
            return NorsProtocol.FrameSamples;
        }
    }
}
