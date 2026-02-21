# OpenTibia.Server (.NET 8)

Backend base para migrar la capa **server** de tu OT HTML5:

- API HTTP: cuentas, personajes, token de personaje
- WebSocket: endpoint `/ws` protegido por token JWT (HMAC SHA-256)
- PostgreSQL: tablas `accounts` y `players` (JSONB)

## Requisitos
- .NET 8 SDK
- PostgreSQL

## Config
Edita `OpenTibia.Server.Api/appsettings.json`:
- `ConnectionStrings:Postgres`
- `Auth:SigningKey` (secreto largo, >= 32 chars)

## Ejecutar
En la raíz:

```bash
dotnet restore
dotnet run --project OpenTibia.Server.Api
```

## Endpoints
- `GET /health`
- `POST /account`
- `POST /characters`
- `POST /characters/create`
- `POST /login-character` -> devuelve `{ token }`
- `GET ws://HOST/ws?token=...`

> Nota: El WebSocket hace **echo** binario como MVP. El siguiente paso es implementar el protocolo real (PacketReader/Writer + dispatcher).
