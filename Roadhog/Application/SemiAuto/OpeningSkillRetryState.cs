using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>One deferred extra attempt per timed-out opener, owned by the current target.</summary>
internal sealed class OpeningSkillRetryState
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    private readonly Queue<Attempt> attempts = new();
    public Attempt? Current => attempts.TryPeek(out var attempt) ? attempt : null;

    public Attempt Defer(int index, SemiAutoSkillNode node, SkillSnapshot baseline, TimeProvider clock)
    {
        var attempt = new Attempt(index, node, baseline, clock.GetTimestamp());
        attempts.Enqueue(attempt);
        return attempt;
    }

    public bool IsExpired(TimeProvider clock) => Current is { } attempt &&
        clock.GetElapsedTime(attempt.DeferredAt) >= Lifetime;

    public bool IsReady(TimeProvider clock) => Current is { } attempt &&
        clock.GetElapsedTime(attempt.DeferredAt) >= RetryDelay;

    public void Start(TimeProvider clock)
    {
        if (Current is { } attempt) attempt.StartedAt = clock.GetTimestamp();
    }

    public void Finish() => attempts.Dequeue();
    public void Reset() => attempts.Clear();

    internal sealed record Attempt(int Index, SemiAutoSkillNode Node, SkillSnapshot Baseline, long DeferredAt)
    {
        public long? StartedAt { get; set; }
    }
}
