using System;
using System.Text;

namespace NORS.Common
{
    /// <summary>Little-endian binary writer over a growable buffer. Reusable to avoid GC churn on the voice path.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buf;
        private int _pos;

        public PacketWriter(int capacity = NorsProtocol.MaxDatagram)
        {
            _buf = new byte[capacity];
        }

        public int Length => _pos;
        public byte[] Buffer => _buf;

        public void Reset() => _pos = 0;

        private void Ensure(int n)
        {
            if (_pos + n > _buf.Length)
                Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _pos + n));
        }

        public void Byte(byte b) { Ensure(1); _buf[_pos++] = b; }
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);

        public void UShort(ushort v)
        {
            Ensure(2);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
        }

        public void UInt(uint v)
        {
            Ensure(4);
            _buf[_pos++] = (byte)v;
            _buf[_pos++] = (byte)(v >> 8);
            _buf[_pos++] = (byte)(v >> 16);
            _buf[_pos++] = (byte)(v >> 24);
        }

        public void Int(int v) => UInt((uint)v);

        public void ULong(ulong v)
        {
            UInt((uint)v);
            UInt((uint)(v >> 32));
        }

        public void Float(float v) => UInt((uint)BitConverter.SingleToInt32Bits(v));

        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { Byte(0); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 255) Array.Resize(ref bytes, 255);
            Byte((byte)bytes.Length);
            Bytes(bytes, 0, bytes.Length);
        }

        public void Bytes(byte[] src, int offset, int count)
        {
            Ensure(count);
            Array.Copy(src, offset, _buf, _pos, count);
            _pos += count;
        }
    }

    /// <summary>Little-endian binary reader over a slice of a buffer.</summary>
    public struct PacketReader
    {
        private readonly byte[] _b;
        private int _pos;
        private readonly int _end;

        public PacketReader(byte[] buffer, int offset, int length)
        {
            _b = buffer;
            _pos = offset;
            _end = offset + length;
        }

        public int Remaining => _end - _pos;
        public bool End => _pos >= _end;

        public byte Byte() => _b[_pos++];
        public bool Bool() => _b[_pos++] != 0;

        public ushort UShort()
        {
            ushort v = (ushort)(_b[_pos] | (_b[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public uint UInt()
        {
            uint v = (uint)(_b[_pos] | (_b[_pos + 1] << 8) | (_b[_pos + 2] << 16) | (_b[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int Int() => (int)UInt();

        public ulong ULong()
        {
            ulong lo = UInt();
            ulong hi = UInt();
            return lo | (hi << 32);
        }

        public float Float() => BitConverter.Int32BitsToSingle((int)UInt());

        public string Str()
        {
            int n = _b[_pos++];
            if (n == 0) return string.Empty;
            string s = Encoding.UTF8.GetString(_b, _pos, n);
            _pos += n;
            return s;
        }

        /// <summary>Copies <paramref name="count"/> bytes into <paramref name="dst"/> and advances.</summary>
        public void Bytes(byte[] dst, int offset, int count)
        {
            Array.Copy(_b, _pos, dst, offset, count);
            _pos += count;
        }

        public void Skip(int count) => _pos += count;
    }
}
