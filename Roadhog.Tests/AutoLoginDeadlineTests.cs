using System.Diagnostics;
using Roadhog.Application.Login;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Login;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.WorkerProcesses;

internal static class AutoLoginDeadlineTests
{
    private static LoginSnapshot Scene(LoginAction action=LoginAction.AcceptLoginProtocol) =>
        new("session",LoginStage.PasswordLogin,action,action.ToString(),"登录页面");
    private static LoginSnapshot World()=>new("session",LoginStage.InWorld,LoginAction.None,"world","在线","role",123);
    private static readonly AutoLoginOptions Fast=new()
    {
        TransitionTimeout=TimeSpan.FromMilliseconds(300),Timeout=TimeSpan.FromSeconds(3),
        PollInterval=TimeSpan.FromMilliseconds(2),ActionRetryInterval=TimeSpan.FromMilliseconds(10),
        LauncherReadyDelay=TimeSpan.Zero,InputReleaseTimeout=TimeSpan.FromMilliseconds(100)
    };
    private sealed class Session(Func<CancellationToken,Task<LoginSnapshot>> read,
        Func<CancellationToken,Task<bool>>? execute=null,Func<Task>? release=null):IAutoLoginSession
    {
        internal int Actions,Releases;
        public Task<LoginSnapshot> ReadAsync(CancellationToken token)=>read(token);
        public Task<bool> ExecuteAsync(LoginSnapshot snapshot,LoginAction action,CancellationToken token)
        { Actions++;return execute?.Invoke(token)??Task.FromResult(true); }
        public Task ReleaseInputAsync(){Releases++;return release?.Invoke()??Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private static void Require(bool condition,string message)
    { if(!condition)throw new InvalidOperationException(message); }
    private static async Task ExpectAsync<T>(Task task) where T:Exception
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch(T) when(task.IsCompleted) { return; }
        throw new InvalidOperationException("Expected "+typeof(T).Name);
    }

    internal static async Task AcceptedRetriesCannotResetDeadlineAsync()
    {
        foreach(bool changingIdentity in new[]{false,true})
        {
            int reads=0;
            await using var session=new Session(_=>Task.FromResult(Scene() with
            {
                Session=changingIdentity?"process-"+reads:"session",
                ActionIdentity=changingIdentity?(++reads).ToString():"button"
            }));
            var elapsed=Stopwatch.StartNew();
            var result=await new AutoLoginRunner(Fast).RunAsync(session,"role",null,CancellationToken.None);
            Require(!result.Success&&result.Error!.Contains("步骤等待超时")&&elapsed.Elapsed<TimeSpan.FromSeconds(2),
                "Accepted repeated clicks and changed process/control identities cannot extend an unchanged step to the total deadline.");
            Require(session.Actions>1&&session.Releases==1,"Retryable controls retain retries and release once on timeout.");
        }
    }

    internal static async Task SamePageProgressGetsNewBudgetAsync()
    {
        var actions=new[]{LoginAction.SelectPasswordTab,LoginAction.AcceptLoginProtocol,LoginAction.SubmitCredentials};
        int stage=0;
        await using var session=new Session(_=>Task.FromResult(stage==actions.Length?World():Scene(actions[stage])),async token=>
        { await Task.Delay(170,token);stage++;return true; });
        var result=await new AutoLoginRunner(Fast).RunAsync(session,"role",null,CancellationToken.None);
        Require(result.Success&&session.Actions==3,"Verified actions advancing within the password page each receive a full step budget.");
    }

    internal static async Task ActionAndColdReadObeyStepBudgetAsync()
    {
        foreach(bool reading in new[]{false,true})
        {
            using var never=new ManualResetEventSlim();
            var native=new LoginOperationExecutor(TimeSpan.FromSeconds(5));
            await using var session=new Session(
                token=>reading?native.RunAsync(_=>{never.Wait();return Scene();},"test",token):Task.FromResult(Scene()),
                token=>native.RunAsync(_=>{never.Wait();return true;},"test",token));
            try
            {
                var result=await new AutoLoginRunner(Fast).RunAsync(session,"role",null,CancellationToken.None);
                Require(!result.Success&&result.Error!.Contains("步骤等待超时"),"Step cancellation reaches in-flight reads/actions instead of waiting ten minutes.");
                Require(session.Actions==(reading?0:1)&&session.Releases==1,"No next input after a blocked operation.");
            }
            finally { never.Set();native.Close(()=>{}); }
        }
    }

    internal static async Task NativeTimeoutRejectsLatePublicationAsync()
    {
        foreach(bool cancel in new[]{false,true})
        {
            using var stop=new CancellationTokenSource();
            using var gate=new ManualResetEventSlim();
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleaned=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var publisher=new LoginSnapshotPublisher(new("test",0,"","dma"));
            var executor=new LoginOperationExecutor(cancel?TimeSpan.FromSeconds(3):TimeSpan.FromMilliseconds(150));
            int bound=0,reads=0;
            var observation=new LoginObservation(Scene(),1,2,"window",new(10,20),new(0,0,100,100));
            var work=executor.RunAsync(token=>publisher.Capture(lifecycle=>
            {
                reads++;lifecycle("one");entered.TrySetResult();gate.Wait();return observation;
            },_=>bound++,token),"test",stop.Token);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if(cancel)stop.Cancel();
                if(cancel)await ExpectAsync<OperationCanceledException>(work);
                else await ExpectAsync<TimeoutException>(work);
                await ExpectAsync<ObjectDisposedException>(executor.RunAsync(_=>++reads,"test",CancellationToken.None));
                // Dispose must invalidate immediately even though capture holds its coordinator.
                if(cancel)await Task.Run(publisher.Dispose).WaitAsync(TimeSpan.FromSeconds(1));
                executor.Close(()=>cleaned.TrySetResult());
                Require(!cleaned.Task.IsCompleted,"Inference resources stay alive until their native call actually finishes.");
                gate.Set();
                await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Require(bound==0&&reads==1,"Late completion cannot publish/bind a click target or start a second native read.");
            }
            finally { gate.Set(); }
        }
    }

