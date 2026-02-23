using System.Buffers.Binary;
using System.Text;

namespace OpenTibia.Server.Game.Networking;

public ref struct PacketReader
{
    private ReadOnlySpan<byte> _buf;
    private int _i;

    public PacketReader(ReadOnlySpan<byte> buf)
    {
        _buf = buf;
        _i = 0;
    }

    public int Remaining => _buf.Length - _i;

    public byte ReadU8()
    {
        if (Remaining < 1) throw new InvalidOperationException("Not enough data (u8).");
        return _buf[_i++];
    }

    public ushort ReadU16()
    {
        if (Remaining < 2) throw new InvalidOperationException("Not enough data (u16).");
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buf.Slice(_i, 2));
        _i += 2;
        return v;
    }

    public uint ReadU32()
    {
        if (Remaining < 4) throw new InvalidOperationException("Not enough data (u32).");
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buf.Slice(_i, 4));
        _i += 4;
        return v;
    }

    public bool ReadBool() => ReadU8() != 0;

    // Asumido: U16 length + UTF8 bytes
    public string ReadString()
    {
        ushort len = ReadU16();
        if (len == 0) return "";
        if (Remaining < len) throw new InvalidOperationException("Not enough data (string).");

        var s = Encoding.UTF8.GetString(_buf.Slice(_i, len));
        _i += len;
        return s;
    }
}