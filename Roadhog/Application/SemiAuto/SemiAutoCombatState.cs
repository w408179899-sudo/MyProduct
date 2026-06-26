namespace Roadhog.Application.SemiAuto;

public sealed class SemiAutoCombatState
{
    private readonly Dictionary<string, DateTimeOffset> _lastPressByNode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _suppressedUntilByNode = new(StringComparer.Ordinal);

    public SemiAutoSkillNode? ActiveChainNode { get; private set; }

    public DateTimeOffset ChainExpiresAt { get; private set; }

    public DateTimeOffset LastTargetWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastPlanWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastSkillWarningAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastTargetStateLogAt { get; set; } = DateTimeOffset.MinValue;

    public DateTimeOffset LastNoSkillLogAt { get; set; } = DateTimeOffset.MinValue;

    public void StartChain(SemiAutoSkillNode node, DateTimeOffset expiresAt)
    {
        ActiveChainNode = node;
        ChainExpiresAt = expiresAt;
    }

    public void ClearChain()
    {
        ActiveChainNode = null;
        ChainExpiresAt = DateTimeOffset.MinValue;
    }

    public bool IsChainExpired(DateTimeOffset now)
    {
        return ActiveChainNode is not null && now >= ChainExpiresAt;
    }

    public bool CanPress(SemiAutoSkillNode node, DateTimeOffset now, TimeSpan repeatGuard)
    {
        return !IsSuppressed(node, now) &&
               (!_lastPressByNode.TryGetValue(node.NodeKey, out var lastPress) ||
                now - lastPress >= repeatGuard);
    }

    public void MarkPressed(SemiAutoSkillNode node, DateTimeOffset now)
    {
        _lastPressByNode[node.NodeKey] = now;
    }

    public bool IsSuppressed(SemiAutoSkillNode node, DateTimeOffset now)
    {
        return _suppressedUntilByNode.TryGetValue(node.NodeKey, out var until) && now < until;
    }

    public void Suppress(SemiAutoSkillNode node, DateTimeOffset until)
    {
        _suppressedUntilByNode[node.NodeKey] = until;
    }
}
