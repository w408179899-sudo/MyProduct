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
            var accounts = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);
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

            await WriteAccountsAsync(accounts.Select(account => account.Clone()).ToList(), cancellationToken)
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

    public async Task<OperationResult> UpsertAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        if (!account.Validate(out var error))
        {
            return OperationResult.Fail(error);
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accounts = await ReadAccountsAsync(cancellationToken).ConfigureAwait(false);

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

            await WriteAccountsAsync(accounts, cancellationToken).ConfigureAwait(false);
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

    private async Task<List<AccountConfig>> ReadAccountsAsync(CancellationToken cancellationToken)
    {
        if (!AtomicJsonFile.Exists(_path)) return [];

        await using var stream = AtomicJsonFile.OpenRead(_path);
        var document = await JsonSerializer.DeserializeAsync<AccountConfigDocument>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return document?.Accounts ?? [];
    }

    private Task WriteAccountsAsync(List<AccountConfig> accounts, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(_path, new AccountConfigDocument
        {
            Accounts = accounts
        }, _jsonOptions, cancellationToken);

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
