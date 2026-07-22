namespace Roadhog.Core.Accounts;

public sealed class ScriptSettings
{
    public string ProfileName { get; set; } = "default_profile";

    public AccountMainMode MainMode { get; set; } = AccountMainMode.CustomCombat;

    public AccountCombatMode CombatMode { get; set; } = AccountCombatMode.Stationary;

    public CombatScriptSettings Combat { get; set; } = new();

    public PathScriptSettings Paths { get; set; } = new();

    public MaintenanceScriptSettings Maintenance { get; set; } = new();

    public TeamScriptSettings Team { get; set; } = new();

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
            Team = (Team ?? new TeamScriptSettings()).Clone(),
            Skills = (Skills ?? new SkillScriptSettings()).Clone(),
            SemiAuto = (SemiAuto ?? new SemiAutoScriptSettings()).Clone()
        };
    }
}

public sealed class SemiAutoScriptSettings
{
    public const int DefaultChainWindowPerLinkMs = 600;
    public const int MinimumChainWindowPerLinkMs = 1;
    public const int MaximumChainWindowPerLinkMs = 10000;

    public int TickIntervalMs { get; set; } = 30;

    public int ChainTickIntervalMs { get; set; } = 40;

    public int ChainWindowPerLinkMs { get; set; } = DefaultChainWindowPerLinkMs;

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

    public bool ConditionSkillPreemptsChain { get; set; } = true;

    public SemiAutoScriptSettings Clone()
    {
        return new SemiAutoScriptSettings
        {
            TickIntervalMs = TickIntervalMs,
            ChainTickIntervalMs = ChainTickIntervalMs,
            ChainWindowPerLinkMs = ChainWindowPerLinkMs,
            TargetIdleDelayMs = TargetIdleDelayMs,
            KeyHoldMs = KeyHoldMs,
            AttackKeyLoopEnabled = AttackKeyLoopEnabled,
            AttackKeyLoopIntervalMs = AttackKeyLoopIntervalMs,
            KeyGapMs = KeyGapMs,
            RepeatGuardMs = RepeatGuardMs,
            PostPressSuppressMs = PostPressSuppressMs,
            ConfirmTimeoutMs = ConfirmTimeoutMs,
            ConfirmPollMs = ConfirmPollMs,
            DefaultChainTimeMs = DefaultChainTimeMs,
            ConditionSkillPreemptsChain = ConditionSkillPreemptsChain
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
    public const double DefaultRecordingMinimumDistance = 5.0D;

    public const int DefaultDeathReviveClickX = 470;

    public const int DefaultDeathReviveClickY = 300;

    public string RevivePathName { get; set; } = "穆尔海姆00133";

    public string CombatPathName { get; set; } = string.Empty;

    public string MaintenancePathName { get; set; } = string.Empty;

    public string TownReturnKey { get; set; } = string.Empty;

    public double RecordingMinimumDistance { get; set; } = DefaultRecordingMinimumDistance;

    public int DeathReviveClickX { get; set; } = DefaultDeathReviveClickX;

    public int DeathReviveClickY { get; set; } = DefaultDeathReviveClickY;

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
            TownReturnKey = TownReturnKey,
            RecordingMinimumDistance = RecordingMinimumDistance,
            DeathReviveClickX = DeathReviveClickX,
            DeathReviveClickY = DeathReviveClickY,
            LoopPath = LoopPath,
            ReverseAtEnd = ReverseAtEnd,
            DeathStopPath = DeathStopPath
        };
    }
}

public sealed class TeamScriptSettings
{
    public TeamRole Role { get; set; } = TeamRole.Leader;

    public double GroupDistanceMeters { get; set; } = 20.0D;

    public TeamLeaderScriptSettings Leader { get; set; } = new();

    public TeamOutputScriptSettings Output { get; set; } = new();

    public TeamSupportScriptSettings Support { get; set; } = new();

    public TeamScriptSettings Clone()
    {
        return new TeamScriptSettings
        {
            Role = Role,
            GroupDistanceMeters = GroupDistanceMeters,
            Leader = (Leader ?? new TeamLeaderScriptSettings()).Clone(),
            Output = (Output ?? new TeamOutputScriptSettings()).Clone(),
            Support = (Support ?? new TeamSupportScriptSettings()).Clone()
        };
    }
}

public enum TeamRole
{
    Leader,
    Output,
    Support
}

