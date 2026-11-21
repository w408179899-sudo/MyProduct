namespace SkillScheduling;

/// <summary>A rule evaluated against the current game state. Lower priority values run first.</summary>
public readonly record struct SkillCandidate<TId>(
    TId Id, int Priority, bool IsEligible, Func<CancellationToken, Task<bool>> TryExecute, bool ContinuesAttempt = false)
    where TId : notnull;

public static class SkillScheduler
{
    /// <summary>
    /// Tries eligible candidates in priority order. A continuation wins ties with fresh
    /// actions, and equal candidates retain registration order.
    /// A final guard may decline a candidate. A handler that owns this turn returns true,
    /// even when it is waiting for confirmation and sends no input.
    /// </summary>
    public static async Task<SkillCandidate<TId>?> RunAsync<TId>(
        IReadOnlyList<SkillCandidate<TId>> candidates, CancellationToken cancellationToken = default)
        where TId : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Priority)
                     .ThenByDescending(candidate => candidate.ContinuesAttempt))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.IsEligible) continue;
            var ownsTurn = await candidate.TryExecute(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (ownsTurn) return candidate;
        }
        return null;
    }
}
