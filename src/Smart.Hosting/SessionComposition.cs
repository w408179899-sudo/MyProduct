using System.Text.Json;
using System.Text.Json.Serialization;
using Smart.Contracts;
using Smart.Data;
using Smart.Runtime;
namespace Smart.Hosting;

public sealed class SessionConfigurationException(string message, Exception? inner = null) : ArgumentException(message, inner);

// One composition per account session. Typed tokens and validated options stay in this scope.
public sealed class SessionComposition(AccountProfile profile)
{
    private sealed record Registration(string Id, string[] Channels, Func<ModuleActivationContext, IAccountModule> Activate);
    private readonly List<Registration> _modules = [];
    private bool _sealed;
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public AccountProfile Profile { get; } = profile;
    public SnapshotCatalog Channels { get; } = new();
    public void AddModule(string id, IEnumerable<string> channels, Func<ModuleActivationContext, IAccountModule> activate)
        => Register(id, channels, activate, false);
    private void Register(string id, IEnumerable<string> channels, Func<ModuleActivationContext, IAccountModule> activate, bool acceptsOptions)
    {
        if (_sealed) throw new InvalidOperationException("Session composition is sealed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentNullException.ThrowIfNull(activate);
        if (!acceptsOptions && Profile.ModuleSettings?.ContainsKey(id) == true)
            throw new SessionConfigurationException("Module does not declare a typed configuration: " + id);
        if (_modules.Count >= 64 || _modules.Any(x => x.Id == id)) throw new SessionConfigurationException("Duplicate module or module limit exceeded: " + id);
        _modules.Add(new(id, channels.Distinct(StringComparer.Ordinal).ToArray(), activate));
    }
    public void AddModule<TOptions>(string id, IEnumerable<string> channels, TOptions defaults,
        Action<TOptions> validate, Func<ModuleActivationContext, TOptions, IAccountModule> activate) where TOptions : notnull
    {
        TOptions options;
        try
        {
            options = Profile.ModuleSettings?.TryGetValue(id, out var json) == true
                ? json.Deserialize<TOptions>(Options) ?? throw new JsonException("Null module configuration.") : defaults;
            validate(options);
        }
        catch (Exception ex) { throw new SessionConfigurationException("Invalid settings for module " + id + ": " + ex.Message, ex); }
        Register(id, channels, context => activate(context, options), true);
    }
    public void Seal()
    {
        if (_sealed) return;
        var registered = Channels.Channels.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var module in _modules)
            foreach (var channel in module.Channels)
                if (!registered.Contains(channel)) throw new SessionConfigurationException($"Module {module.Id} requires unregistered channel {channel}.");
        foreach (var id in Profile.ModuleSettings?.Keys ?? [])
            if (!_modules.Any(x => x.Id == id)) throw new SessionConfigurationException("Settings refer to an unknown module: " + id);
        Channels.Seal(); _sealed = true;
    }
    public IReadOnlyList<IAccountModule> Activate(ISnapshotReader snapshots, Guid runId)
    {
        if (!_sealed) throw new InvalidOperationException("Seal session composition first.");
        try
        {
            var modules = _modules.Select(registration =>
            {
                var scoped = new ModuleSnapshotReader(snapshots, registration.Id, registration.Channels);
                var module = registration.Activate(new(Profile.Id, registration.Id, runId, scoped));
                if (module.Id != registration.Id || !registration.Channels.ToHashSet(StringComparer.Ordinal).SetEquals(module.RequiredChannels))
                    throw new SessionConfigurationException("Module identity/channels differ from registration: " + registration.Id);
                return module;
            }).ToArray();
            ModuleCatalog.Validate(modules, Channels.Channels.Select(x => x.Id).ToArray());
            return modules;
        }
        catch (SessionConfigurationException) { throw; }
        catch (Exception ex) { throw new SessionConfigurationException("Module activation failed: " + ex.Message, ex); }
    }
}
