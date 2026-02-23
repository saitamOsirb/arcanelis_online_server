using System.Net.WebSockets;
using OpenTibia.Server.Game.Core;
using OpenTibia.Server.Game.Networking;
using OpenTibia.Server.Game.Protocol;

namespace OpenTibia.Server.Game.Sessions;

public sealed class GameSession
{
    private readonly WebSocket _ws;
    private readonly SessionRegistry _registry;
    private readonly GameOptions _options;

    private bool _sentServerData;
    private bool _loggedIn;

    public string CharacterName { get; }

    public GameSession(string characterName, WebSocket ws, SessionRegistry registry, GameOptions options)
    {
        CharacterName = characterName;
        _ws = ws;
        _registry = registry;
        _options = options;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await SendServerDataAsync(ct);
            await ReceiveAndProcessLoop(ct);
        }
        finally
        {
            _registry.TryRemove(CharacterName);

            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
            }
            catch { }
        }
    }

    private async Task SendServerDataAsync(CancellationToken ct)
    {
        if (_sentServerData) return;
        _sentServerData = true;

        byte[] packet = ServerProtocolWriter.BuildServerData(
            worldWidth: _options.WorldWidth,
            worldHeight: _options.WorldHeight,
            worldDepth: _options.WorldDepth,
            chunkW: _options.ChunkW,
            chunkH: _options.ChunkH,
            chunkD: _options.ChunkD,
            tickMs: _options.TickMs,
            clockSpeed: _options.ClockSpeed,
            serverVersion: _options.ServerVersion,
            clientVersion: _options.ClientVersion
        );

        await SendAsync(new ArraySegment<byte>(packet), ct);
    }

    private async Task ReceiveAndProcessLoop(CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        using MemoryStream ms = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            ms.SetLength(0);

            WebSocketReceiveResult res;
            do
            {
                res = await _ws.ReceiveAsync(buffer, ct);

                if (res.MessageType == WebSocketMessageType.Close)
                    return;

                if (res.MessageType != WebSocketMessageType.Binary)
                    break;

                ms.Write(buffer, 0, res.Count);
            }
            while (!res.EndOfMessage);

            if (ms.Length < 1)
                continue;

            byte[] msg = ms.ToArray();
            ClientOpcode opcode = (ClientOpcode)msg[0];

            try
            {
                HandlePacket(opcode, msg, 1, msg.Length - 1, ct);
            }
            catch
            {
                // TODO: log error + opcode
            }
        }
    }

    private void HandlePacket(ClientOpcode opcode, byte[] buffer, int offset, int length, CancellationToken ct)
    {
        // CLIENT -> SERVER reader (strings U8)
        ClientPacketReader pr = new ClientPacketReader(buffer, offset, length);

        switch (opcode)
        {
            case ClientOpcode.REQUEST_LOGIN:
                {
                    string account = pr.ReadStringU8();
                    string password = pr.ReadStringU8();

                    // En este port: el WS token ya autenticó.
                    // Si quieres doble validación aquí, conectamos Infrastructure luego.
                    _loggedIn = true;

                    // En C# 12 evitamos await dentro de este método sincrónico:
                    SendFullLoginSequence(ct).GetAwaiter().GetResult();
                    break;
                }

            // TODO: implementar movimiento, chat, etc.
            default:
                break;
        }
    }

    private async Task SendFullLoginSequence(CancellationToken ct)
    {
        bool featuresEnabled = _options.FeaturesEnabled;

        // TODO: reemplazar por carga real de DB + dataset 1098
        PlayerInfoDto p = new PlayerInfoDto
        {
            Id = 1000,
            Name = CharacterName,
            X = 100,
            Y = 100,
            Z = 7,
            Direction = 1,
            Experience = 0,
            Level = 1,
            Speed = 220,
            Attack = 1,
            AttackSlowness = 0,
            Capacity = 400,
            Health = 150,
            MaxHealth = 150,
            Mana = 0,
            MaxMana = 0,
            Outfit = new OutfitDto
            {
                Id = 128,
                Details = new OutfitDetailsDto { Head = 0, Body = 0, Legs = 0, Feet = 0 },
                Mount = 0,
                Mounted = false,
                AddonOne = false,
                AddonTwo = false
            },
            Conditions = new List<byte>()
        };

        // equipment 10 slots vacíos
        p.Equipment = new ItemDto?[10];

        // outfits list (placeholder)
        p.Outfits.Add(new OutfitListEntryDto { Id = 128, Name = "Citizen" });
        p.Mounts = new List<OutfitListEntryDto>();

        // 1) LOGIN_SUCCESS
        byte[] login = ServerProtocolWriter.BuildLoginSuccess(p, featuresEnabled);
        await SendAsync(new ArraySegment<byte>(login), ct);

        // 2) WORLD_TIME
        byte[] time = ServerProtocolWriter.BuildWorldTime((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await SendAsync(new ArraySegment<byte>(time), ct);

        // 3) WRITE_SPELLS
        byte[] spells = ServerProtocolWriter.BuildWriteSpells(Array.Empty<byte>());
        await SendAsync(new ArraySegment<byte>(spells), ct);

        // 4) PLAYER_STATISTICS
        byte[] stats = ServerProtocolWriter.BuildPlayerStatistics(
            capacity: p.Capacity,
            attack: p.Attack,
            armor: 0,
            speed: p.Speed
        );
        await SendAsync(new ArraySegment<byte>(stats), ct);

        // 5) WRITE_CHUNK (chunk actual)
        int chunkW = _options.ChunkW;
        int chunkH = _options.ChunkH;
        int chunkD = _options.ChunkD;

        TileDto[] tiles = new TileDto[chunkW * chunkH * chunkD];
        for (int i = 0; i < tiles.Length; i++)
            tiles[i] = new TileDto(tileId: 0, flags: 0, zone: 0);

        // Sector coords derivadas de posición
        int zMod = p.Z % chunkD;
        int px = p.X - zMod;
        int py = p.Y - zMod;

        ushort sx = (ushort)(px / chunkW);
        ushort sy = (ushort)(py / chunkH);
        ushort sz = (ushort)(p.Z < 8 ? 0 : 1);

        int nSectorsWidth = _options.WorldWidth / chunkW;
        int nSectorsHeight = _options.WorldHeight / chunkH;
        uint chunkId = (uint)(sx + (sy * nSectorsWidth) + (sz * nSectorsWidth * nSectorsHeight));

        byte[] chunk = ServerProtocolWriter.BuildWriteChunk(chunkId, sx, sy, sz, tiles);
        await SendAsync(new ArraySegment<byte>(chunk), ct);
    }

    public Task SendAsync(ArraySegment<byte> bytes, CancellationToken ct) =>
        _ws.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken: ct);
}