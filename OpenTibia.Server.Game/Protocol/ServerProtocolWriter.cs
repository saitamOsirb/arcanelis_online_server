using OpenTibia.Server.Game.Networking;

namespace OpenTibia.Server.Game.Protocol;

public static class ServerProtocolWriter
{
    // ---------- Tipos simples ----------
    public static void WritePosition(PacketWriter pw, ushort x, ushort y, ushort z)
    {
        pw.WriteU16(x);
        pw.WriteU16(y);
        pw.WriteU16(z);
    }

    public static void WriteItem(PacketWriter pw, ushort clientItemId, byte count)
    {
        pw.WriteU16(clientItemId);
        pw.WriteU8(count);
    }

    public static void WriteNullItem(PacketWriter pw)
    {
        pw.WriteU16(0);
        pw.WriteU8(0);
    }

    public static void WriteConditions(PacketWriter pw, IReadOnlyList<byte> conditions)
    {
        pw.WriteU8((byte)conditions.Count);
        for (int i = 0; i < conditions.Count; i++)
            pw.WriteU8(conditions[i]);
    }

    public static void WriteOutfit(PacketWriter pw, OutfitDto outfit, bool featuresEnabled)
    {
        pw.WriteU16(outfit.Id);

        if (outfit.Details != null)
        {
            pw.WriteU8(outfit.Details.Head);
            pw.WriteU8(outfit.Details.Body);
            pw.WriteU8(outfit.Details.Legs);
            pw.WriteU8(outfit.Details.Feet);
        }
        else
        {
            pw.WriteU8(0); pw.WriteU8(0); pw.WriteU8(0); pw.WriteU8(0);
        }

        if (featuresEnabled)
        {
            pw.WriteU16(outfit.Mount);
            pw.WriteBool(outfit.Mounted);
            pw.WriteBool(outfit.AddonOne);
            pw.WriteBool(outfit.AddonTwo);
        }
        else
        {
            // null(5) en el legacy
            pw.WriteU16(0);
            pw.WriteU8(0);
            pw.WriteU8(0);
            pw.WriteU8(0);
        }
    }

    public static void WriteOutfitList(PacketWriter pw, IReadOnlyList<OutfitListEntryDto> outfits)
    {
        pw.WriteU8((byte)outfits.Count);
        for (int i = 0; i < outfits.Count; i++)
        {
            pw.WriteU16(outfits[i].Id);
            pw.WriteString(outfits[i].Name);
        }
    }

    // ---------- Paquetes completos ----------
    public static byte[] BuildServerData(
        ushort worldWidth,
        ushort worldHeight,
        byte worldDepth,
        byte chunkW,
        byte chunkH,
        byte chunkD,
        byte tickMs,
        ushort clockSpeed,
        string serverVersion,
        ushort clientVersion)
    {
        PacketWriter pw = new PacketWriter(256);
        pw.WriteU8((byte)ServerOpcode.SEND_SERVER_DATA);

        pw.WriteU16(worldWidth);
        pw.WriteU16(worldHeight);
        pw.WriteU8(worldDepth);

        pw.WriteU8(chunkW);
        pw.WriteU8(chunkH);
        pw.WriteU8(chunkD);

        pw.WriteU8(tickMs);
        pw.WriteU16(clockSpeed);
        pw.WriteString(serverVersion);
        pw.WriteU16(clientVersion);

        return pw.ToArray();
    }

    public static byte[] BuildLoginSuccess(PlayerInfoDto p, bool featuresEnabled)
    {
        PacketWriter pw = new PacketWriter(2048);
        pw.WriteU8((byte)ServerOpcode.LOGIN_SUCCESS);

        pw.WriteU32(p.Id);
        pw.WriteString(p.Name);
        WritePosition(pw, p.X, p.Y, p.Z);
        pw.WriteU8(p.Direction);

        pw.WriteU32(p.Experience);
        pw.WriteU8(p.Level);
        pw.WriteU16(p.Speed);
        pw.WriteU8(p.Attack);
        pw.WriteU8(p.AttackSlowness);

        // 10 slots
        for (int i = 0; i < 10; i++)
        {
            if (p.Equipment[i] == null) WriteNullItem(pw);
            else WriteItem(pw, p.Equipment[i]!.ClientItemId, p.Equipment[i]!.Count);
        }

        pw.WriteU32(p.Capacity);

        WriteOutfitList(pw, p.Mounts);
        WriteOutfitList(pw, p.Outfits);

        WriteOutfit(pw, p.Outfit, featuresEnabled);

        pw.WriteU8(p.Health);
        pw.WriteU8(p.MaxHealth);
        pw.WriteU16(p.Mana);
        pw.WriteU16(p.MaxMana);

        WriteConditions(pw, p.Conditions);

        return pw.ToArray();
    }

