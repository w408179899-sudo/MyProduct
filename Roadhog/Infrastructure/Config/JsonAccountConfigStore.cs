using System.Text.Json;
using System.Text.Json.Serialization;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Config;

public sealed class JsonAccountConfigStore : IAccountConfigStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonAccountConfigStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Config path cannot be empty.", nameof(path))
            : path;
    }

    public string ConfigPath => _path;

    public async Task<OperationResult<IReadOnlyList<AccountConfig>>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return OperationResult<IReadOnlyList<AccountConfig>>.Ok(Array.Empty<AccountConfig>());
            }

            await using var stream = File.OpenRead(_path);
            var document = await JsonSerializer.DeserializeAsync<AccountConfigDocument>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);

            var accounts = document?.Accounts ?? new List<AccountConfig>();
            var validation = Validate(accounts);
            return validation.Success
                ? OperationResult<IReadOnlyList<AccountConfig>>.Ok(accounts.Select(account => account.Clone()).ToArray())
                : OperationResult<IReadOnlyList<AccountConfig>>.Fail(validation.Error ?? "Account config validation failed.");
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<AccountConfig>>.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> SaveAllAsync(IReadOnlyList<AccountConfig> accounts, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var validation = Validate(accounts);
            if (!validation.Success)
            {
                return validation;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new AccountConfigDocument
            {
                Version = 1,
                Accounts = accounts.Select(account => account.Clone()).ToList()
            };

            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        if (!account.Validate(out var error))
        {
            return OperationResult.Fail(error);
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accounts = new List<AccountConfig>();
            if (File.Exists(_path))
            {
                await using var readStream = File.OpenRead(_path);
                var document = await JsonSerializer.DeserializeAsync<AccountConfigDocument>(readStream, _jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (document?.Accounts is not null)
                {
                    accounts.AddRange(document.Accounts);
                }
            }

            var index = accounts.FindIndex(item => string.Equals(item.AccountName, account.AccountName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                accounts[index] = account.Clone();
            }
            else
            {
                accounts.Add(account.Clone());
            }

            var validation = Validate(accounts);
            if (!validation.Success)
            {
                return validation;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var writeStream = File.Create(_path);
            await JsonSerializer.SerializeAsync(writeStream, new AccountConfigDocument { Version = 1, Accounts = accounts }, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            _sync.Release();
        }
    }

    private static OperationResult Validate(IReadOnlyList<AccountConfig> accounts)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in accounts)
        {
            if (!account.Validate(out var error))
            {
                return OperationResult.Fail(error);
            }

            if (!names.Add(account.AccountName))
            {
                return OperationResult.Fail("Duplicate account config: " + account.AccountName);
            }
        }

        return OperationResult.Ok();
    }
}
