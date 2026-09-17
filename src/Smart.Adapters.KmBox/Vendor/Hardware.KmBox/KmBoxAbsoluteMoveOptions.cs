namespace Hardware.KmBox;

public sealed class KmBoxAbsoluteMoveOptions
{
    public int OriginX { get; set; } = 1;

    public int OriginY { get; set; } = 1;

    public int ResetDeltaX { get; set; } = short.MinValue;

    public int ResetDeltaY { get; set; } = short.MinValue;

    public int ResetCount { get; set; } = 3;

    public int StepDelayMs { get; set; }

    public int TargetMoveDurationMs { get; set; }
}
