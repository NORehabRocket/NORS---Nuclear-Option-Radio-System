namespace NORS.Common
{
    /// <summary>
    /// Symmetric payload scrambling for passcode ("encryption") channels — SRS-style.
    /// XOR keystream from an xorshift32 PRNG seeded with (passcode hash ^ frame seq),
    /// so applying it twice with the same key restores the audio. Not cryptography —
    /// it's gameplay crypto: without the passcode (including on older clients that
    /// ignore the key id) the frame plays as harsh noise.
    /// </summary>
    public static class Scramble
    {
        public static uint Fnv1a(string s)
        {
            uint h = 2166136261u;
            if (s != null)
                foreach (char c in s) h = (h ^ c) * 16777619u;
            return h;
        }

        /// <summary>Key id 2..255 for a passcode (0 = clear, 1 = legacy faction-secure).</summary>
        public static byte KeyId(string passcode)
        {
            if (string.IsNullOrEmpty(passcode)) return 0;
            return (byte)(2 + (Fnv1a(passcode) % 254u));
        }

        public static void Apply(byte[] buf, int offset, int len, uint seed)
        {
            uint s = seed;
            if (s == 0) s = 0x9E3779B9u;
            int end = offset + len;
            for (int i = offset; i < end; )
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                buf[i++] ^= (byte)s;
                if (i < end) buf[i++] ^= (byte)(s >> 8);
                if (i < end) buf[i++] ^= (byte)(s >> 16);
                if (i < end) buf[i++] ^= (byte)(s >> 24);
            }
        }
    }
}
