namespace OpenTibia.Server.Game.Core;

public sealed class GameOptions
{
    public ushort WorldWidth { get; set; } = 2048;
    public ushort WorldHeight { get; set; } = 2048;
    public byte WorldDepth { get; set; } = 16;

    public byte ChunkW { get; set; } = 9;
    public byte ChunkH { get; set; } = 7;
    public byte ChunkD { get; set; } = 8;

    public byte TickMs { get; set; } = 50;
    public ushort ClockSpeed { get; set; } = 6;

    // 10.98
    public string ServerVersion { get; set; } = "10.98";
    public ushort ClientVersion { get; set; } = 1098;

    // habilita mount/addons en outfit
    public bool FeaturesEnabled { get; set; } = true;

    // dataset de contenido (cuando carguemos data real)
    public string DataVersion { get; set; } = "1098";
}