namespace Smart.Hosting;

// Configuration admission evidence, not a business data/read-quality API.
public sealed record VerifiedCharacter(string Id, string Name);
public sealed record ProfileVerification(VerifiedCharacter? Character, SessionTarget? Target)
{
    public void ValidateFor(AccountProfile profile)
    {
        if (profile.Mode == RuntimeMode.Mock) return;
        if (profile.Dma?.Binding is null)
            throw new InvalidOperationException("请先刷新并选择明确的物理 DMA 设备，不能只按设备序号保存。");
        if (Character is null || string.IsNullOrWhiteSpace(Character.Id) || Character.Id.Length > 256 ||
            string.IsNullOrWhiteSpace(Character.Name) || Character.Name.Length > 256 ||
            Target is null || Target.ProcessId <= 0 || Target.ModuleBase == 0)
            throw new InvalidOperationException("未取得明确的角色身份，不能保存硬件配置。");
    }
}

public interface IAccountProfileVerifier
{
    Task<ProfileVerification> VerifyAsync(AccountProfile profile, CancellationToken token);
}
