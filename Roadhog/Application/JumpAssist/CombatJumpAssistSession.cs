using Roadhog.Application.Workers;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.JumpAssist;

public sealed class CombatJumpAssistSession : IAsyncDisposable
{
    private static readonly TimeSpan DefaultJumpInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultCooldownPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DefaultKeyHoldDuration = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan TeamBaselineRetryInterval = TimeSpan.FromSeconds(1);

    private readonly object _syncRoot = new();
    private readonly AccountWorkerContext _context;
    private readonly IKeyboardInput _keyboard;
    private readonly bool _teamFollower;
    private readonly TimeSpan _jumpInterval;
    private readonly TimeSpan _cooldownPollInterval;
    private readonly TimeSpan _keyHoldDuration;
    private readonly HashSet<string> _pauseReasons = new(StringComparer.Ordinal);

    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private long _generation;
    private JumpAssistMode _mode;
    private ushort _soloTargetEntityId;
    private uint _soloTargetServerObjectId;
    private uint _soloTargetBaselineHp;
    private bool _teamGroupActive;
    private bool _teamCooldownConfirmed;
    private bool _teamCombatTargetObserved;
    private uint _teamJumpTargetServerObjectId;
    private bool _teamJumpTargetActivated;
    private uint _pendingTeamJumpTargetServerObjectId;
    private SemaphoreSlim? _sessionWakeSignal;
    private DateTimeOffset _lastTeamBaselineFailureAt = DateTimeOffset.MinValue;

    public CombatJumpAssistSession(
        AccountWorkerContext context,
        IKeyboardInput keyboard,
        bool teamFollower,
        TimeSpan? jumpInterval = null,
        TimeSpan? cooldownPollInterval = null,
        TimeSpan? keyHoldDuration = null)
    {
        _context = context;
        _keyboard = keyboard;
        _teamFollower = teamFollower;
        _jumpInterval = NormalizeInterval(jumpInterval, DefaultJumpInterval);
        _cooldownPollInterval = NormalizeInterval(cooldownPollInterval, DefaultCooldownPollInterval);
        _keyHoldDuration = NormalizeInterval(keyHoldDuration, DefaultKeyHoldDuration);
    }

    public JumpAssistMode Mode
    {
        get
        {
            lock (_syncRoot)
            {
                return _mode;
            }
        }
    }

    public bool TeamCooldownConfirmed
    {
        get
        {
            lock (_syncRoot)
            {
                return _teamCooldownConfirmed;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_syncRoot)
            {
                return _pauseReasons.Count > 0;
            }
        }
    }

