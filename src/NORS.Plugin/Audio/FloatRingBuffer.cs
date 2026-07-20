using System;

namespace NORS.Plugin.Audio
{
    /// <summary>
    /// Single-producer / single-consumer float ring buffer used as a jitter buffer between the
    /// main thread (which decodes incoming frames) and Unity's audio thread (the PCM reader
    /// callback). Lock-guarded for simplicity; contention is tiny (a few writes/reads per frame).
    /// </summary>
    internal sealed class FloatRingBuffer
    {
        private readonly float[] _buf;
        private int _read;
        private int _write;
        private int _count;
        private readonly object _lock = new object();

        public FloatRingBuffer(int capacity)
        {
            _buf = new float[capacity];
        }

        public int Count { get { lock (_lock) return _count; } }
        public int Capacity => _buf.Length;

        public void Clear()
        {
            lock (_lock) { _read = _write = _count = 0; }
        }

        public void Write(float[] src, int offset, int len)
        {
            lock (_lock)
            {
                for (int i = 0; i < len; i++)
                {
                    if (_count == _buf.Length)
                    {
                        // Overflow: drop the oldest sample to stay current (favours latency over completeness).
                        _read = (_read + 1) % _buf.Length;
                        _count--;
                    }
                    _buf[_write] = src[offset + i];
                    _write = (_write + 1) % _buf.Length;
                    _count++;
                }
            }
        }

        /// <summary>Reads up to <paramref name="len"/> samples into <paramref name="dst"/>; remainder is zero-filled. Returns samples actually read.</summary>
        public int Read(float[] dst, int offset, int len)
        {
            lock (_lock)
            {
                int n = Math.Min(len, _count);
                for (int i = 0; i < n; i++)
                {
                    dst[offset + i] = _buf[_read];
                    _read = (_read + 1) % _buf.Length;
                }
                _count -= n;
                for (int i = n; i < len; i++) dst[offset + i] = 0f;
                return n;
            }
        }
    }
}
