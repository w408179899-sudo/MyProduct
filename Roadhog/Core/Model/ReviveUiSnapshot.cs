namespace Roadhog.Core.Model;

/// <summary>Ordinary death-recovery dialog; a null button means it cannot currently be clicked.</summary>
public sealed record ReviveUiSnapshot(bool IsOpen, ulong InstanceId, GameUiPoint? ConfirmButton)
{
    public static ReviveUiSnapshot Closed { get; } = new(false, 0, null);
}
