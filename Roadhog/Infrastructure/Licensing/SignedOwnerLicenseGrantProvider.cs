using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Core.Licensing;

namespace Roadhog.Infrastructure.Licensing;

public sealed class SignedOwnerLicenseGrantProvider : IOwnerLicenseGrantProvider
{
    internal const string EmbeddedPublicKeyBlobBase64 =
        "RUNTMSAAAABM0sdmd3tY/wVzVw9U4/RU9s7T1hGonX0fQXJivBYMOVN4O91pl3OOszWXgPX1KPPR8Xc/Y3kTHmXJ8HHp65WJ";

    private const int CurrentVersion = 1;
    private const string PayloadPrefix = "Roadhog.OwnerLicenseGrant.v1|";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly byte[] _publicKeyBlob;

    public SignedOwnerLicenseGrantProvider(
        string path,
        IDeviceIdentityProvider deviceIdentityProvider,
        string publicKeyBlobBase64 = EmbeddedPublicKeyBlobBase64)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Owner license grant path cannot be empty.", nameof(path))
            : Path.GetFullPath(path);
        _deviceIdentityProvider = deviceIdentityProvider
            ?? throw new ArgumentNullException(nameof(deviceIdentityProvider));
        _publicKeyBlob = Convert.FromBase64String(
            string.IsNullOrWhiteSpace(publicKeyBlobBase64)
                ? throw new ArgumentException("Owner license public key cannot be empty.", nameof(publicKeyBlobBase64))
                : publicKeyBlobBase64);
    }

    public async Task<OperationResult<bool>> IsAuthorizedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return OperationResult<bool>.Ok(false);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<OwnerLicenseGrantDocument>(json, JsonOptions);
            if (document is null
                || document.Version != CurrentVersion
                || !IsSha256Hex(document.DeviceHash)
                || string.IsNullOrWhiteSpace(document.Signature))
            {
                return OperationResult<bool>.Fail("Owner license grant format is invalid.");
            }

            var deviceResult = _deviceIdentityProvider.GetDeviceHash();
            if (!deviceResult.Success || !IsSha256Hex(deviceResult.Value))
            {
                return OperationResult<bool>.Fail(
                    deviceResult.Error ?? "Current device identity is unavailable.");
            }

            var normalizedGrantHash = document.DeviceHash.Trim().ToLowerInvariant();
            var normalizedDeviceHash = deviceResult.Value!.Trim().ToLowerInvariant();
            if (!string.Equals(normalizedGrantHash, normalizedDeviceHash, StringComparison.Ordinal))
            {
                return OperationResult<bool>.Fail("Owner license grant does not match this device.");
            }

            var signature = Convert.FromBase64String(document.Signature);
            var payload = Encoding.UTF8.GetBytes(PayloadPrefix + normalizedGrantHash);
            using var key = CngKey.Import(_publicKeyBlob, CngKeyBlobFormat.EccPublicBlob);
            using var ecdsa = new ECDsaCng(key);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                return OperationResult<bool>.Fail("Owner license grant signature is invalid.");
            }

            return OperationResult<bool>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or JsonException
                or FormatException)
        {
            return OperationResult<bool>.Fail("Failed to read owner license grant: " + ex.Message);
        }
    }

    private static bool IsSha256Hex(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return false;
        }

        return value.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
    }

    private sealed class OwnerLicenseGrantDocument
    {
        public int Version { get; set; }

        public string DeviceHash { get; set; } = string.Empty;

        public string Signature { get; set; } = string.Empty;
    }
}
