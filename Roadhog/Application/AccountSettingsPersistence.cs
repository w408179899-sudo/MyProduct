using System.Runtime.CompilerServices;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Profiles;

namespace Roadhog.Application;

/// <summary>Saves the profile/account pair and compensates a failed account write.</summary>
public static class AccountSettingsPersistence
{
    private static readonly ConditionalWeakTable<IScriptProfileStore,
        ConditionalWeakTable<IAccountConfigStore, SemaphoreSlim>> SaveGates = new();

    public static async Task<OperationResult> SaveAsync(
        IScriptProfileStore profiles,
        IAccountConfigStore accounts,
        ScriptProfileDocument candidateProfile,
        AccountConfig candidateAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(candidateProfile);
        ArgumentNullException.ThrowIfNull(candidateAccount);
        var profile = candidateProfile.Clone();
        profile.Name = profile.Name?.Trim() ?? string.Empty;
        var account = candidateAccount.Clone();
        var gate = SaveGates.GetValue(profiles, _ => new()).GetValue(accounts, _ => new(1, 1));
        var entered = false;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            // Read only this candidate's physical identity. A typed absence permits
            // creation; an invalid file or a mismatched stored name permits no write.
            var previousResult = await profiles.LoadOptionalAsync(profile.Name, cancellationToken).ConfigureAwait(false);
            if (!previousResult.Success)
                return OperationResult.Fail("读取原方案失败，配置未保存：" + previousResult.Error);
            var previous = previousResult.Value?.Clone();
            cancellationToken.ThrowIfCancellationRequested();

            var profileResult = await profiles.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
            if (!profileResult.Success)
                return OperationResult.Fail(profileResult.Error ?? "保存方案失败。");

            OperationResult accountResult;
            try
            {
                accountResult = await accounts.UpsertAsync(account, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                accountResult = OperationResult.Fail(exception.Message);
            }
            if (accountResult.Success) return accountResult;

            OperationResult rollback;
            try
            {
                // Cleanup must still run if cancellation caused the account failure.
                rollback = previous is null
                    ? await profiles.DeleteAsync(profile.Name, CancellationToken.None).ConfigureAwait(false)
                    : await profiles.SaveAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                rollback = OperationResult.Fail(exception.Message);
            }
            return OperationResult.Fail(rollback.Success
                ? "保存账号配置失败：" + accountResult.Error + "；方案已恢复。"
                : "保存账号配置失败：" + accountResult.Error + "；方案已部分保存，恢复失败：" + rollback.Error);
        }
        catch (Exception exception)
        {
            return OperationResult.Fail("保存配置未完成：" + exception.Message);
        }
        finally
        {
            if (entered) gate.Release();
        }
    }
}