public sealed class TeamLeaderScriptSettings
{
    public bool Enabled { get; set; }

    public bool DungeonMode { get; set; }

    public bool AllowSelfDefense { get; set; } = true;

    public bool StopAdvanceWhenMemberDisconnected { get; set; }

    public TeamLeaderScriptSettings Clone()
    {
        return new TeamLeaderScriptSettings
        {
            Enabled = Enabled,
            DungeonMode = DungeonMode,
            AllowSelfDefense = AllowSelfDefense,
            StopAdvanceWhenMemberDisconnected = StopAdvanceWhenMemberDisconnected
        };
    }
}

public sealed class TeamOutputScriptSettings
{
    public const string DefaultAssistTargetKey = "`";

    public bool Enabled { get; set; }

    public bool DungeonMode { get; set; }

    public bool AllowSelfDefense { get; set; } = true;

    public bool FollowLeader { get; set; } = true;

    public bool OnlyAttackLeaderMarkedTarget { get; set; } = true;

    public bool StopWhenLeaderHasNoTarget { get; set; } = true;

    public bool StopWhenLeaderDead { get; set; } = true;

    public double LeaderDistanceMeters { get; set; } = 12.0D;

    public string AssistTargetKey { get; set; } = DefaultAssistTargetKey;

    public TeamOutputScriptSettings Clone()
    {
        return new TeamOutputScriptSettings
        {
            Enabled = Enabled,
            DungeonMode = DungeonMode,
            AllowSelfDefense = AllowSelfDefense,
            FollowLeader = FollowLeader,
            OnlyAttackLeaderMarkedTarget = OnlyAttackLeaderMarkedTarget,
            StopWhenLeaderHasNoTarget = StopWhenLeaderHasNoTarget,
            StopWhenLeaderDead = StopWhenLeaderDead,
            LeaderDistanceMeters = LeaderDistanceMeters,
            AssistTargetKey = string.IsNullOrWhiteSpace(AssistTargetKey)
                ? DefaultAssistTargetKey
                : AssistTargetKey.Trim()
        };
    }
}

public sealed class TeamSupportScriptSettings
{
    public bool Enabled { get; set; }

    public bool DungeonMode { get; set; }

    public bool JoinCombat { get; set; }

    public bool MentalCleanseEnabled { get; set; } = true;

    public bool PhysicalCleanseEnabled { get; set; } = true;

    public bool AllowSelfDefense { get; set; }

    public bool StopWhenLeaderDead { get; set; } = true;

    public double LeaderDistanceMeters { get; set; } = 12.0D;

    public List<TeamHealSkillRuleConfig> HealSkillRules { get; set; } = new();

    public string MentalCleanseKey { get; set; } = "NumPad8";

    public string PhysicalCleanseKey { get; set; } = "NumPad7";

    public string GroupCleanseKey { get; set; } = string.Empty;

    public TeamSupportScriptSettings Clone()
    {
        return new TeamSupportScriptSettings
        {
            Enabled = Enabled,
            DungeonMode = DungeonMode,
            JoinCombat = JoinCombat,
            MentalCleanseEnabled = MentalCleanseEnabled,
            PhysicalCleanseEnabled = PhysicalCleanseEnabled,
            AllowSelfDefense = AllowSelfDefense,
            StopWhenLeaderDead = StopWhenLeaderDead,
            LeaderDistanceMeters = LeaderDistanceMeters,
            HealSkillRules = HealSkillRules?.Select(rule => rule.Clone()).ToList() ?? new List<TeamHealSkillRuleConfig>(),
            MentalCleanseKey = MentalCleanseKey ?? string.Empty,
            PhysicalCleanseKey = PhysicalCleanseKey ?? string.Empty,
            GroupCleanseKey = GroupCleanseKey ?? string.Empty
        };
    }
}

public sealed class TeamHealSkillRuleConfig
{
    public int BelowPercent { get; set; } = 80;

    public MaintenanceRuleRunTiming RunTiming { get; set; } = MaintenanceRuleRunTiming.Always;

    public TeamHealSkillTargetType TargetType { get; set; } = TeamHealSkillTargetType.Single;

    public string Key { get; set; } = "NumPad1";

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public TeamHealSkillRuleConfig Clone()
    {
        return new TeamHealSkillRuleConfig
        {
            BelowPercent = BelowPercent,
            RunTiming = RunTiming,
            TargetType = TargetType,
            Key = Key ?? string.Empty,
            SkillId = SkillId,
            SkillName = SkillName ?? string.Empty
        };
    }
}

