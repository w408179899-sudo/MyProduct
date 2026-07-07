namespace Roadhog.Core.Accounts;

public sealed class ScriptSettings
{
    public string ProfileName { get; set; } = "default_profile";

    public AccountMainMode MainMode { get; set; } = AccountMainMode.CustomCombat;

    public AccountCombatMode CombatMode { get; set; } = AccountCombatMode.Stationary;

    public CombatScriptSettings Combat { get; set; } = new();

    public PathScriptSettings Paths { get; set; } = new();

    public MaintenanceScriptSettings Maintenance { get; set; } = new();

    public SkillScriptSettings Skills { get; set; } = new();

    public SemiAutoScriptSettings SemiAuto { get; set; } = new();

    public ScriptSettings Clone()
    {
        return new ScriptSettings
        {
            ProfileName = ProfileName,
            MainMode = MainMode,
            CombatMode = CombatMode,
            Combat = (Combat ?? new CombatScriptSettings()).Clone(),
            Paths = (Paths ?? new PathScriptSettings()).Clone(),
            Maintenance = (Maintenance ?? new MaintenanceScriptSettings()).Clone(),
            Skills = (Skills ?? new SkillScriptSettings()).Clone(),
            SemiAuto = (SemiAuto ?? new SemiAutoScriptSettings()).Clone()
        };
    }
}

public sealed class SemiAutoScriptSettings
{
    public int TickIntervalMs { get; set; } = 30;

    public int ChainTickIntervalMs { get; set; } = 40;

    public int TargetIdleDelayMs { get; set; } = 200;

    public int KeyHoldMs { get; set; } = 25;

    public bool AttackKeyLoopEnabled { get; set; } = true;

    public int AttackKeyLoopIntervalMs { get; set; } = 300;

    public int KeyGapMs { get; set; } = 30;

    public int RepeatGuardMs { get; set; } = 120;

    public int PostPressSuppressMs { get; set; } = 650;

    public int ConfirmTimeoutMs { get; set; } = 1500;

    public int ConfirmPollMs { get; set; } = 30;

    public int DefaultChainTimeMs { get; set; } = 2500;

    public SemiAutoScriptSettings Clone()
    {
        return new SemiAutoScriptSettings
        {
            TickIntervalMs = TickIntervalMs,
            ChainTickIntervalMs = ChainTickIntervalMs,
            TargetIdleDelayMs = TargetIdleDelayMs,
            KeyHoldMs = KeyHoldMs,
            AttackKeyLoopEnabled = AttackKeyLoopEnabled,
            AttackKeyLoopIntervalMs = AttackKeyLoopIntervalMs,
            KeyGapMs = KeyGapMs,
            RepeatGuardMs = RepeatGuardMs,
            PostPressSuppressMs = PostPressSuppressMs,
            ConfirmTimeoutMs = ConfirmTimeoutMs,
            ConfirmPollMs = ConfirmPollMs,
            DefaultChainTimeMs = DefaultChainTimeMs
        };
    }
}

public sealed class CombatScriptSettings
{
    public bool EnableLoot { get; set; } = true;

    public bool ContestMonster { get; set; }

    public bool CounterEnemyRace { get; set; }

    public bool PreferAggressiveMonsters { get; set; }

    public List<string> ActiveMonsterNameFilters { get; set; } = new();

    public bool HasStationaryCombatPosition { get; set; }

    public double StationaryCombatX { get; set; }

    public double StationaryCombatY { get; set; }

    public double StationaryCombatZ { get; set; }

    public double StationaryCombatRadius { get; set; } = 30.0D;

    public double PathCombatRadius { get; set; } = 30.0D;

    public double PathFollowReachDistance { get; set; } = 5.0D;

    public double CameraYawPixelsPerDegree { get; set; } = 11.0D;

    public double CameraPitchPixelsPerDegree { get; set; } = 13.0D;

