using System.Net.WebSockets;
using OpenTibia.Server.Game.Sessions;

namespace OpenTibia.Server.Game.Core;

public sealed class GameServer
{
    private readonly SessionRegistry _registry;
    private readonly GameOptions _options;

    public GameServer(SessionRegistry registry, GameOptions options)
    {
        _registry = registry;
        _options = options;
    }

    public GameSession CreateSession(string characterName, WebSocket ws)
    {
        if (_registry.IsOnline(characterName))
            throw new InvalidOperationException("Character already online.");

        GameSession session = new GameSession(characterName, ws, _registry, _options);

        if (!_registry.TryAdd(session))
            throw new InvalidOperationException("Character already online.");

        return session;
    }

    public void Tick()
    {
        // TODO: world tick (luego)
    }
}