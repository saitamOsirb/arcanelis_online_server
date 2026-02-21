namespace OpenTibia.Server.Domain;

public sealed record CreateAccountRequest(string Account, string Password, string Name, int Sex);
public sealed record LoginRequest(string Account, string Password);
public sealed record CreateCharacterRequest(string Account, string Password, string Name, int Sex);

public sealed record CharacterDto(string Name);
public sealed record CharactersResponse(IReadOnlyList<CharacterDto> Characters);
public sealed record TokenResponse(string Token);
