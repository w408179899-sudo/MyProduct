using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Roadhog.Core.Accounts;
using Roadhog.Core.Hardware;
using Roadhog.Infrastructure.Composition;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Licensing;

namespace Roadhog.Infrastructure.Config;

/// <summary>Preflights an old client's accounts and shared libraries; the UI owns the final account save.</summary>
public sealed class LegacyAccountImporter
{
    private readonly RoadhogServiceOptions _options;
    private readonly IHardwareDeviceResolver _hardware;

    public LegacyAccountImporter(RoadhogServiceOptions options, IHardwareDeviceResolver? hardware = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _hardware = hardware ?? new WindowsHardwareDeviceResolver(options.HardwareResolver);
    }

    public Task<IReadOnlyList<AccountConfig>> ImportAsync(string accountsJsonPath,
        IReadOnlyList<AccountConfig> existing, CancellationToken cancellationToken = default) =>
        ImportCoreAsync(accountsJsonPath, existing, preserveConflicts: false, cancellationToken);

    public Task<IReadOnlyList<AccountConfig>> ImportPreservingConflictsAsync(string accountsJsonPath,
        IReadOnlyList<AccountConfig> existing, CancellationToken cancellationToken = default) =>
        ImportCoreAsync(accountsJsonPath, existing, preserveConflicts: true, cancellationToken);