    public CombatScriptSettings Clone()
    {
        return new CombatScriptSettings
        {
            EnableLoot = EnableLoot,
            ContestMonster = ContestMonster,
            CounterEnemyRace = CounterEnemyRace,
            PreferAggressiveMonsters = PreferAggressiveMonsters,
            ActiveMonsterNameFilters = ActiveMonsterNameFilters?.ToList() ?? new List<string>(),
            HasStationaryCombatPosition = HasStationaryCombatPosition,
            StationaryCombatX = StationaryCombatX,
            StationaryCombatY = StationaryCombatY,
            StationaryCombatZ = StationaryCombatZ,
            StationaryCombatRadius = StationaryCombatRadius,
            PathCombatRadius = PathCombatRadius,
            PathFollowReachDistance = PathFollowReachDistance,
            CameraYawPixelsPerDegree = CameraYawPixelsPerDegree,
            CameraPitchPixelsPerDegree = CameraPitchPixelsPerDegree
        };
    }
}

public sealed class PathScriptSettings
{
    public string RevivePathName { get; set; } = "穆尔海姆00133";

    public string CombatPathName { get; set; } = string.Empty;

    public string MaintenancePathName { get; set; } = string.Empty;

    public bool LoopPath { get; set; } = true;

    public bool ReverseAtEnd { get; set; }

    public bool DeathStopPath { get; set; } = true;

    public PathScriptSettings Clone()
    {
        return new PathScriptSettings
        {
            RevivePathName = RevivePathName,
            CombatPathName = CombatPathName,
            MaintenancePathName = MaintenancePathName,
            LoopPath = LoopPath,
            ReverseAtEnd = ReverseAtEnd,
            DeathStopPath = DeathStopPath
        };
    }
}

public sealed class MaintenanceScriptSettings
{
    public bool SitMaintenanceEnabled { get; set; } = true;

    public int SitMpBelowPercent { get; set; } = 10;

    public int SitMpRecoverToPercent { get; set; } = 90;

    public int SitHpBelowPercent { get; set; } = 25;

    public int SitHpRecoverToPercent { get; set; } = 75;

    public List<MaintenanceKeyRuleConfig> HpMaintenanceRules { get; set; } = new();

    public List<MaintenanceKeyRuleConfig> MpMaintenanceRules { get; set; } = new();

    public List<StatusMaintenanceRuleConfig> StatusMaintenanceRules { get; set; } = new();

    public bool AutoEquip { get; set; } = true;

    public bool AutoDecompose { get; set; } = true;

    public int BagCleanupThreshold { get; set; } = 85;

    public int BagTotalSlots { get; set; } = 100;

    public MaintenanceScriptSettings Clone()
    {
        return new MaintenanceScriptSettings
        {
            SitMaintenanceEnabled = SitMaintenanceEnabled,
            SitMpBelowPercent = SitMpBelowPercent,
            SitMpRecoverToPercent = SitMpRecoverToPercent,
            SitHpBelowPercent = SitHpBelowPercent,
            SitHpRecoverToPercent = SitHpRecoverToPercent,
            HpMaintenanceRules = HpMaintenanceRules?.Select(rule => rule.Clone()).ToList() ?? new List<MaintenanceKeyRuleConfig>(),
            MpMaintenanceRules = MpMaintenanceRules?.Select(rule => rule.Clone()).ToList() ?? new List<MaintenanceKeyRuleConfig>(),
            StatusMaintenanceRules = StatusMaintenanceRules?.Select(rule => rule.Clone()).ToList() ?? new List<StatusMaintenanceRuleConfig>(),
            AutoEquip = AutoEquip,
            AutoDecompose = AutoDecompose,
            BagCleanupThreshold = BagCleanupThreshold,
            BagTotalSlots = BagTotalSlots
        };
    }
}

public sealed class MaintenanceKeyRuleConfig
{
    public int BelowPercent { get; set; } = 50;