public enum TeamHealSkillTargetType
{
    Single,
    Group
}

public sealed class MaintenanceScriptSettings
{
    public bool BagCleanupEnabled { get; set; }

    public bool SitMaintenanceEnabled { get; set; } = true;

    public int SitMpBelowPercent { get; set; } = 10;

    public int SitMpRecoverToPercent { get; set; } = 90;

    public int SitHpBelowPercent { get; set; } = 25;

    public int SitHpRecoverToPercent { get; set; } = 75;

    public List<MaintenanceKeyRuleConfig> HpMaintenanceRules { get; set; } = new();

    public List<MaintenanceKeyRuleConfig> MpMaintenanceRules { get; set; } = new();

    public List<StatusMaintenanceRuleConfig> StatusMaintenanceRules { get; set; } = new();

    public List<DpMaintenanceRuleConfig> DpMaintenanceRules { get; set; } = new();

    public bool AutoEquip { get; set; } = true;

    public bool AutoDecompose { get; set; } = true;

    public int BagCleanupThreshold { get; set; } = 5;

    public int BagCleanupSellItemClickX { get; set; }

    public int BagCleanupSellItemClickY { get; set; }

    public int BagCleanupSellButtonClickX { get; set; }

    public int BagCleanupSellButtonClickY { get; set; }

    public BagCleanupItemCoordinateMode BagCleanupItemCoordinateMode { get; set; } =
        BagCleanupItemCoordinateMode.LegacyNormalizedTopLeft;

    public List<string> BagCleanupExcludedItemNames { get; set; } = new();

    public List<BagCleanupRuleConfig> BagCleanupRules { get; set; } = BagCleanupRuleCatalog.CreateDefaultRules();

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
            DpMaintenanceRules = DpMaintenanceRules?.Select(rule => rule.Clone()).ToList() ?? new List<DpMaintenanceRuleConfig>(),
            BagCleanupEnabled = BagCleanupEnabled,
            AutoEquip = AutoEquip,
            AutoDecompose = AutoDecompose,
            BagCleanupThreshold = BagCleanupThreshold,
            BagCleanupSellItemClickX = BagCleanupSellItemClickX,
            BagCleanupSellItemClickY = BagCleanupSellItemClickY,
            BagCleanupSellButtonClickX = BagCleanupSellButtonClickX,
            BagCleanupSellButtonClickY = BagCleanupSellButtonClickY,
            BagCleanupItemCoordinateMode = BagCleanupItemCoordinateMode,
            BagCleanupExcludedItemNames = BagCleanupExcludedItemNames?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            BagCleanupRules = BagCleanupRuleCatalog.MergeWithDefaults(BagCleanupRules)
        };
    }
}

public enum BagCleanupItemCoordinateMode
{
    LegacyNormalizedTopLeft,
    WindowRectRelativeExperimental
}

public enum BagCleanupAction
{
    Sell,
    Discard
}

public sealed class BagCleanupRuleConfig
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Quality { get; set; } = string.Empty;

    public List<string> ItemKinds { get; set; } = new();

    public bool Enabled { get; set; }

    public BagCleanupAction Action { get; set; } = BagCleanupAction.Sell;

    public BagCleanupRuleConfig Clone()
    {
        return new BagCleanupRuleConfig
        {
            Key = Key?.Trim() ?? string.Empty,
            DisplayName = DisplayName?.Trim() ?? string.Empty,
            Category = Category?.Trim() ?? string.Empty,
            Quality = Quality?.Trim() ?? string.Empty,
            ItemKinds = ItemKinds?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>(),
            Enabled = Enabled,
            Action = Action
        };
    }
}

public static class BagCleanupRuleCatalog
{
    public const string GreenEquipment = "equipment.green";
    public const string BlueEquipment = "equipment.blue";
    public const string WhiteManastone = "manastone.white";
    public const string GreenManastone = "manastone.green";
    public const string Stigma = "scroll.stigma";
    public const string RecipeScroll = "scroll.recipe";
    public const string SkillBook = "book.skill";
    public const string SpellBook = "book.spell";
    public const string WhiteExtractionStone = "extraction_stone.white";
    public const string GreenExtractionStone = "extraction_stone.green";
    public const string BlueExtractionStone = "extraction_stone.blue";
    public const string GoldExtractionStone = "extraction_stone.gold";
    public const string Medicine = "consumable.medicine";

