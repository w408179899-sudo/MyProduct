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
    public int TickIntervalMs { get; set; } = 40;

    public int ChainTickIntervalMs { get; set; } = 40;

    public int TargetIdleDelayMs { get; set; } = 200;

    public int KeyHoldMs { get; set; } = 25;

    public bool AttackKeyLoopEnabled { get; set; } = true;

    public int AttackKeyLoopIntervalMs { get; set; } = 70;

    public int KeyGapMs { get; set; } = 30;

    public int RepeatGuardMs { get; set; } = 120;

    public int PostPressSuppressMs { get; set; } = 650;

    public int ConfirmTimeoutMs { get; set; } = 1500;

    public int ConfirmPollMs { get; set; } = 30;

    public int DefaultChainTimeMs { get; set; } = 5000;

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

    public CombatScriptSettings Clone()
    {
        return new CombatScriptSettings
        {
            EnableLoot = EnableLoot,
            ContestMonster = ContestMonster,
            CounterEnemyRace = CounterEnemyRace
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
            AutoEquip = AutoEquip,
            AutoDecompose = AutoDecompose,
            BagCleanupThreshold = BagCleanupThreshold,
            BagTotalSlots = BagTotalSlots
        };
    }
}

public sealed class SkillScriptSettings
{
    public SkillConfigurationMode Mode { get; set; } = SkillConfigurationMode.Auto;

    public List<string> KeyOrder { get; set; } = DefaultKeyOrder();

    public string TriggerPrefixMode { get; set; } = "TopContiguousTriggerSkills";

    public List<SkillConfigNode> ExecutionTree { get; set; } = new();

    public List<ManualSkillMappingConfig> ManualMappings { get; set; } = new();

    public SkillScriptSettings Clone()
    {
        return new SkillScriptSettings
        {
            Mode = Mode,
            KeyOrder = KeyOrder?.ToList() ?? DefaultKeyOrder(),
            TriggerPrefixMode = TriggerPrefixMode,
            ExecutionTree = ExecutionTree?.Select(node => node.Clone()).ToList() ?? new List<SkillConfigNode>(),
            ManualMappings = ManualMappings?.Select(mapping => mapping.Clone()).ToList() ?? new List<ManualSkillMappingConfig>()
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
            "NumPad0"
        };
    }
}

public enum SkillConfigurationMode
{
    Auto,
    ManualMapping
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