    public string Key { get; set; } = string.Empty;

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public MaintenanceRuleRunTiming RunTiming { get; set; } = MaintenanceRuleRunTiming.Always;

    public MaintenanceKeyRuleConfig Clone()
    {
        return new MaintenanceKeyRuleConfig
        {
            BelowPercent = BelowPercent,
            Key = Key,
            SkillId = SkillId,
            SkillName = SkillName,
            RunTiming = RunTiming
        };
    }
}

public sealed class StatusMaintenanceRuleConfig
{
    public string Key { get; set; } = string.Empty;

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public uint AbnormalStatusId { get; set; }

    public MaintenanceRuleRunTiming RunTiming { get; set; } = MaintenanceRuleRunTiming.Always;

    public StatusMaintenanceRuleConfig Clone()
    {
        return new StatusMaintenanceRuleConfig
        {
            Key = Key,
            SkillId = SkillId,
            SkillName = SkillName,
            AbnormalStatusId = AbnormalStatusId,
            RunTiming = RunTiming
        };
    }
}

public enum MaintenanceRuleRunTiming
{
    Always,
    InCombat,
    AfterCombat
}

public sealed class SkillScriptSettings
{
    public SkillConfigurationMode Mode { get; set; } = SkillConfigurationMode.Auto;

    public OpeningSkillConfig OpeningSkill { get; set; } = new();

    public bool SpiritmasterAutoSkillLogicEnabled { get; set; }

    public SpiritmasterSkillSettings Spiritmaster { get; set; } = new();

    public List<string> KeyOrder { get; set; } = DefaultKeyOrder();

    public string TriggerPrefixMode { get; set; } = "TopContiguousTriggerSkills";

    public List<SkillConfigNode> ExecutionTree { get; set; } = new();

    public List<ManualSkillMappingConfig> ManualMappings { get; set; } = new();

    public List<SkillConfigNode> SystemExecutionTree { get; set; } = new();

    public SkillScriptSettings Clone()
    {
        return new SkillScriptSettings
        {
            Mode = Mode,
            OpeningSkill = (OpeningSkill ?? new OpeningSkillConfig()).Clone(),
            SpiritmasterAutoSkillLogicEnabled = SpiritmasterAutoSkillLogicEnabled,
            Spiritmaster = (Spiritmaster ?? new SpiritmasterSkillSettings()).Clone(),
            KeyOrder = KeyOrder?.ToList() ?? DefaultKeyOrder(),
            TriggerPrefixMode = TriggerPrefixMode,
            ExecutionTree = ExecutionTree?.Select(node => node.Clone()).ToList() ?? new List<SkillConfigNode>(),
            ManualMappings = ManualMappings?.Select(mapping => mapping.Clone()).ToList() ?? new List<ManualSkillMappingConfig>(),
            SystemExecutionTree = SystemExecutionTree?.Select(node => node.Clone()).ToList() ?? new List<SkillConfigNode>()
        };
    }

    public static List<string> DefaultKeyOrder()
    {
        return new List<string>
        {
            "D1",
            "D2",
            "D3",
            "D4",
            "D5",
            "D6",
            "D7",
            "D8",
            "D9",
            "D0",
            "OemMinus",
            "OemPlus",
            "NumPad1",
            "NumPad2",
            "NumPad3",
            "NumPad4",
            "NumPad5",
            "NumPad6",
            "NumPad7",
            "NumPad8",
            "NumPad9",
            "NumPad0",
            "NumPadSubtract",
            "NumPadAdd"
        };
    }
}

public sealed class SpiritmasterSkillSettings
{
    public List<SpiritmasterSkillRefConfig> DotSkills { get; set; } = new();

    public List<SpiritmasterSkillKeyRuleConfig> SummonSkills { get; set; } = new();

    public int SummonKeyIntervalMs { get; set; } = 2000;

    public string OpeningAttackKey { get; set; } = string.Empty;

    public List<SpiritmasterPetHpRuleConfig> PetHpMaintenanceRules { get; set; } = new();