    public static List<BagCleanupRuleConfig> CreateDefaultRules()
    {
        return new List<BagCleanupRuleConfig>
        {
            Rule(GreenEquipment, "绿色装备", "equipment", "green", "weapon", "armor", "accessory", "shield"),
            Rule(BlueEquipment, "蓝色装备", "equipment", "blue", "weapon", "armor", "accessory", "shield"),
            Rule(WhiteManastone, "白色魔石", "manastone", "white", "manastone"),
            Rule(GreenManastone, "绿色魔石", "manastone", "green", "manastone"),
            Rule(Stigma, "烙印", "scroll", string.Empty, "stigma"),
            Rule(RecipeScroll, "制作图纸/卷轴", "scroll", string.Empty, "recipe", "design", "scroll"),
            Rule(SkillBook, "技能书", "book", string.Empty, "skill_book"),
            Rule(SpellBook, "咒语书", "book", string.Empty, "spell_book"),
            Rule(WhiteExtractionStone, "白色提炼石", "extraction_stone", "white", "extraction_stone"),
            Rule(GreenExtractionStone, "绿色提炼石", "extraction_stone", "green", "extraction_stone"),
            Rule(BlueExtractionStone, "蓝色提炼石", "extraction_stone", "blue", "extraction_stone"),
            Rule(GoldExtractionStone, "金色提炼石", "extraction_stone", "gold", "extraction_stone"),
            Rule(Medicine, "药水/仙药/灵药", "consumable", string.Empty, "potion", "elixir", "remedy")
        };
    }

    public static List<BagCleanupRuleConfig> MergeWithDefaults(IEnumerable<BagCleanupRuleConfig>? configuredRules)
    {
        var configuredByKey = configuredRules?
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Key))
            .GroupBy(rule => rule.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Clone(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BagCleanupRuleConfig>(StringComparer.OrdinalIgnoreCase);

        var merged = new List<BagCleanupRuleConfig>();
        foreach (var defaultRule in CreateDefaultRules())
        {
            if (configuredByKey.TryGetValue(defaultRule.Key, out var configuredRule))
            {
                defaultRule.Enabled = configuredRule.Enabled;
                defaultRule.Action = configuredRule.Action;
            }

            merged.Add(defaultRule);
        }

        foreach (var configuredRule in configuredByKey.Values)
        {
            if (merged.Any(rule => string.Equals(rule.Key, configuredRule.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            merged.Add(configuredRule.Clone());
        }

        return merged;
    }

    private static BagCleanupRuleConfig Rule(
        string key,
        string displayName,
        string category,
        string quality,
        params string[] itemKinds)
    {
        return new BagCleanupRuleConfig
        {
            Key = key,
            DisplayName = displayName,
            Category = category,
            Quality = quality,
            ItemKinds = itemKinds.ToList(),
            Action = BagCleanupAction.Sell
        };
    }
}

public sealed class MaintenanceKeyRuleConfig
{
    public int BelowPercent { get; set; } = 50;

    public MaintenanceRuleActionType ActionType { get; set; } = MaintenanceRuleActionType.Skill;

    public string Key { get; set; } = string.Empty;

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public MaintenanceRuleRunTiming RunTiming { get; set; } = MaintenanceRuleRunTiming.Always;

    public MaintenanceKeyRuleConfig Clone()
    {
        return new MaintenanceKeyRuleConfig
        {
            BelowPercent = BelowPercent,
            ActionType = ActionType,
            Key = Key,
            SkillId = SkillId,
            SkillName = SkillName,
            RunTiming = RunTiming
        };
    }
}

public enum MaintenanceRuleActionType
{
    Skill,
    Potion
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

public sealed class DpMaintenanceRuleConfig
{
    public int RequiredDp { get; set; } = 2000;

    public string Key { get; set; } = string.Empty;

    public uint SkillId { get; set; }

    public string SkillName { get; set; } = string.Empty;

    public MaintenanceRuleRunTiming RunTiming { get; set; } = MaintenanceRuleRunTiming.Always;

    public DpMaintenanceRuleConfig Clone()
    {
        return new DpMaintenanceRuleConfig
        {
            RequiredDp = RequiredDp,
            Key = Key,
            SkillId = SkillId,
            SkillName = SkillName,
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