    public static byte[] BuildWorldTime(uint time)
    {
        PacketWriter pw = new PacketWriter(16);
        pw.WriteU8((byte)ServerOpcode.WORLD_TIME);
        pw.WriteU32(time);
        return pw.ToArray();
    }

    public static byte[] BuildWriteSpells(IReadOnlyList<byte> spellIds)
    {
        PacketWriter pw = new PacketWriter(256);
        pw.WriteU8((byte)ServerOpcode.WRITE_SPELLS);

        pw.WriteU8((byte)spellIds.Count);
        for (int i = 0; i < spellIds.Count; i++)
            pw.WriteU8(spellIds[i]);

        return pw.ToArray();
    }

    public static byte[] BuildPlayerStatistics(uint capacity, byte attack, byte armor, ushort speed)
    {
        PacketWriter pw = new PacketWriter(32);
        pw.WriteU8((byte)ServerOpcode.PLAYER_STATISTICS);
        pw.WriteU32(capacity);
        pw.WriteU8(attack);
        pw.WriteU8(armor);
        pw.WriteU16(speed);
        return pw.ToArray();
    }

    // tiles = chunkW*chunkH*chunkD entries
    public static byte[] BuildWriteChunk(uint chunkId, ushort sectorX, ushort sectorY, ushort sectorZ, TileDto[] tiles)
    {
        PacketWriter pw = new PacketWriter(4096);
        pw.WriteU8((byte)ServerOpcode.WRITE_CHUNK);

        pw.WriteU32(chunkId);
        WritePosition(pw, sectorX, sectorY, sectorZ);

        for (int i = 0; i < tiles.Length; i++)
        {
            pw.WriteU16(tiles[i].TileId);
            pw.WriteU8(tiles[i].Flags);
            pw.WriteU8(tiles[i].Zone);
        }

        return pw.ToArray();
    }
}

// -------- DTOs ----------
public sealed class PlayerInfoDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";

    public ushort X { get; set; }
    public ushort Y { get; set; }
    public ushort Z { get; set; }

    public byte Direction { get; set; }

    public uint Experience { get; set; }
    public byte Level { get; set; }
    public ushort Speed { get; set; }
    public byte Attack { get; set; }
    public byte AttackSlowness { get; set; }

    public ItemDto?[] Equipment { get; set; } = new ItemDto?[10];

    public uint Capacity { get; set; }

    public List<OutfitListEntryDto> Mounts { get; set; } = new();
    public List<OutfitListEntryDto> Outfits { get; set; } = new();
    public OutfitDto Outfit { get; set; } = new();

    public byte Health { get; set; }
    public byte MaxHealth { get; set; }
    public ushort Mana { get; set; }
    public ushort MaxMana { get; set; }

    public List<byte> Conditions { get; set; } = new();
}

public sealed class ItemDto
{
    public ushort ClientItemId { get; set; }
    public byte Count { get; set; }
}

public sealed class OutfitDto
{
    public ushort Id { get; set; }
    public OutfitDetailsDto? Details { get; set; } = new OutfitDetailsDto();
    public ushort Mount { get; set; }
    public bool Mounted { get; set; }
    public bool AddonOne { get; set; }
    public bool AddonTwo { get; set; }
}

public sealed class OutfitDetailsDto
{
    public byte Head { get; set; }
    public byte Body { get; set; }
    public byte Legs { get; set; }
    public byte Feet { get; set; }
}

public sealed class OutfitListEntryDto
{
    public ushort Id { get; set; }
    public string Name { get; set; } = "";
}

public readonly struct TileDto
{
    public TileDto(ushort tileId, byte flags, byte zone)
    {
        TileId = tileId;
        Flags = flags;
        Zone = zone;
    }
    public ushort TileId { get; }
    public byte Flags { get; }
    public byte Zone { get; }
}