namespace Roadhog.Core.Accounts;

public sealed class AccountConfig
{
    public string InstanceId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;

    public AccountKmBoxSettings? KmBox { get; set; }

    public bool AutoRecover { get; set; } = true;

    public string LicenseCredentialPath { get; set; } = string.Empty;

    public string BagCleanupNameListPath { get; set; } = string.Empty;

    public string RadarMapDirectory { get; set; } = string.Empty;

    public string OwnerLicenseGrantPath { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HardwareVerificationSessionId { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public string TargetProcessName { get; set; } = string.Empty;

    public string HardwareKey { get; set; } = string.Empty;

    public string HardwareBindingKind { get; set; } = string.Empty;

    public string HardwareBindingConfidence { get; set; } = string.Empty;

    public string HardwareDeviceInstanceId { get; set; } = string.Empty;

    public string HardwareLocationKey { get; set; } = string.Empty;

    public string HardwareDisplayName { get; set; } = string.Empty;

    public string VmmDeviceName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string ProfileName { get; set; } = "default_profile";

    public AccountMainMode MainMode { get; set; } = AccountMainMode.CustomCombat;

    public AccountCombatMode CombatMode { get; set; } = AccountCombatMode.Stationary;

    public string RevivePathName { get; set; } = string.Empty;

    public string CombatPathName { get; set; } = string.Empty;

    public string MaintenancePathName { get; set; } = string.Empty;

    public string SkillProfileName { get; set; } = string.Empty;

    public ScriptSettings? ScriptSettings { get; set; }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            error = "Account name cannot be empty.";
            return false;
        }

        if (ProcessId < 0)
        {
            error = "ProcessId cannot be negative.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public AccountConfig Clone()
    {
        return new AccountConfig
        {
            InstanceId = InstanceId,
            Region = Region,
            KmBox = KmBox?.Clone(),
            AutoRecover = AutoRecover,
            LicenseCredentialPath = LicenseCredentialPath,
            BagCleanupNameListPath = BagCleanupNameListPath,
            RadarMapDirectory = RadarMapDirectory,
            OwnerLicenseGrantPath = OwnerLicenseGrantPath,
            AccountName = AccountName,
            CharacterName = CharacterName,
            HardwareVerificationSessionId = HardwareVerificationSessionId,
            ProcessId = ProcessId,
            TargetProcessName = TargetProcessName,
            HardwareKey = HardwareKey,
            HardwareBindingKind = HardwareBindingKind,
            HardwareBindingConfidence = HardwareBindingConfidence,
            HardwareDeviceInstanceId = HardwareDeviceInstanceId,
            HardwareLocationKey = HardwareLocationKey,
            HardwareDisplayName = HardwareDisplayName,
            VmmDeviceName = VmmDeviceName,
            Enabled = Enabled,
            ProfileName = ProfileName,
            MainMode = MainMode,
            CombatMode = CombatMode,
            RevivePathName = RevivePathName,
            CombatPathName = CombatPathName,
            MaintenancePathName = MaintenancePathName,
            SkillProfileName = SkillProfileName,
            ScriptSettings = ScriptSettings?.Clone()
        };
    }
}
