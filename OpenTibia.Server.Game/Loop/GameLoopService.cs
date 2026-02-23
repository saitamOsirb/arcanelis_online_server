using Microsoft.Extensions.Hosting;
using OpenTibia.Server.Game.Core;

namespace OpenTibia.Server.Game.Loop;

public sealed class GameLoopService : BackgroundService
{
    private readonly GameServer _game;

    public GameLoopService(GameServer game)
    {
        _game = game;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        const int tickMs = 50;

        while (!stoppingToken.IsCancellationRequested)
        {
            var start = Environment.TickCount64;

            _game.Tick();

            var elapsed = (int)(Environment.TickCount64 - start);
            var delay = tickMs - elapsed;
            if (delay > 0)
                await Task.Delay(delay, stoppingToken);
        }
    }
}