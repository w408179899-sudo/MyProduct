using System.Net;
using System.Net.Sockets;
using SampleProject.Bootstrap;
using SampleProject.Desktop.Services;
using SampleProject.Desktop.ViewModels;
using Smart.Adapters.Dma;
using Smart.Hosting;
using Smart.Hosting.Windows;
using Smart.Runtime;
using Xunit;

namespace SampleProject.Tests;

public sealed class DesktopViewModelTests
{
    private sealed class Dialogs : IDesktopDialogs
    {
        public bool Answer = true;
        public string? OpenFile(string title, string filter) => null;
        public string? SaveFile(string title, string filter, string name) => null;
        public bool Confirm(string message) => Answer;
    }
    private sealed class Diagnostics : IHardwareDiagnostics
    {
        public int Calls;
        public Func<CancellationToken, Task<D3xxInventory>>? Discover;
        public Task<D3xxInventory> DiscoverAsync(string path, string driver, CancellationToken token)
        { Calls++; return Discover?.Invoke(token) ?? throw new InvalidOperationException("controlled discovery failure"); }
        public Task<IReadOnlyList<ProcessBinding>> ListProcessesAsync(DmaSettings settings, CancellationToken token)
        { Calls++; return Task.FromResult<IReadOnlyList<ProcessBinding>>([new(42, "fixture.exe", "fixture", 4096)]); }
        public Task<DmaProbeResult> ProbeAsync(DmaSettings settings, CancellationToken token)
        { Calls++; return Task.FromResult(new DmaProbeResult(new(42, "fixture.exe", "fixture", 4096), true)); }
        public Task TestInputAsync(InputSettings settings, CancellationToken token) { Calls++; return Task.CompletedTask; }
    }
    private sealed class Scope : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "smart-desktop-" + Guid.NewGuid().ToString("N"));
        public AccountWorkspace Workspace { get; }
        public Diagnostics Hardware { get; } = new();
        public Dialogs Dialogs { get; } = new();
        public ShellViewModel Model { get; }
        public Scope()
        {
            var path = Path.Combine(Root, "accounts.json");
            Workspace = new(new JsonConfigStore<HostSettings>(path, 1, x => x.Validate()), new ProjectHost(leaseDirectory: Path.Combine(Root, "leases")));
            Model = new(Workspace, Hardware, Dialogs, path);
        }
        public async ValueTask DisposeAsync() { await Model.ShutdownAsync(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static async Task Running(ManagedAccount account)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (account.Status.State != SessionState.Running) await Task.Delay(10, deadline.Token);
    }
    [Fact] public async Task DefaultStartupAndMockLifecycleNeverCallHardwareDiagnostics()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync();
        Assert.False(scope.Model.DiscoverCommand.CanExecute(null));
        await scope.Model.StartCommand.ExecuteAsync(null); await Running(scope.Workspace.Accounts.Single());
        scope.Model.Refresh(); Assert.Equal("运行中", scope.Model.Accounts.Single().State);
        await scope.Model.PauseCommand.ExecuteAsync(null); Assert.Equal(SessionState.Paused, scope.Workspace.Accounts.Single().Status.State);
        Assert.Equal(0, scope.Hardware.Calls); Assert.NotNull(scope.Workspace.Accounts.Single().StartedAt);
    }
    [Fact] public async Task SavingAnotherAccountPreservesRunningAccountAndItsSession()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync();
        var running = scope.Workspace.Accounts.Single(); running.Start(); await Running(running);
        scope.Model.NewCommand.Execute(null); scope.Model.Editor.Id = "second";
        await scope.Model.VerifyCommand.ExecuteAsync(null); await scope.Model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, scope.Workspace.Accounts.Count); Assert.Same(running, scope.Workspace.Accounts[0]);
        Assert.Equal(SessionState.Running, running.Status.State); Assert.Equal(1, running.Status.Generation);
        await scope.Workspace.DeleteAccountAsync("second"); Assert.Same(running, scope.Workspace.Accounts.Single());
        Assert.Equal(SessionState.Running, running.Status.State);
    }
    [Fact] public async Task EditingRunningAccountFailsWithoutChangingPersistedConfiguration()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync();
        var original = await File.ReadAllTextAsync(scope.Model.ConfigurationPath);
        await scope.Model.StartCommand.ExecuteAsync(null); await Running(scope.Workspace.Accounts.Single());
        scope.Model.Editor.Id = "renamed"; await scope.Model.VerifyCommand.ExecuteAsync(null); await scope.Model.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Stop accounts", scope.Model.Notice); Assert.True(scope.Model.IsDirty);
        Assert.Equal(original, await File.ReadAllTextAsync(scope.Model.ConfigurationPath));
        Assert.Equal("local", scope.Workspace.Accounts.Single().Profile.Id);
    }
    [Fact] public async Task FailedSaveRetainsExistingAccountAndDoesNotPublishReplacement()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync();
        var original = scope.Workspace.Accounts.Single();
        using (File.Open(scope.Model.ConfigurationPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Record.ExceptionAsync(() => scope.Workspace.SaveAccountAsync(new("new"), "local"));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Same(original, scope.Workspace.Accounts.Single()); original.Start(); await Running(original);
    }
    [Fact] public async Task UnsavedEditsCannotSilentlySwitchOrStart()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync();
        var selected = scope.Model.SelectedAccount; scope.Model.Editor.Id = "draft"; scope.Dialogs.Answer = false;
        scope.Model.SelectedAccount = null; Assert.Same(selected, scope.Model.SelectedAccount); Assert.Equal("draft", scope.Model.Editor.Id);
        await scope.Model.StartCommand.ExecuteAsync(null); Assert.Equal(SessionState.Stopped, scope.Workspace.Accounts.Single().Status.State);
        Assert.False(scope.Model.ConfirmClose());
    }
    [Fact] public async Task DiscoveryFailureLeavesTheEditorUsableAndShowsActionableError()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync(); scope.Model.Editor.Mode = RuntimeMode.Hardware;
        await scope.Model.DiscoverCommand.ExecuteAsync(null);
        Assert.False(scope.Model.IsBusy); Assert.True(scope.Model.CanEdit); Assert.Contains("controlled discovery failure", scope.Model.Notice);
    }
    [Fact] public async Task ClosingCancelsPendingDiagnosticsAndRejectsLateInventory()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync(); scope.Model.Editor.Mode = RuntimeMode.Hardware;
        var completion = new TaskCompletionSource<D3xxInventory>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken captured = default;
        scope.Hardware.Discover = token => { captured = token; return completion.Task; };
        var pending = scope.Model.DiscoverCommand.ExecuteAsync(null);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = captured.Register(() => cancelled.TrySetResult());
        var close = scope.Model.ShutdownAsync();
        try { await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { completion.TrySetResult(new("FTD3XX.dll", new string('A', 64), [])); }
        await Task.WhenAll(pending, close).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(scope.Model.Devices); Assert.Contains("已取消", scope.Model.Notice);
    }
    [Fact] public async Task ProcessSelectionUpdatesOnlyDraftAndProbeIsExplicit()
    {
        await using var scope = new Scope(); await scope.Model.InitializeAsync(); scope.Model.Editor.Mode = RuntimeMode.Hardware;
        await scope.Model.ListProcessesCommand.ExecuteAsync(null); scope.Model.SelectedProcess = Assert.Single(scope.Model.Processes);
        Assert.Equal("42", scope.Model.Editor.ProcessId); Assert.True(scope.Model.IsDirty);
        Assert.Equal(RuntimeMode.Mock, scope.Workspace.Accounts.Single().Profile.Mode);
        await scope.Model.ProbeCommand.ExecuteAsync(null); Assert.Contains("PID 42", scope.Model.DmaResult);
        scope.Model.Editor.ModuleName = "changed.exe"; Assert.DoesNotContain("通过", scope.Model.DmaResult);
    }
    [Fact] public void InvalidNumericDraftIsRejectedInsteadOfSavingPreviousNumericValue()
    {
        var editor = new ProfileEditor { RetryDelayMs = "abc" };
        Assert.Throws<ArgumentException>(() => editor.ToProfile());
        editor.Port = "65536x"; Assert.Throws<ArgumentException>(() => editor.ToInputSettings());
    }
    [Fact] public async Task PageRegistrationAcceptsExtensionsAndRejectsDuplicateIds()
    {
        await using var scope = new Scope();
        var page = new DesktopPage("project", "项目设置", "P", new object());
        var model = new ShellViewModel(scope.Workspace, scope.Hardware, scope.Dialogs, scope.Model.ConfigurationPath, [page]);
        Assert.Same(page, model.Pages.Last());
        Assert.Throws<ArgumentException>(() => new ShellViewModel(scope.Workspace, scope.Hardware, scope.Dialogs, "x", [page, page]));
    }
    [Fact] public async Task InputDiagnosticSendsOnlyConnectAndReleasesItsLease()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var run = new HardwareDiagnostics().TestInputAsync(new("127.0.0.1", port, "AABBCCDD"), default);
        var request = await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0xAF3C2828u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(request.Buffer.AsSpan(12)));
        await server.SendAsync(request.Buffer, request.RemoteEndPoint); await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, server.Available);
        using var lease = new InputLeaseRegistry(InputLeaseRegistry.SharedDirectory).Acquire($"kmbox:127.0.0.1:{port}");
    }
}
