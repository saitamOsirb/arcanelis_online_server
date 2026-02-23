using System.Buffers.Binary;
using System.Text;

namespace OpenTibia.Server.Game.Networking;

public sealed class PacketWriter
{
    private byte[] _buf;
    private int _i;

    public PacketWriter(int capacity = 256)
    {
        _buf = new byte[Math.Max(capacity, 16)];
        _i = 0;
    }

    public int Length => _i;

    private void Ensure(int extra)
    {
        if (_i + extra <= _buf.Length) return;
        Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _i + extra));
    }

    public void WriteU8(byte v)
    {
        Ensure(1);
        _buf[_i++] = v;
    }

    public void WriteU16(ushort v)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_i, 2), v);
        _i += 2;
    }

    public void WriteU32(uint v)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(_i, 4), v);
        _i += 4;
    }

    public void WriteBool(bool v) => WriteU8(v ? (byte)1 : (byte)0);

    // Asumido: U16 length + UTF8 bytes
    public void WriteString(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            WriteU16(0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("String too long.");

        WriteU16((ushort)bytes.Length);
        Ensure(bytes.Length);
        bytes.CopyTo(_buf.AsSpan(_i));
        _i += bytes.Length;
    }

    public ArraySegment<byte> ToSegment() => new(_buf, 0, _i);

    public byte[] ToArray()
    {
        var outBuf = new byte[_i];
        Buffer.BlockCopy(_buf, 0, outBuf, 0, _i);
        return outBuf;
    }
}