using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Input;

namespace Roadhog.Infrastructure.Config;

public sealed class JsonKmBoxNetDeviceConfigStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public JsonKmBoxNetDeviceConfigStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("KMBox Net config path cannot be empty.", nameof(path))
            : path;
    }

    public OperationResult<KmBoxNetDeviceConfig> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return OperationResult<KmBoxNetDeviceConfig>.Ok(new KmBoxNetDeviceConfig());
            }

            var text = File.ReadAllText(_path);
            var config = JsonSerializer.Deserialize<KmBoxNetDeviceConfig>(text, _jsonOptions) ?? new KmBoxNetDeviceConfig();
            return OperationResult<KmBoxNetDeviceConfig>.Ok(config);
        }
        catch (Exception ex)
        {
            return OperationResult<KmBoxNetDeviceConfig>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<KmBoxNetDeviceConfig>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return OperationResult<KmBoxNetDeviceConfig>.Ok(new KmBoxNetDeviceConfig());
            }

            await using var stream = File.OpenRead(_path);
            var config = await JsonSerializer.DeserializeAsync<KmBoxNetDeviceConfig>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return OperationResult<KmBoxNetDeviceConfig>.Ok(config ?? new KmBoxNetDeviceConfig());
        }
        catch (Exception ex)
        {
            return OperationResult<KmBoxNetDeviceConfig>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult> SaveAsync(KmBoxNetDeviceConfig config, CancellationToken cancellationToken = default)
    {
        if (!config.Validate(out var error))
        {
            return OperationResult.Fail(error);
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, config, _jsonOptions, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }
}
