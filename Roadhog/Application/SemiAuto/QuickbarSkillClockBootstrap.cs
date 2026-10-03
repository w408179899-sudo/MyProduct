using System.Globalization;
using Roadhog.Core.Model;

namespace Roadhog.Application.SemiAuto;

/// <summary>
/// Finite startup proposals for an uncalibrated cooldown clock. Unknown cooldown
/// readiness permits a bounded trial, never an assertion that an icon is lit.
/// The owner alone sends keys and observes real releases and calibration.
/// </summary>
public sealed class QuickbarSkillClockBootstrap
{
    public static readonly TimeSpan MaximumTotalDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan MaximumCandidateDuration = TimeSpan.FromMilliseconds(1200);
    public const int MaximumCandidates = 3;

    private static readonly HashSet<string> DamageEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkillATK_Instant", "SkillATK", "SkillATKDrain_Instant",
        "SpellATK_Instant", "SpellATK", "SpellATKDrain_Instant", "SpellATKDrain",
        "DelayedSpellATK_Instant", "DelayedSkillATK_Instant", "NoReduceSpellATK_Instant",
        "FPATK_Instant", "FPATK", "DelayedFPATK_Instant", "ProcATK_Instant",
        "MPAttack_Instant", "MPAttack", "DashATK", "BackDashATK", "MoveBehindATK", "DeathBlow"
    };

    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _attemptedNodeKeys = new(StringComparer.Ordinal);
    private long? _totalStartedTimestamp;
    private long? _candidateStartedTimestamp;
    private QuickbarSkillNode? _proposal;
    private bool _proposalNeedsTrial;

    public QuickbarSkillClockBootstrap(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public bool IsCompleted { get; private set; }
    public string? CompletionReason { get; private set; }
    public QuickbarSkillNode? CurrentCandidate { get; private set; }
    public int AttemptedCandidateCount => _attemptedNodeKeys.Count;

    /// <summary>Selection does not start either time budget or consume an attempt.</summary>
    public QuickbarSkillNode? SelectCandidate(
        QuickbarSkillPlan plan,
        SkillAvailabilitySnapshot availability,
        IReadOnlyList<SkillSnapshot> skills,
        Func<SkillSnapshot, SemiAutoSkillCooldownReadiness?> cooldownReadiness,
        bool isClockCalibrated)
    {
        _proposal = null;
        _proposalNeedsTrial = false;
        ObserveCalibration(isClockCalibrated);
        ExpireBudgets();
        if (IsCompleted || availability.Page != plan.Page) return null;

        var candidates = new List<(QuickbarSkillNode Node, SemiAutoSkillCooldownReadiness? Readiness)>();
        foreach (var root in plan.Roots)
        {
            // Eligibility is exact-rank and exact-slot: a learned lower/higher
            // rank or an inferred chain slot cannot authorize a startup trial.
            if (root.NodeKey.Contains('/') || !HasOrdinaryBinding(root, availability)) continue;
            var skill = skills.FirstOrDefault(value => value.SkillId == root.SkillId);
            if (skill is null || !IsEligibleOrdinaryAttack(skill)) continue;
            var readiness = cooldownReadiness(skill);
            if (readiness is SemiAutoSkillCooldownReadiness.Ready or SemiAutoSkillCooldownReadiness.Unknown)
                candidates.Add((root, readiness));
        }

        // A genuinely ready ordinary attack needs no exceptional trial budget.
        foreach (var candidate in candidates)
            if (candidate.Readiness == SemiAutoSkillCooldownReadiness.Ready)
                return Propose(candidate.Node, needsTrial: false);

        if (CurrentCandidate is { } current)
        {
            var stillEligible = candidates.FirstOrDefault(candidate => candidate.Node.NodeKey == current.NodeKey);
            if (stillEligible.Node is not null)
                return Propose(stillEligible.Node, needsTrial: true);
            FinishCandidate(current, "candidate_unavailable");
            if (IsCompleted) return null;
        }

        foreach (var candidate in candidates)
            if (!_attemptedNodeKeys.Contains(candidate.Node.NodeKey))
                return Propose(candidate.Node, needsTrial: true);

        // An initially empty official skill snapshot cannot spend a budget that
        // has never started. After a real trial, exhausted candidates are final.
        if (_attemptedNodeKeys.Count > 0) Complete("candidates_exhausted");
        return null;
    }

    /// <summary>Call immediately before actually sending an unknown-CD trial key.</summary>
    public bool MarkAttemptStarted(QuickbarSkillNode node)
    {
        ExpireBudgets();
        if (IsCompleted || _proposal is not { } proposal || proposal.NodeKey != node.NodeKey ||
            proposal.SkillId != node.SkillId) return false;
        if (CurrentCandidate is { } current) return current.NodeKey == node.NodeKey;
        if (!_proposalNeedsTrial || _attemptedNodeKeys.Contains(node.NodeKey) ||
            _attemptedNodeKeys.Count >= MaximumCandidates) return false;

        var now = _timeProvider.GetTimestamp();
        _totalStartedTimestamp ??= now;
        _candidateStartedTimestamp = now;
        _attemptedNodeKeys.Add(node.NodeKey);
        CurrentCandidate = node;
        return true;
    }

    /// <summary>Checks only an actual trial; UTC changes cannot extend its deadline.</summary>
    public bool IsAttemptExpired(QuickbarSkillNode node)
    {
        if (!_attemptedNodeKeys.Contains(node.NodeKey)) return false;
        return IsCompleted || CurrentCandidate?.NodeKey != node.NodeKey ||
            HasElapsed(_candidateStartedTimestamp, MaximumCandidateDuration) ||
            HasElapsed(_totalStartedTimestamp, MaximumTotalDuration);
    }

    public void FinishCandidate(QuickbarSkillNode node, string reason)
    {
        if (CurrentCandidate?.NodeKey != node.NodeKey) return;
        CurrentCandidate = null;
        _candidateStartedTimestamp = null;
        if (_proposal?.NodeKey == node.NodeKey)
        {
            _proposal = null;
            _proposalNeedsTrial = false;
        }
        if (HasElapsed(_totalStartedTimestamp, MaximumTotalDuration)) Complete("total_timeout");
        else if (_attemptedNodeKeys.Count >= MaximumCandidates) Complete("attempts_exhausted");
    }

    public void ObserveCalibration(bool isClockCalibrated)
    {
        if (isClockCalibrated) Complete("calibrated");
    }

    /// <summary>A new combat/binding scope alone grants a new startup budget.</summary>
    public void Reset()
    {
        IsCompleted = false;
        CompletionReason = null;
        CurrentCandidate = null;
        _proposal = null;
        _proposalNeedsTrial = false;
        _totalStartedTimestamp = null;
        _candidateStartedTimestamp = null;
        _attemptedNodeKeys.Clear();
    }

    public static bool IsEligibleOrdinaryAttack(SkillSnapshot skill)
    {
        if (skill.HighestLevel <= 0 || skill.CooldownDuration == 0 || skill.IsToggle ||
            !EqualsToken(skill.XmlActivation, "Active") ||
            !(EqualsToken(skill.XmlSkillType, "Physical") || EqualsToken(skill.XmlSkillType, "Magical")) ||
            !EqualsToken(skill.XmlTargetRelationRestriction, "Enemy") ||
            !QuickbarSkillCombatController.IsOrdinarySkill(skill)) return false;
        if (!string.IsNullOrWhiteSpace(skill.XmlUltraTransfer) && !EqualsToken(skill.XmlUltraTransfer, "0"))
            return false;

        // Non-empty malformed DP requirements are not an invitation to spend DP.
        if (!string.IsNullOrWhiteSpace(skill.XmlCostDp) &&
            (!uint.TryParse(skill.XmlCostDp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dp) || dp != 0))
            return false;

        var tags = Tokens(skill.XmlTags);
        if (tags.Any(tag => EqualsToken(tag, "dp") || EqualsToken(tag, "passive") ||
            EqualsToken(tag, "toggle") || tag.Contains("Chant", StringComparison.OrdinalIgnoreCase))) return false;

        if (new[] { skill.XmlSubType, skill.XmlSkillCategory, skill.Name, skill.DisplayBaseName }
            .Any(value => value?.Contains("Chant", StringComparison.OrdinalIgnoreCase) == true ||
                value?.Contains("\u771F\u8A00", StringComparison.Ordinal) == true)) return false;
        if (EqualsToken(skill.XmlSubType, "Buff") || EqualsToken(skill.XmlSubType, "Heal") ||
            EqualsToken(skill.XmlSubType, "Healing") || EqualsToken(skill.XmlSubType, "Summon")) return false;

        return EqualsToken(skill.XmlSubType, "Attack") || Tokens(skill.XmlEffects).Any(DamageEffects.Contains);
    }

    private static bool HasOrdinaryBinding(QuickbarSkillNode node, SkillAvailabilitySnapshot availability) =>
        availability.UnsupportedSkillIds?.Contains(node.SkillId) == true &&
        availability.BindingSlots?.Any(slot => slot.Bar == node.Bar && slot.Slot == node.Slot &&
            slot.ContentType == 21 && slot.BaseSkillId == node.BaseSkillId && slot.EffectiveSkillId == node.SkillId) == true;

    private QuickbarSkillNode Propose(QuickbarSkillNode node, bool needsTrial)
    {
        _proposal = node;
        _proposalNeedsTrial = needsTrial;
        return node;
    }

    private void ExpireBudgets()
    {
        if (IsCompleted) return;
        if (HasElapsed(_totalStartedTimestamp, MaximumTotalDuration)) Complete("total_timeout");
        else if (CurrentCandidate is { } current && HasElapsed(_candidateStartedTimestamp, MaximumCandidateDuration))
            FinishCandidate(current, "candidate_timeout");
    }

    private bool HasElapsed(long? started, TimeSpan duration) =>
        started is { } timestamp && _timeProvider.GetElapsedTime(timestamp) >= duration;

    private void Complete(string reason)
    {
        if (IsCompleted) return;
        IsCompleted = true;
        CompletionReason = reason;
        CurrentCandidate = null;
        _candidateStartedTimestamp = null;
        _proposal = null;
        _proposalNeedsTrial = false;
    }

    private static bool EqualsToken(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Tokens(string? value) =>
        (value ?? string.Empty).Split(new[] { ',', ';', ' ', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
}
