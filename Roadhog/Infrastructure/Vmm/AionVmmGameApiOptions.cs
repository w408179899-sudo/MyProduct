namespace Roadhog.Infrastructure.Vmm;

public sealed class AionVmmGameApiOptions
{
    public string DefaultProcessName { get; set; } = "Aion.bin";

    public string DefaultModuleName { get; set; } = "Game.dll";

    public string DefaultVmmDeviceName { get; set; } = "fpga";

    public string? MemProcFsHome { get; set; }

    public string VmmDeviceEnvironmentVariable { get; set; } = "VMM_DEVICE";

    public string VmmRemoteEnvironmentVariable { get; set; } = "VMM_REMOTE";

    public string ProcessEnvironmentVariable { get; set; } = "VMM_PROCESS";

    public string ModuleEnvironmentVariable { get; set; } = "VMM_MODULE";

    public string SkillXmlEnvironmentVariable { get; set; } = "AION_CLIENT_SKILLS_XML";

    public string SkillXmlLegacyEnvironmentVariable { get; set; } = "AION_SKILL_XML";

    public bool GroupByDisplayName { get; set; } = true;

    public bool FilterUtilitySkills { get; set; } = true;
}