    private async Task<IReadOnlyList<AccountConfig>> ImportCoreAsync(string accountsJsonPath,
        IReadOnlyList<AccountConfig> existing, bool preserveConflicts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var source = Path.GetFullPath(accountsJsonPath);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到旧账号配置。", source);
        var directory = Path.GetDirectoryName(source)!;
        var rootName = new DirectoryInfo(directory).Name.Equals("config", StringComparison.OrdinalIgnoreCase)
            ? new DirectoryInfo(directory).Parent?.Name ?? "导入" : new DirectoryInfo(directory).Name;
        var loaded = await new JsonAccountConfigStore(source).LoadAllAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!loaded.Success) throw new InvalidDataException(loaded.Error);
        if (loaded.Value is null || loaded.Value.Count == 0) throw new InvalidDataException("旧配置没有可导入的账号。");
        var imported = new List<AccountConfig>();
        var credentialIdentities = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in loaded.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var account = original.Clone();
            account.InstanceId = Guid.NewGuid().ToString("N");
            account.HardwareVerificationSessionId = string.Empty;
            account.ProcessId = 0;
            account.AccountName = UniqueName(account.AccountName, rootName, existing.Concat(imported));
            if (account.KmBox is null)
            {
                var kmBox = await new JsonKmBoxNetDeviceConfigStore(Path.Combine(directory, "kmbox-net.json"))
                    .LoadAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!kmBox.Success || kmBox.Value is null) throw new InvalidDataException(kmBox.Error ?? "无法读取旧 KMBox 配置。");
                account.KmBox = new AccountKmBoxSettings
                { IpAddress = kmBox.Value.IpAddress.Trim(), Port = kmBox.Value.Port, Mac = kmBox.Value.Mac.Trim() };
            }
            if (!account.KmBox.Validate(out var error)) throw new InvalidDataException(account.AccountName + "：" + error);
            account.VmmDeviceName = ResolveVmmBinding(account);
            var credential = string.IsNullOrWhiteSpace(account.LicenseCredentialPath)
                ? Path.Combine(directory, "license.dat") : Path.GetFullPath(account.LicenseCredentialPath, directory);
            account.LicenseCredentialPath = Path.GetFullPath(credential);
            if (!string.IsNullOrWhiteSpace(account.BagCleanupNameListPath))
                account.BagCleanupNameListPath = Path.GetFullPath(account.BagCleanupNameListPath, directory);
            if (!string.IsNullOrWhiteSpace(account.RadarMapDirectory))
                account.RadarMapDirectory = Path.GetFullPath(account.RadarMapDirectory, directory);
            if (!string.IsNullOrWhiteSpace(account.OwnerLicenseGrantPath))
                account.OwnerLicenseGrantPath = Path.GetFullPath(account.OwnerLicenseGrantPath, directory);
            else if (File.Exists(Path.Combine(directory, "owner-license.json")))
                account.OwnerLicenseGrantPath = Path.Combine(directory, "owner-license.json");
            await ValidateUniqueBindingsAsync(account, existing.Concat(imported), credentialIdentities, cancellationToken).ConfigureAwait(false);
            imported.Add(account);
        }

        // Read all source contents and compare every destination before changing any shared library.
        if (preserveConflicts)
        {
            var preserved = await new LegacyConfigurationImportPlanner(_options)
                .PlanAsync(directory, rootName, imported, cancellationToken).ConfigureAwait(false);
            await ApplyCopiesAsync(preserved, cancellationToken).ConfigureAwait(false);
            return imported;
        }
        var copies = new List<LegacyImportFile>();
        await PlanLibraryAsync(Path.Combine(directory, "paths"), _options.PathLibraryDirectory, copies, cancellationToken).ConfigureAwait(false);
        await PlanLibraryAsync(Path.Combine(directory, "profiles"), _options.ProfileLibraryDirectory, copies, cancellationToken).ConfigureAwait(false);
        await PlanLibraryAsync(Path.Combine(directory, "radar-maps"), _options.RadarMapDirectory, copies, cancellationToken).ConfigureAwait(false);
        var nameListTarget = _options.BagCleanupNameListPath ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(_options.AccountConfigPath))!, JsonBagCleanupNameListStore.DefaultFileName);
        await PlanFileAsync(Path.Combine(directory, JsonBagCleanupNameListStore.DefaultFileName), nameListTarget, copies, cancellationToken).ConfigureAwait(false);
        await PlanFileAsync(Path.Combine(directory, JsonBagCleanupNameListStore.LegacyFileName),
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(nameListTarget))!, JsonBagCleanupNameListStore.LegacyFileName), copies, cancellationToken).ConfigureAwait(false);
        await ApplyCopiesAsync(copies, cancellationToken).ConfigureAwait(false);
        return imported;
    }

    private string ResolveVmmBinding(AccountConfig account)
    {
        if (string.IsNullOrWhiteSpace(account.HardwareKey) || account.HardwareKey.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(account.AccountName + "：旧 DMA 绑定不明确，请先在硬件设置中重新选择设备。");
        var name = account.VmmDeviceName.Trim();
        if (string.IsNullOrEmpty(name) || name.Equals("fpga", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _hardware.BindByKey(account.AccountName, account.HardwareKey);
            if (!resolved.Success || resolved.Value is null)
                throw new InvalidDataException(account.AccountName + "：无法确认旧 fpga 绑定，请连接 DMA 后重新选择硬件设备。");
            name = resolved.Value.VmmDeviceName;
        }
        return CanonicalExplicitVmm(name) ?? throw new InvalidDataException(account.AccountName
            + "：DMA 连接没有明确设备编号，请先在硬件设置中重新选择设备。");
    }

    private async Task ValidateUniqueBindingsAsync(AccountConfig account, IEnumerable<AccountConfig> existing,
        Dictionary<string, string?> credentialIdentities, CancellationToken cancellationToken)
    {
        foreach (var other in existing)
        {
            if (Same(account.HardwareKey, other.HardwareKey) || Same(account.HardwareDeviceInstanceId, other.HardwareDeviceInstanceId)
                || Same(account.HardwareLocationKey, other.HardwareLocationKey))
                throw Conflict(account, other, "DMA 物理设备");
            var otherVmm = other.VmmDeviceName.Trim();
            if (otherVmm.Equals("fpga", StringComparison.OrdinalIgnoreCase)) otherVmm = "fpga://devindex=0";
            if (Same(account.VmmDeviceName, CanonicalExplicitVmm(otherVmm))) throw Conflict(account, other, "DMA 连接");
            if (other.KmBox is { } kmBox && (Same(Mac(account.KmBox!.Mac), Mac(kmBox.Mac))
                || Same(Endpoint(account.KmBox), Endpoint(kmBox)))) throw Conflict(account, other, "KMBox");
            var otherCredential = string.IsNullOrWhiteSpace(other.LicenseCredentialPath)
                ? DefaultCredential(other) : Path.GetFullPath(other.LicenseCredentialPath,
                    Path.GetDirectoryName(Path.GetFullPath(_options.AccountConfigPath))!);
            if (Same(account.LicenseCredentialPath, otherCredential)) throw Conflict(account, other, "授权凭据文件，不能让两个账号并发使用同一凭据");
            // A copied credential is still the same login. Compare opaque bytes without reading or exposing its contents.
            if (File.Exists(account.LicenseCredentialPath) && File.Exists(otherCredential)
                && SameFileBytes(account.LicenseCredentialPath, otherCredential))
                throw Conflict(account, other, "相同授权凭据，不能复制同一凭据给多个账号");
            var importedIdentity = await ReadCredentialIdentityAsync(account.LicenseCredentialPath, credentialIdentities, cancellationToken).ConfigureAwait(false);
            var existingIdentity = await ReadCredentialIdentityAsync(otherCredential, credentialIdentities, cancellationToken).ConfigureAwait(false);
            if (Same(importedIdentity, existingIdentity))
                throw Conflict(account, other, "相同授权客户端身份，不能让两个账号并发使用同一凭据");
        }
    }

    private static async Task<string?> ReadCredentialIdentityAsync(string path,
        Dictionary<string, string?> identities, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        if (identities.TryGetValue(path, out var identity)) return identity;
        // DPAPI encryption is randomized. Compare only the local client identity, never expose license secrets.
        var loaded = await new DpapiLicenseCredentialStore(path).LoadAsync(cancellationToken).ConfigureAwait(false);
        identity = loaded.Success ? loaded.Value?.ClientInstanceId : null;
        identities[path] = identity;
        return identity;
    }

    private string DefaultCredential(AccountConfig account) => Guid.TryParse(account.InstanceId, out var id)
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_options.AccountConfigPath))!, "workers", id.ToString("N"), "license.dat")
        : Path.GetFullPath(_options.LicenseCredentialPath);

    private static string? CanonicalExplicitVmm(string? value)
    {
        var match = Regex.Match(value?.Trim() ?? string.Empty, @"\Afpga://devindex=([0-9]+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var index) ? "fpga://devindex=" + index : null;
    }

    private static string UniqueName(string accountName, string root, IEnumerable<AccountConfig> accounts)
    {
        var names = accounts.Select(account => account.AccountName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(accountName)) return accountName;
        var stem = accountName + " (" + root + ")";
        var candidate = stem;
        for (var index = 2; names.Contains(candidate); index++) candidate = stem + " " + index;
        return candidate;
    }

    private static async Task PlanLibraryAsync(string source, string destination, List<LegacyImportFile> plan, CancellationToken token)
    {
        if (!Directory.Exists(source)) return;
        var targetRoot = Path.GetFullPath(destination);
        var files = Directory.EnumerateFiles(source, "*.json", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false });
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(targetRoot, Path.GetRelativePath(source, file)));
            if (!target.StartsWith(Path.TrimEndingDirectorySeparator(targetRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("共享配置路径超出目标目录。");
            await PlanFileAsync(file, target, plan, token).ConfigureAwait(false);
        }
    }

    private static async Task PlanFileAsync(string source, string target, List<LegacyImportFile> plan, CancellationToken token)
    {
        if (!File.Exists(source)) return;
        target = Path.GetFullPath(target);
        var bytes = await File.ReadAllBytesAsync(source, token).ConfigureAwait(false);
        var planned = plan.FirstOrDefault(copy => Same(copy.Target, target));
        if (planned is not null)
        {
            if (!bytes.AsSpan().SequenceEqual(planned.Content)) throw new InvalidDataException("共享配置冲突：" + target);
            return;
        }
        if (File.Exists(target))
        {
            var current = await File.ReadAllBytesAsync(target, token).ConfigureAwait(false);
            if (!bytes.AsSpan().SequenceEqual(current)) throw new InvalidDataException("共享配置同名但内容不同，未覆盖：" + target);
            return;
        }
        plan.Add(new(target, bytes));
    }

    private static async Task ApplyCopiesAsync(IReadOnlyList<LegacyImportFile> copies, CancellationToken token)
    {
        var created = new List<LegacyImportFile>();
        try
        {
            foreach (var copy in copies)
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(copy.Target)!);
                var temporary = copy.Target + ".import-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, copy.Content, token).ConfigureAwait(false);
                    File.Move(temporary, copy.Target, overwrite: false);
                    created.Add(copy);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            foreach (var copy in created)
            {
                // Roll back only the bytes this import created, preserving any concurrent edit.
                if (File.Exists(copy.Target) && File.ReadAllBytes(copy.Target).AsSpan().SequenceEqual(copy.Content)) File.Delete(copy.Target);
            }
            throw;
        }
    }

    private static bool SameFileBytes(string left, string right) => new FileInfo(left).Length == new FileInfo(right).Length
        && SHA256.HashData(File.ReadAllBytes(left)).AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(right)));
    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string Mac(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static string Endpoint(AccountKmBoxSettings value) => (IPAddress.TryParse(value.IpAddress, out var ip) ? ip.ToString() : value.IpAddress.Trim()) + ":" + value.Port;
    private static InvalidDataException Conflict(AccountConfig imported, AccountConfig existing, string binding) =>
        new(imported.AccountName + " 与已有账号 " + existing.AccountName + " 使用相同的 " + binding + "，导入已取消。");
}