    internal static async Task TransientReadAndInputTimeoutAsync()
    {
        var reads=new LoginOperationExecutor(TimeSpan.FromSeconds(1));
        await ExpectAsync<IOException>(reads.RunAsync<int>(_=>throw new IOException("temporary"),"test",CancellationToken.None));
        Require(await reads.RunAsync(_=>7,"test",CancellationToken.None)==7,"A completed transient failure does not retire a healthy session.");
        var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var input=new LoginOperationExecutor(TimeSpan.FromMilliseconds(60));
        int clicks=0;CancellationToken received=default;
        await ExpectAsync<TimeoutException>(input.RunTaskAsync(token=>{received=token;clicks++;return gate.Task;},"input",CancellationToken.None));
        Require(received.IsCancellationRequested,"Timeout cancels the underlying multi-key operation, not only the await.");
        await ExpectAsync<ObjectDisposedException>(input.RunTaskAsync(_=>{clicks++;return Task.FromResult(2);},"input",CancellationToken.None));
        gate.SetResult(1);
        Require(clicks==1,"A timed-out input operation cannot overlap a new click.");
        input.Close(()=>{});reads.Close(()=>{});
    }

    internal static async Task CaptchaWaitsForBoundReadyImageAsync()
    {
        var original=new CaptchaImage(1,[],[],"old");
        var changed=original with {Hash="new"};
        var before=new Dictionary<ulong,CaptchaImage>{{1,original}};
        int polls=0;
        var image=await CaptchaImageWaiter.WaitAsync(_=>
        {
            polls++;
            // Unknown buffers and the old picture are not evidence that Refresh completed.
            var candidates=polls<4?new[]{original,changed with {Address=2}}:new[]{changed};
            return Task.FromResult(CaptchaMemoryReader.Changed(before,candidates));
        },CancellationToken.None,TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(15));
        Require(image==changed&&polls==4,"Wait across slow image updates using the same positive-change baseline without another refresh.");
        polls=0;
        var first=await CaptchaImageWaiter.WaitAsync(_=>Task.FromResult(++polls<3?null:"loaded"),
            CancellationToken.None,TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(10));
        Require(first=="loaded"&&polls==3,"Initial image loading is polled before refresh or recognition.");
        var absent=await CaptchaImageWaiter.WaitAsync<string>(_=>Task.FromResult<string?>(null),
            CancellationToken.None,TimeSpan.FromMilliseconds(50),TimeSpan.FromMilliseconds(5));
        Require(absent is null,"A missing image has a finite wait and cannot become OCR input.");
        using var stop=new CancellationTokenSource();stop.Cancel();
        await ExpectAsync<OperationCanceledException>(CaptchaImageWaiter.WaitAsync<string>(_=>throw new InvalidOperationException("must not read"),stop.Token));
        await ExpectAsync<TimeoutException>(CaptchaImageWaiter.WaitAsync<string>(async token=>
        { await Task.Delay(Timeout.Infinite,token);return "late"; },CancellationToken.None,TimeSpan.FromMilliseconds(50)));
    }

    internal static async Task IdentityTimeoutNeverStartsLateAsync()
    {
        var saved=new AccountConfig {CharacterName="role",HardwareVerificationSessionId=HardwareVerificationSession.CurrentId};
        var late=new TaskCompletionSource<PlayerSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts=0;CancellationToken received=default;
        var result=await AccountIdentityStartGuard.RunAsync(saved,token=>{received=token;return late.Task;},
            ()=>{starts++;return OperationResult.Ok();},CancellationToken.None,TimeSpan.FromMilliseconds(80));
        Require(!result.Success&&result.Error!.Contains("核验等待超时")&&received.IsCancellationRequested,"Identity verification has a bounded, canceled provider read.");
        late.SetResult(new(1,0,"role",100,100,100,100,0,null,DateTimeOffset.UtcNow));
        await Task.Yield();
        Require(starts==0,"A late matching identity cannot start business after the attempt timed out.");
        Task? worker=null;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normal=await AccountIdentityStartGuard.RunAsync(saved,_=>late.Task,()=>
        {
            worker=Task.Run(async()=>
            {
                await ready.Task;
                Require(!SnapshotReadBudget.OperationCancelled,"The started worker must not inherit the identity check's canceled read scope.");
            });
            return OperationResult.Ok();
        },CancellationToken.None,TimeSpan.FromMilliseconds(80));
        ready.SetResult();
        Require(normal.Success,"Matching identity still starts normally.");
        await worker!;
    }

    internal static async Task ReleaseCannotHangOrClaimSuccessAsync()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session=new Session(_=>Task.FromResult(World()),release:()=>release.Task);
        try
        {
            var result=await new AutoLoginRunner(Fast).RunAsync(session,"role",null,CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            Require(!result.Success&&result.Error!.Contains("释放输入")&&session.Releases==1,
                "Input release timeout returns an explicit rejection, never successful login/start or automatic crash recovery.");
        }
        finally { release.SetResult(); }
        await using var failed=new Session(_=>Task.FromResult(World()),release:()=>throw new IOException("offline"));
        Require(!(await new AutoLoginRunner(Fast).RunAsync(failed,"role",null,CancellationToken.None)).Success,
            "A synchronous release failure is also a rejection instead of a crash/relaunch signal.");
    }
}