    public async Task StartSoloTargetAsync(
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        uint currentHp)
    {
        if (_teamFollower ||
            targetEntityId == 0 ||
            _context.StopToken.IsCancellationRequested)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_mode == JumpAssistMode.SoloTarget &&
                IsSameTarget(
                    _soloTargetEntityId,
                    _soloTargetServerObjectId,
                    targetEntityId,
                    targetServerObjectId))
            {
                return;
            }
        }

        await StopCurrentSessionAsync("solo_target_changed").ConfigureAwait(false);
        StartSession(
            JumpAssistMode.SoloTarget,
            targetEntityId,
            targetServerObjectId,
            targetName,
            currentHp,
            null);
    }

    public async Task ObserveSoloTargetHealthAsync(
        ushort targetEntityId,
        uint targetServerObjectId,
        uint currentHp)
    {
        var damageObserved = false;
        lock (_syncRoot)
        {
            if (_mode != JumpAssistMode.SoloTarget ||
                !IsSameTarget(
                    _soloTargetEntityId,
                    _soloTargetServerObjectId,
                    targetEntityId,
                    targetServerObjectId))
            {
                return;
            }

            if (_soloTargetBaselineHp == 0 && currentHp > 0)
            {
                _soloTargetBaselineHp = currentHp;
                return;
            }

            damageObserved = _soloTargetBaselineHp > 0 && currentHp < _soloTargetBaselineHp;
        }

        if (damageObserved)
        {
            await StopCurrentSessionAsync("solo_target_damage_observed").ConfigureAwait(false);
        }
    }

    public async Task StopSoloTargetAsync(string reason)
    {
        lock (_syncRoot)
        {
            if (_mode != JumpAssistMode.SoloTarget)
            {
                return;
            }
        }

        await StopCurrentSessionAsync(reason).ConfigureAwait(false);
    }

    public void ObserveTeamCombatState(bool localCombatTargetActive, uint leaderTargetServerObjectId)
    {
        if (!_teamFollower)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_teamGroupActive &&
                (localCombatTargetActive || leaderTargetServerObjectId != 0))
            {
                _teamCombatTargetObserved = true;
            }
        }
    }

    public async Task PrepareTeamCombatJumpAsync(uint targetServerObjectId)
    {
        if (!_teamFollower || targetServerObjectId == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_teamGroupActive ||
                _teamJumpTargetServerObjectId == targetServerObjectId)
            {
                return;
            }

            _teamJumpTargetServerObjectId = targetServerObjectId;
            _teamJumpTargetActivated = false;
            _pendingTeamJumpTargetServerObjectId = 0;
            _teamCooldownConfirmed = false;
            _teamCombatTargetObserved = true;
        }

        await StopCurrentSessionAsync("team_target_changed").ConfigureAwait(false);
        lock (_syncRoot)
        {
            if (!_teamGroupActive ||
                _teamJumpTargetServerObjectId != targetServerObjectId)
            {
                return;
            }

            _teamCooldownConfirmed = false;
        }

        var baseline = await ReadTeamCooldownBaselineAsync(targetServerObjectId).ConfigureAwait(false);
        if (baseline is null)
        {
            lock (_syncRoot)
            {
                if (_teamJumpTargetServerObjectId == targetServerObjectId)
                {
                    _teamJumpTargetServerObjectId = 0;
                }
            }

            return;
        }

        lock (_syncRoot)
        {
            if (!_teamGroupActive ||
                _teamCooldownConfirmed ||
                _teamJumpTargetServerObjectId != targetServerObjectId)
            {
                return;
            }
        }

        StartSession(
            JumpAssistMode.TeamGroup,
            0,
            0,
            string.Empty,
            0,
            baseline);

        var prepared = false;
        lock (_syncRoot)
        {
            if (_teamGroupActive &&
                !_teamCooldownConfirmed &&
                _mode == JumpAssistMode.TeamGroup &&
                _teamJumpTargetServerObjectId == targetServerObjectId)
            {
                prepared = true;
            }
        }

        if (!prepared)
        {
            _context.Logger.Warn("jump_assist.team_target_jump.prepare_failed", new Dictionary<string, object?>
            {
                ["account"] = _context.Config.AccountName,
                ["targetServerObjectId"] = targetServerObjectId,
                ["mode"] = Mode.ToString()
            });
            return;
        }

        _context.Logger.Info("jump_assist.team_target_jump.prepared", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["targetServerObjectId"] = targetServerObjectId,
            ["rearmed"] = true
        });
    }

    public void ActivatePreparedTeamCombatJump(uint targetServerObjectId)
    {
        if (!_teamFollower || targetServerObjectId == 0)
        {
            return;
        }

        var activated = false;
        lock (_syncRoot)
        {
            if (_teamGroupActive &&
                !_teamCooldownConfirmed &&
                _mode == JumpAssistMode.TeamGroup &&
                _teamJumpTargetServerObjectId == targetServerObjectId &&
                !_teamJumpTargetActivated)
            {
                _teamJumpTargetActivated = true;
                _pendingTeamJumpTargetServerObjectId = targetServerObjectId;
                TryWakeSessionLocked();
                activated = true;
            }
        }

        if (!activated)
        {
            return;
        }

        _context.Logger.Info("jump_assist.team_target_jump.requested", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["targetServerObjectId"] = targetServerObjectId,
            ["trigger"] = "combat_phase"
        });
    }

    public async Task EnterTeamGroupAsync()
    {
        if (!_teamFollower || _context.StopToken.IsCancellationRequested)
        {
            return;
        }

        lock (_syncRoot)
        {
            _teamGroupActive = true;
            if (_mode == JumpAssistMode.TeamGroup || _teamCooldownConfirmed)
            {
                return;
            }

            if (DateTimeOffset.Now - _lastTeamBaselineFailureAt < TeamBaselineRetryInterval)
            {
                return;
            }
        }

        await StopCurrentSessionAsync("team_group_entered").ConfigureAwait(false);
        var baseline = await ReadTeamCooldownBaselineAsync().ConfigureAwait(false);
        if (baseline is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_teamGroupActive || _teamCooldownConfirmed)
            {
                return;
            }
        }

        StartSession(
            JumpAssistMode.TeamGroup,
            0,
            0,
            string.Empty,
            0,
            baseline);
    }

    public async Task ExitTeamGroupAsync(string reason)
    {
        if (!_teamFollower)
        {
            return;
        }

        lock (_syncRoot)
        {
            _teamGroupActive = false;
            _teamCooldownConfirmed = false;
            _teamCombatTargetObserved = false;
            _teamJumpTargetServerObjectId = 0;
            _teamJumpTargetActivated = false;
            _pendingTeamJumpTargetServerObjectId = 0;
        }

        await StopCurrentSessionAsync(reason).ConfigureAwait(false);
    }

    public async Task TryRearmTeamGroupAsync(
        bool localCombatTargetActive,
        uint leaderTargetServerObjectId)
    {
        if (!_teamFollower)
        {
            return;
        }

        var shouldRearm = false;
        lock (_syncRoot)
        {
            if (_teamGroupActive &&
                _teamCombatTargetObserved &&
                !localCombatTargetActive &&
                leaderTargetServerObjectId == 0 &&
                (_teamCooldownConfirmed || _teamJumpTargetServerObjectId != 0))
            {
                _teamCooldownConfirmed = false;
                _teamCombatTargetObserved = false;
                _teamJumpTargetServerObjectId = 0;
                _teamJumpTargetActivated = false;
                _pendingTeamJumpTargetServerObjectId = 0;
                shouldRearm = true;
            }
        }

        if (shouldRearm)
        {
            await StopCurrentSessionAsync("team_combat_cleared").ConfigureAwait(false);
            await EnterTeamGroupAsync().ConfigureAwait(false);
        }
    }

    public void Pause(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            changed = _pauseReasons.Add(reason);
        }

        if (changed)
        {
            _context.Logger.Info("jump_assist.paused", new Dictionary<string, object?>
            {
                ["account"] = _context.Config.AccountName,
                ["reason"] = reason
            });
        }
    }

    public void Resume(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var changed = false;
        lock (_syncRoot)
        {
            changed = _pauseReasons.Remove(reason);
            if (changed && _pauseReasons.Count == 0)
            {
                TryWakeSessionLocked();
            }
        }

        if (changed)
        {
            _context.Logger.Info("jump_assist.resumed", new Dictionary<string, object?>
            {
                ["account"] = _context.Config.AccountName,
                ["reason"] = reason
            });
        }
    }

    public async Task WaitForTeamCooldownObservationAsync()
    {
        lock (_syncRoot)
        {
            if (_mode != JumpAssistMode.TeamGroup)
            {
                return;
            }
        }

        await Task.Delay(
                _cooldownPollInterval + TimeSpan.FromMilliseconds(25),
                _context.StopToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync(string reason)
    {
        lock (_syncRoot)
        {
            _teamGroupActive = false;
            _teamCooldownConfirmed = false;
            _teamCombatTargetObserved = false;
            _teamJumpTargetServerObjectId = 0;
            _teamJumpTargetActivated = false;
            _pendingTeamJumpTargetServerObjectId = 0;
            _pauseReasons.Clear();
        }

        await StopCurrentSessionAsync(reason).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync("disposed").ConfigureAwait(false);
    }

    private void StartSession(
        JumpAssistMode mode,
        ushort targetEntityId,
        uint targetServerObjectId,
        string targetName,
        uint targetBaselineHp,
        IReadOnlyDictionary<uint, uint>? cooldownBaseline)
    {
        CancellationTokenSource cancellation;
        SemaphoreSlim wakeSignal;
        long generation;

        lock (_syncRoot)
        {
            if (_sessionTask is not null || _context.StopToken.IsCancellationRequested)
            {
                return;
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_context.StopToken);
            wakeSignal = new SemaphoreSlim(0, 1);
            generation = ++_generation;
            _sessionCancellation = cancellation;
            _sessionWakeSignal = wakeSignal;
            _mode = mode;
            _soloTargetEntityId = targetEntityId;
            _soloTargetServerObjectId = targetServerObjectId;
            _soloTargetBaselineHp = targetBaselineHp;
            if (mode != JumpAssistMode.TeamGroup)
            {
                _teamJumpTargetServerObjectId = 0;
            }

            _pendingTeamJumpTargetServerObjectId = 0;
            _sessionTask = Task.Run(
                () => RunSessionAsync(
                    generation,
                    mode,
                    targetEntityId,
                    targetServerObjectId,
                    cooldownBaseline,
                    wakeSignal,
                    cancellation),
                CancellationToken.None);
        }

        _context.Logger.Info("jump_assist.started", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["mode"] = mode.ToString(),
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = targetServerObjectId,
            ["targetName"] = targetName
        });
    }

    private async Task RunSessionAsync(
        long generation,
        JumpAssistMode mode,
        ushort targetEntityId,
        uint targetServerObjectId,
        IReadOnlyDictionary<uint, uint>? cooldownBaseline,
        SemaphoreSlim wakeSignal,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var nextJumpAt = mode == JumpAssistMode.TeamGroup
            ? DateTimeOffset.Now + _jumpInterval
            : DateTimeOffset.MinValue;
        var nextCooldownPollAt = DateTimeOffset.MinValue;
        var teamCooldownSnapshotAvailable = mode != JumpAssistMode.TeamGroup;
        var lastCooldownReadWarningAt = DateTimeOffset.MinValue;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTimeOffset.Now;
                if (TryBeginJump(
                        generation,
                        mode,
                        periodicJumpDue: false,
                        out var activatedTeamTargetServerObjectId))
                {
                    await PressSpaceAsync(
                            mode,
                            targetEntityId,
                            targetServerObjectId,
                            activatedTeamTargetServerObjectId,
                            "team_target_activated",
                            token)
                        .ConfigureAwait(false);
                    nextJumpAt = DateTimeOffset.Now + _jumpInterval;
                }

                now = DateTimeOffset.Now;
                if (mode == JumpAssistMode.TeamGroup &&
                    cooldownBaseline is not null &&
                    now >= nextCooldownPollAt)
                {
                    nextCooldownPollAt = now + _cooldownPollInterval;
                    var skills = await ReadSkillsAsync().ConfigureAwait(false);
                    teamCooldownSnapshotAvailable = skills.Count > 0;
                    if (teamCooldownSnapshotAvailable &&
                        TryFindAdvancedCooldown(
                            cooldownBaseline,
                            skills,
                            out var advancedSkill))
                    {
                        CompleteTeamSessionFromCooldown(generation, advancedSkill);
                        return;
                    }

                    if (!teamCooldownSnapshotAvailable &&
                        now - lastCooldownReadWarningAt >= TimeSpan.FromSeconds(3))
                    {
                        lastCooldownReadWarningAt = now;
                        _context.Logger.Warn("jump_assist.team_cooldown_read.failed", new Dictionary<string, object?>
                        {
                            ["account"] = _context.Config.AccountName,
                            ["error"] = "skill_snapshot_empty"
                        });
                    }
                }

                now = DateTimeOffset.Now;
                var periodicJumpDue = now >= nextJumpAt;
                if (teamCooldownSnapshotAvailable &&
                    TryBeginJump(
                        generation,
                        mode,
                        periodicJumpDue,
                        out var requestedTeamTargetServerObjectId))
                {
                    await PressSpaceAsync(
                            mode,
                            targetEntityId,
                            targetServerObjectId,
                            requestedTeamTargetServerObjectId,
                            "periodic",
                            token)
                        .ConfigureAwait(false);
                    nextJumpAt = DateTimeOffset.Now + _jumpInterval;
                }

                var loopDelay = mode == JumpAssistMode.TeamGroup
                    ? _cooldownPollInterval
                    : Min(_jumpInterval, TimeSpan.FromMilliseconds(100));
                await wakeSignal.WaitAsync(loopDelay, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _context.Logger.Error("jump_assist.loop.failed", exception, new Dictionary<string, object?>
            {
                ["account"] = _context.Config.AccountName,
                ["mode"] = mode.ToString()
            });
        }
        finally
        {
            lock (_syncRoot)
            {
                if (_generation == generation)
                {
                    _sessionCancellation = null;
                    _sessionTask = null;
                    if (_mode == mode)
                    {
                        _mode = JumpAssistMode.None;
                    }

                    _soloTargetEntityId = 0;
                    _soloTargetServerObjectId = 0;
                    _soloTargetBaselineHp = 0;
                    _pendingTeamJumpTargetServerObjectId = 0;
                    _sessionWakeSignal = null;
                }
            }

            wakeSignal.Dispose();
            cancellation.Dispose();
        }
    }

    private void CompleteTeamSessionFromCooldown(long generation, SkillSnapshot skill)
    {
        lock (_syncRoot)
        {
            if (_generation != generation || _mode != JumpAssistMode.TeamGroup)
            {
                return;
            }

            _teamCooldownConfirmed = true;
            _mode = JumpAssistMode.None;
            _pendingTeamJumpTargetServerObjectId = 0;
        }

        _context.Logger.Info("jump_assist.team_cooldown_confirmed", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["skillId"] = skill.SkillId,
            ["skillName"] = skill.Name,
            ["cooldownEndTime"] = skill.CooldownEndTime
        });
    }

    private async Task StopCurrentSessionAsync(string reason)
    {
        CancellationTokenSource? cancellation;
        Task? task;
        JumpAssistMode mode;

        lock (_syncRoot)
        {
            cancellation = _sessionCancellation;
            task = _sessionTask;
            mode = _mode;
            if (cancellation is null || task is null)
            {
                _mode = JumpAssistMode.None;
                _soloTargetEntityId = 0;
                _soloTargetServerObjectId = 0;
                _soloTargetBaselineHp = 0;
                _pendingTeamJumpTargetServerObjectId = 0;
                _sessionWakeSignal = null;
                return;
            }

            _sessionCancellation = null;
            _sessionTask = null;
            _mode = JumpAssistMode.None;
            _soloTargetEntityId = 0;
            _soloTargetServerObjectId = 0;
            _soloTargetBaselineHp = 0;
            _pendingTeamJumpTargetServerObjectId = 0;
            _sessionWakeSignal = null;
            cancellation.Cancel();
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _context.Logger.Info("jump_assist.stopped", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["mode"] = mode.ToString(),
            ["reason"] = reason
        });
    }

    private bool TryBeginJump(
        long generation,
        JumpAssistMode mode,
        bool periodicJumpDue,
        out uint requestedTeamTargetServerObjectId)
    {
        lock (_syncRoot)
        {
            requestedTeamTargetServerObjectId = 0;
            if (_generation != generation ||
                _mode != mode ||
                _pauseReasons.Count > 0)
            {
                return false;
            }

            if (mode == JumpAssistMode.TeamGroup &&
                _pendingTeamJumpTargetServerObjectId != 0)
            {
                requestedTeamTargetServerObjectId = _pendingTeamJumpTargetServerObjectId;
                _pendingTeamJumpTargetServerObjectId = 0;
                return true;
            }

            if (mode == JumpAssistMode.TeamGroup &&
                _teamJumpTargetServerObjectId != 0 &&
                !_teamJumpTargetActivated)
            {
                return false;
            }

            return periodicJumpDue;
        }
    }

    private async Task PressSpaceAsync(
        JumpAssistMode mode,
        ushort targetEntityId,
        uint targetServerObjectId,
        uint requestedTeamTargetServerObjectId,
        string trigger,
        CancellationToken cancellationToken)
    {
        var pressResult = await _keyboard
            .PressKeyAsync("Space", _keyHoldDuration, cancellationToken)
            .ConfigureAwait(false);
        var effectiveTargetServerObjectId = ResolveJumpTargetServerObjectId(
            mode,
            targetServerObjectId,
            requestedTeamTargetServerObjectId);
        if (pressResult.Success)
        {
            _context.Logger.Info("jump_assist.space.pressed", new Dictionary<string, object?>
            {
                ["account"] = _context.Config.AccountName,
                ["mode"] = mode.ToString(),
                ["trigger"] = trigger,
                ["targetEntityId"] = targetEntityId,
                ["targetServerObjectId"] = effectiveTargetServerObjectId
            });
            if (requestedTeamTargetServerObjectId != 0)
            {
                _context.Logger.Info("jump_assist.team_target_jump.pressed", new Dictionary<string, object?>
                {
                    ["account"] = _context.Config.AccountName,
                    ["targetServerObjectId"] = requestedTeamTargetServerObjectId
                });
            }

            return;
        }

        _context.Logger.Warn("jump_assist.space.failed", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["mode"] = mode.ToString(),
            ["targetEntityId"] = targetEntityId,
            ["targetServerObjectId"] = effectiveTargetServerObjectId,
            ["trigger"] = trigger,
            ["error"] = pressResult.Error
        });
    }

    private uint ResolveJumpTargetServerObjectId(
        JumpAssistMode mode,
        uint targetServerObjectId,
        uint requestedTeamTargetServerObjectId)
    {
        if (requestedTeamTargetServerObjectId != 0)
        {
            return requestedTeamTargetServerObjectId;
        }

        if (mode != JumpAssistMode.TeamGroup)
        {
            return targetServerObjectId;
        }

        lock (_syncRoot)
        {
            return _teamJumpTargetServerObjectId;
        }
    }

    private void TryWakeSessionLocked()
    {
        if (_sessionWakeSignal is null || _sessionWakeSignal.CurrentCount != 0)
        {
            return;
        }

        _sessionWakeSignal.Release();
    }

    private async Task<IReadOnlyList<SkillSnapshot>> ReadSkillsAsync() =>
        (await _context.Snapshots.ReadSkillsAsync().ConfigureAwait(false)).Value;

    private async Task<IReadOnlyDictionary<uint, uint>?> ReadTeamCooldownBaselineAsync(
        uint targetServerObjectId = 0)
    {
        var skills = await ReadSkillsAsync().ConfigureAwait(false);
        if (skills.Count > 0)
        {
            return skills
                .GroupBy(skill => skill.SkillId)
                .ToDictionary(group => group.Key, group => group.First().CooldownEndTime);
        }

        lock (_syncRoot)
        {
            _lastTeamBaselineFailureAt = DateTimeOffset.Now;
        }

        _context.Logger.Warn("jump_assist.team_baseline.failed", new Dictionary<string, object?>
        {
            ["account"] = _context.Config.AccountName,
            ["targetServerObjectId"] = targetServerObjectId,
            ["error"] = "skill_snapshot_empty"
        });
        return null;
    }

    private static bool TryFindAdvancedCooldown(
        IReadOnlyDictionary<uint, uint> baseline,
        IReadOnlyList<SkillSnapshot> currentSkills,
        out SkillSnapshot advancedSkill)
    {
        foreach (var skill in currentSkills)
        {
            if (!baseline.TryGetValue(skill.SkillId, out var previousCooldownEndTime))
            {
                continue;
            }

            if (skill.CooldownEndTime != 0 &&
                unchecked((int)(skill.CooldownEndTime - previousCooldownEndTime)) > 0)
            {
                advancedSkill = skill;
                return true;
            }
        }

        advancedSkill = null!;
        return false;
    }

    private static bool IsSameTarget(
        ushort leftEntityId,
        uint leftServerObjectId,
        ushort rightEntityId,
        uint rightServerObjectId)
    {
        if (leftServerObjectId != 0 && rightServerObjectId != 0)
        {
            return leftServerObjectId == rightServerObjectId;
        }

        return leftEntityId != 0 && leftEntityId == rightEntityId;
    }

    private static TimeSpan NormalizeInterval(TimeSpan? value, TimeSpan fallback)
    {
        return value.HasValue && value.Value > TimeSpan.Zero
            ? value.Value
            : fallback;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        return left <= right ? left : right;
    }
}

public enum JumpAssistMode
{
    None,
    SoloTarget,
    TeamGroup
}
