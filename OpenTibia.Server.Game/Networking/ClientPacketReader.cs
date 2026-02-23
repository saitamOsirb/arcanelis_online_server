using System.Text;

namespace OpenTibia.Server.Game.Networking;

public sealed class ClientPacketReader
{
    private readonly byte[] _buf;
    private int _i;

    public ClientPacketReader(byte[] buf, int offset, int length)
    {
        _buf = new byte[length];
        Buffer.BlockCopy(buf, offset, _buf, 0, length);
        _i = 0;
    }

    private int Remaining => _buf.Length - _i;

    public byte ReadU8()
    {
        if (Remaining < 1) throw new InvalidOperationException("Not enough data (u8).");
        return _buf[_i++];
    }

    public ushort ReadU16()
    {
        if (Remaining < 2) throw new InvalidOperationException("Not enough data (u16).");
        ushort v = (ushort)(_buf[_i] | (_buf[_i + 1] << 8));
        _i += 2;
        return v;
    }

    public uint ReadU32()
    {
        if (Remaining < 4) throw new InvalidOperationException("Not enough data (u32).");
        uint v = (uint)(_buf[_i] | (_buf[_i + 1] << 8) | (_buf[_i + 2] << 16) | (_buf[_i + 3] << 24));
        _i += 4;
        return v;
    }

    // CLIENT strings: U8 length + UTF8 bytes
    public string ReadStringU8()
    {
        byte len = ReadU8();
        if (len == 0) return "";
        if (Remaining < len) throw new InvalidOperationException("Not enough data (string u8).");

        string s = Encoding.UTF8.GetString(_buf, _i, len);
        _i += len;
        return s;
    }
}