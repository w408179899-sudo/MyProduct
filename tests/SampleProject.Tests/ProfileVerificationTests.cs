using System.Collections.Immutable;
using SampleProject.Bootstrap;
using SampleProject.Desktop.Services;
using SampleProject.Desktop.ViewModels;
using Smart.Adapters.Dma;
using Smart.Contracts;
using Smart.Data;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;
using Xunit;

namespace SampleProject.Tests;

public sealed class ProfileVerificationTests
{
    private sealed class Dialogs : IDesktopDialogs
    {
        public string? Import;
        public bool Answer = true;
        public string? OpenFile(string title, string filter) => Import;
        public string? SaveFile(string title, string filter, string name) => null;
        public bool Confirm(string message) => Answer;
    }
    private sealed class Diagnostics : IHardwareDiagnostics
    {
        public Action? OnInput;
        public Task<D3xxInventory> DiscoverAsync(string path, string driver, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessBinding>> ListProcessesAsync(DmaSettings settings, CancellationToken token) => throw new NotSupportedException();
        public Task<DmaProbeResult> ProbeAsync(DmaSettings settings, CancellationToken token) =>
            Task.FromResult(new DmaProbeResult(new(42, "fixture.exe", "fixture", 4096), true));
        public Task TestInputAsync(InputSettings settings, CancellationToken token) { OnInput?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class Verifier : IAccountProfileVerifier
    {
        public Func<AccountProfile, CancellationToken, Task<ProfileVerification>> Verify =
            (p, _) => Task.FromResult(p.Mode == RuntimeMode.Mock ? new ProfileVerification(null, null) : Passed);
        public Task<ProfileVerification> VerifyAsync(AccountProfile profile, CancellationToken token) => Verify(profile, token);
    }
    private static ProfileVerification Passed => new(new("fixture-id", "Fixture Character"), new(42, "fixture.exe", 4096));
    private sealed class Transport : IProcessMemoryTransport
    {
        public string DeviceId => "fixture-device";
        public string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public string Identity = "process-1";
        public bool Disposed;
        public ProcessBinding GetProcess(int pid, string module) => new(pid, "fixture.exe", Identity, 4096);
        public IReadOnlyList<ProcessBinding> ListProcesses(string? module = null) => [GetProcess(42, "fixture.exe")];
        public ImmutableArray<MemoryBlock> ReadBatch(int pid, IReadOnlyList<MemoryReadRequest> requests) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }
    private sealed class ProjectProbe : IProjectCharacterVerification, IRawChannelReader<VerifiedCharacter, NoPartition>
    {
        public bool Failing;
        public int Captures;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<RawRead<VerifiedCharacter>> CaptureAsync(CaptureContext context, NoPartition partition, CancellationToken token)
        {
            Captures++; Entered.TrySetResult();
            return ValueTask.FromResult(Failing ? RawRead<VerifiedCharacter>.Failed("injected") :
                RawRead<VerifiedCharacter>.Complete(new("character-" + Captures, "Fixture " + Captures)));
        }
        public Func<ISnapshotReader, CancellationToken, ValueTask<VerifiedCharacter>> Register(
            AccountProfile profile, SnapshotCatalog catalog, DmaDispatcher dispatcher, ProcessBinding process)
        {
            var channel = catalog.Register<VerifiedCharacter, NoPartition, VerifiedCharacter>("fixture-character", this,
                new ReplaceMerger<VerifiedCharacter>(x => !string.IsNullOrWhiteSpace(x.Id)), SnapshotMergePolicy.Replace, TimeSpan.Zero);
            return async (reader, token) => (await reader.ReadAsync(channel, NoPartition.Value, token)).Value;
        }
    }
    private sealed class Scope : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "smart-verification-" + Guid.NewGuid().ToString("N"));
        public string Config => Path.Combine(Root, "accounts.json");
        public AccountWorkspace Workspace { get; }
        public Verifier Verifier { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ShellViewModel Model { get; }
        public Scope()
        {
            Workspace = new(new JsonConfigStore<HostSettings>(Config, 1, x => x.Validate()),
                new ProjectHost(leaseDirectory: Path.Combine(Root, "leases")));
            Model = new(Workspace, new Diagnostics(), Dialogs, Config, verifier: Verifier);
        }
        public async Task HardwareDraft()
        {
            await Model.InitializeAsync(); Model.NewCommand.Execute(null);
            Model.Editor.Mode = RuntimeMode.Hardware;
            Model.Editor.LibraryPath = typeof(ProfileVerificationTests).Assembly.Location;
            Model.Editor.ProcessId = "42"; Model.Editor.ModuleName = "fixture.exe";
            Model.Editor.Address = "127.0.0.1"; Model.Editor.Mac = "AABBCCDD";
            Model.Editor.SetBinding(new(0, new(1, 0x0403601f, 1, "fixture-serial", "fixture-device"),
                new string('A', 64), "FTD3XX.dll", new string('B', 64)));
        }
        public async ValueTask DisposeAsync()
        { await Model.ShutdownAsync(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [Fact] public async Task SingleConnectionTestsCannotEnableOrBypassSave()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var before = await File.ReadAllTextAsync(scope.Config);
        await scope.Model.ProbeCommand.ExecuteAsync(null); await scope.Model.TestInputCommand.ExecuteAsync(null);
        Assert.False(scope.Model.SaveCommand.CanExecute(null));
        await scope.Model.SaveCommand.ExecuteAsync(null); // ExecuteAsync itself must also enforce the guard.
        Assert.Contains("请先验证", scope.Model.Notice);
        Assert.Single(scope.Workspace.Accounts); Assert.Equal(before, await File.ReadAllTextAsync(scope.Config));
    }
    [Fact] public async Task VerifiedCharacterEnablesSaveButAnyEditEvenRevertedInvalidatesIt()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        await scope.Model.VerifyCommand.ExecuteAsync(null);
        Assert.True(scope.Model.CanSave); Assert.Contains("Fixture Character", scope.Model.VerificationResult);
        scope.Model.Editor.Port = "12346"; scope.Model.Editor.Port = "12345";
        Assert.False(scope.Model.CanSave); await scope.Model.SaveCommand.ExecuteAsync(null); Assert.Single(scope.Workspace.Accounts);
        await scope.Model.VerifyCommand.ExecuteAsync(null); await scope.Model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, scope.Workspace.Accounts.Count); Assert.False(scope.Model.CanSave);
        Assert.Equal("Fixture Character", scope.Model.Accounts.Last().TestedCharacter);
        Assert.Contains("Fixture Character", await File.ReadAllTextAsync(scope.Config));
    }
    [Fact] public async Task FailedRetestRevokesPreviousApproval()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        await scope.Model.VerifyCommand.ExecuteAsync(null); Assert.True(scope.Model.CanSave);
        scope.Verifier.Verify = (_, _) => throw new IOException("fixture character absent");
        await scope.Model.VerifyCommand.ExecuteAsync(null);
        Assert.False(scope.Model.CanSave); Assert.Contains("fixture character absent", scope.Model.VerificationResult);
        await scope.Model.SaveCommand.ExecuteAsync(null); Assert.Single(scope.Workspace.Accounts);
    }
    [Fact] public async Task MissingCharacterCannotBeApprovedByModuleHeaderOrPid()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        scope.Verifier.Verify = (_, _) => Task.FromResult(new ProfileVerification(null, new(42, "fixture.exe", 4096)));
        await scope.Model.VerifyCommand.ExecuteAsync(null);
        Assert.False(scope.Model.CanSave); Assert.Contains("角色身份", scope.Model.VerificationResult);
    }
    [Fact] public async Task LateSuccessAfterEditOrCancellationCannotApproveSave()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var result = new TaskCompletionSource<ProfileVerification>(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Verifier.Verify = (_, _) => result.Task;
        var pending = scope.Model.VerifyCommand.ExecuteAsync(null);
        try { scope.Model.Editor.Id = "changed"; scope.Model.CancelCommand.Execute(null); }
        finally { result.TrySetResult(Passed); }
        await pending.WaitAsync(TimeSpan.FromSeconds(3)); Assert.False(scope.Model.CanSave);
        await scope.Model.SaveCommand.ExecuteAsync(null); Assert.Single(scope.Workspace.Accounts);
    }
    [Fact] public async Task ImportVerificationFailureDoesNotReplaceOrPersistAnyAccount()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var before = await File.ReadAllTextAsync(scope.Config); var old = scope.Workspace.Accounts.Single();
        var path = Path.Combine(scope.Root, "import.json"); scope.Dialogs.Import = path;
        await new JsonConfigStore<HostSettings>(path, 1, x => x.Validate()).SaveAsync(
            new(ImmutableArray.Create(new AccountProfile("mock-import"), scope.Model.Editor.ToProfile())));
        scope.Verifier.Verify = (p, _) => p.Mode == RuntimeMode.Mock ? Task.FromResult(new ProfileVerification(null, null)) :
            throw new IOException("character verification failed");
        await scope.Model.ImportCommand.ExecuteAsync(null);
        Assert.Same(old, scope.Workspace.Accounts.Single()); Assert.Equal(before, await File.ReadAllTextAsync(scope.Config));
    }
    [Fact] public async Task EmptyTemplateRefusesHardwareApprovalWithoutOpeningDevice()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountProfileVerifier().VerifyAsync(scope.Model.Editor.ToProfile(), default));
        Assert.Contains("尚未接入角色快照验证器", error.Message);
    }
    [Fact] public async Task MockApprovalIsExplicitAndCannotCarryOverToHardware()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync(); scope.Model.NewCommand.Execute(null);
        Assert.False(scope.Model.CanSave); await scope.Model.VerifyCommand.ExecuteAsync(null);
        Assert.True(scope.Model.CanSave); Assert.Contains("未验证真实角色", scope.Model.VerificationResult);
        scope.Model.Editor.Mode = RuntimeMode.Hardware; Assert.False(scope.Model.CanSave);
    }
    [Fact] public async Task CharacterConfirmationCanRejectWrongDeviceAndLeavesConfigUnchanged()
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var before = await File.ReadAllTextAsync(scope.Config);
        await scope.Model.VerifyCommand.ExecuteAsync(null); scope.Dialogs.Answer = false;
        await scope.Model.SaveCommand.ExecuteAsync(null);
        Assert.Single(scope.Workspace.Accounts); Assert.Equal(before, await File.ReadAllTextAsync(scope.Config));
    }
    [Fact] public async Task DeviceIndexWithoutPhysicalIdentityCannotApproveHardwareSave()
    {
        await using var scope = new Scope(); await scope.HardwareDraft(); scope.Model.Editor.ClearBinding();
        await scope.Model.VerifyCommand.ExecuteAsync(null);
        Assert.False(scope.Model.CanSave); Assert.Contains("物理 DMA", scope.Model.VerificationResult);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task OfficialCharacterAndHandshakeAreRequiredAndProcessSwapRejectsApproval(bool swapped)
    {
        await using var scope = new Scope(); await scope.HardwareDraft();
        var transport = new Transport(); var project = new ProjectProbe(); var handshakes = 0;
        var diagnostics = new Diagnostics { OnInput = () => { handshakes++; if (swapped) transport.Identity = "process-2"; } };
        var verifier = new AccountProfileVerifier(project, diagnostics)
        { CreatePool = () => new VmmConnectionPool(new InputLeaseRegistry(), _ => transport) };
        if (swapped) await Assert.ThrowsAsync<IOException>(() => verifier.VerifyAsync(scope.Model.Editor.ToProfile(), default));
        else Assert.Equal("Fixture 1", (await verifier.VerifyAsync(scope.Model.Editor.ToProfile(), default)).Character!.Name);
        Assert.Equal(1, project.Captures); Assert.Equal(1, handshakes); Assert.True(transport.Disposed);
    }
    [Fact] public async Task VerificationCreatesNewSnapshotSessionInsteadOfReusingPreviousCharacter()
    {
        await using var scope = new Scope(); await scope.HardwareDraft(); var project = new ProjectProbe();
        var verifier = new AccountProfileVerifier(project, new Diagnostics())
        { CreatePool = () => new VmmConnectionPool(new InputLeaseRegistry(), _ => new Transport()) };
        Assert.Equal("Fixture 1", (await verifier.VerifyAsync(scope.Model.Editor.ToProfile(), default)).Character!.Name);
        Assert.Equal("Fixture 2", (await verifier.VerifyAsync(scope.Model.Editor.ToProfile(), default)).Character!.Name);
        Assert.Equal(2, project.Captures);
    }
    [Fact] public async Task ColdReadFailureNeverApprovesOrStartsInputAndCancellationReleasesDevice()
    {
        await using var scope = new Scope(); await scope.HardwareDraft(); var project = new ProjectProbe { Failing = true };
        var transport = new Transport(); var handshakes = 0;
        var verifier = new AccountProfileVerifier(project, new Diagnostics { OnInput = () => handshakes++ })
        { CreatePool = () => new VmmConnectionPool(new InputLeaseRegistry(), _ => transport) };
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = verifier.VerifyAsync(scope.Model.Editor.ToProfile(), stop.Token);
        try { await project.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); Assert.False(pending.IsCompleted); }
        finally { stop.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, handshakes); Assert.True(transport.Disposed);
    }
}