    public List<SpiritmasterPetBuffRuleConfig> PetBuffRules { get; set; } = new();

    public SpiritmasterSkillSettings Clone()
    {
        return new SpiritmasterSkillSettings
        {
            DotSkills = DotSkills?.Select(rule => rule.Clone()).ToList() ?? new List<SpiritmasterSkillRefConfig>(),
            SummonSkills = SummonSkills?.Select(rule => rule.Clone()).ToList() ?? new List<SpiritmasterSkillKeyRuleConfig>(),
            SummonKeyIntervalMs = SummonKeyIntervalMs <= 0 ? 2000 : SummonKeyIntervalMs,
            OpeningAttackKey = OpeningAttackKey ?? string.Empty,
            PetHpMaintenanceRules = PetHpMaintenanceRules?.Select(rule => rule.Clone()).ToList() ?? new List<SpiritmasterPetHpRuleConfig>(),
            PetBuffRules = PetBuffRules?.Select(rule => rule.Clone()).ToList() ?? new List<SpiritmasterPetBuffRuleConfig>()
        };
    }
}

public sealed class SpiritmasterSkillRefConfig
{
    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public SpiritmasterSkillRefConfig Clone()
    {
        return new SpiritmasterSkillRefConfig
        {
            SkillId = SkillId,
            SkillName = SkillName
        };
    }
}

public sealed class SpiritmasterSkillKeyRuleConfig
{
    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public SpiritmasterSkillKeyRuleConfig Clone()
    {
        return new SpiritmasterSkillKeyRuleConfig
        {
            SkillId = SkillId,
            SkillName = SkillName,
            Key = Key
        };
    }
}

public sealed class SpiritmasterPetHpRuleConfig
{
    public const int DefaultCooldownMs = 10_300;

    public const int MinCooldownMs = 1;

    public const int MaxCooldownMs = 600_000;

    public int BelowPercent { get; set; } = 68;

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public int CooldownMs { get; set; } = DefaultCooldownMs;

    public SpiritmasterPetHpRuleConfig Clone()
    {
        return new SpiritmasterPetHpRuleConfig
        {
            BelowPercent = BelowPercent,
            SkillId = SkillId,
            SkillName = SkillName,
            Key = Key,
            CooldownMs = CooldownMs
        };
    }
}

public sealed class SpiritmasterPetBuffRuleConfig
{
    public uint AbnormalStatusId { get; set; }

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public SpiritmasterPetBuffRuleConfig Clone()
    {
        return new SpiritmasterPetBuffRuleConfig
        {
            AbnormalStatusId = AbnormalStatusId,
            SkillId = SkillId,
            SkillName = SkillName,
            Key = Key
        };
    }
}

public sealed class OpeningSkillConfig
{
    public bool Enabled { get; set; }

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public OpeningSkillConfig Clone()
    {
        return new OpeningSkillConfig
        {
            Enabled = Enabled,
            SkillId = SkillId,
            SkillName = SkillName,
            Key = Key
        };
    }
}

public enum SkillConfigurationMode
{
    Auto,
    ManualMapping,
    SystemClassification
}

public sealed class SkillConfigNode
{
    public uint SkillId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int? ChainTimeMs { get; set; }

    public List<SkillConfigNode> Children { get; set; } = new();

    public SkillConfigNode Clone()
    {
        return new SkillConfigNode
        {
            SkillId = SkillId,
            Name = Name,
            BaseName = BaseName,
            Type = Type,
            ChainTimeMs = ChainTimeMs,
            Children = Children.Select(child => child.Clone()).ToList()
        };
    }
}

public sealed class ManualSkillMappingConfig
{
    public string SkillType { get; set; } = string.Empty;

    public string SkillName { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public ManualSkillMappingConfig Clone()
    {
        return new ManualSkillMappingConfig
        {
            SkillType = SkillType,
            SkillName = SkillName,
            Key = Key
        };
    }
}
