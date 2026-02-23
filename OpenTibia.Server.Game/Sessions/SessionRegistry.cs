using System.Collections.Concurrent;

namespace OpenTibia.Server.Game.Sessions;

public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<string, GameSession> _byName = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(GameSession session) => _byName.TryAdd(session.CharacterName, session);

    public bool TryRemove(string characterName) => _byName.TryRemove(characterName, out _);

    public bool IsOnline(string characterName) => _byName.ContainsKey(characterName);

    public IEnumerable<GameSession> Sessions => _byName.Values;
}