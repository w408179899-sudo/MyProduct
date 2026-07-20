using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Core.Licensing;

namespace Roadhog.Infrastructure.Licensing;

public sealed class DpapiLicenseCredentialStore : ILicenseCredentialStore
{
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("Roadhog.LicenseCredential.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _path;

    public DpapiLicenseCredentialStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("License credential path cannot be empty.", nameof(path))
            : Path.GetFullPath(path);
    }

    public async Task<OperationResult<LicenseCredential?>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return OperationResult<LicenseCredential?>.Ok(null);
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            var jsonBytes = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            var credential = JsonSerializer.Deserialize<LicenseCredential>(jsonBytes, JsonOptions);
            if (credential is null)
            {
                return OperationResult<LicenseCredential?>.Fail("Local license credential is invalid.");
            }

            if (!credential.Validate(out var error))
            {
                return OperationResult<LicenseCredential?>.Fail(error);
            }

            return OperationResult<LicenseCredential?>.Ok(credential);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return OperationResult<LicenseCredential?>.Fail("Failed to read local license credential: " + ex.Message);
        }
    }

    public async Task<OperationResult> SaveAsync(
        LicenseCredential credential,
        CancellationToken cancellationToken = default)
    {
        if (!credential.Validate(out var error))
        {
            return OperationResult.Fail(error);
        }

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return OperationResult.Fail("License credential directory is invalid.");
        }

        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(credential, JsonOptions);
            var protectedBytes = ProtectedData.Protect(
                jsonBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return OperationResult.Fail("Failed to save local license credential: " + ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }
}
