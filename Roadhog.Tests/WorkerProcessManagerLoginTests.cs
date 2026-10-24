using Roadhog.Infrastructure.WorkerProcesses;

internal static partial class WorkerProcessManagerTests
{
    public static async Task LoginOnlyDoesNotStartAsync()
    {
        await using var test=new TestEnvironment();var account=test.Account(1);var other=test.Account(2);
        account.GameLoginAccount="test-only";
        account.ProtectedGameLoginPassword=Roadhog.Infrastructure.Login.LoginPasswordProtector.Protect("Test42!");
        var manager=await test.ManagerAsync([account,other]);
        Require((await manager.StartAsync(other.InstanceId)).Success,"sibling starts");
        var sibling=await test.InfoAsync(other);
        Require((await manager.LoginAsync(account.InstanceId)).Success,"login succeeds");
        await Task.Delay(350);
        var view=View(manager,account);var info=await test.InfoAsync(account);
        Require(view.State=="logged_in"&&!view.DesiredRunning&&info.Starts==0&&!info.Running,"login only neither starts nor autorecovers bot");
        Require((await test.InfoAsync(other)).Starts==sibling.Starts,"sibling unchanged");
        Require((await manager.StartAsync(account.InstanceId)).Success,"start allowed after login");
        Require((await test.InfoAsync(account)).Starts==1,"bot starts once");
        Require((await manager.LoginAsync(account.InstanceId)).Success,"login on running worker is idempotent");
        Require((await test.InfoAsync(account)).Starts==1,"login never restarts running worker");
        RequireAll((await manager.StopAllAsync()).Values,"login test stops");
    }
    public static async Task LoginStopAndDuplicateAsync()
    {
        await using var test=new TestEnvironment();var account=test.Account(1);var manager=await test.ManagerAsync([account],"slow-login");
        var login=manager.LoginAsync(account.InstanceId);
        await UntilAsync(()=>manager.Snapshot().Single().Error=="模拟登录中","login progress reaches console");
        var pid=View(manager,account).WorkerProcessId;
        Require((await manager.LoginAsync(account.InstanceId)).Success,"duplicate login coalesces");
        Require(!(await manager.StartAsync(account.InstanceId)).Success,"start cannot race login only");
        Require(View(manager,account).WorkerProcessId==pid,"duplicate does not spawn second worker");
        Require((await manager.StopAsync(account.InstanceId)).Success,"stop cancels login");
        Require(!(await login).Success,"cancelled login cannot complete");
        Require(View(manager,account).WorkerProcessId is null&&!View(manager,account).DesiredRunning,"cancel releases worker and intent");
    }
    public static async Task RejectedLoginDoesNotRecoverAsync()
    {
        await using var test=new TestEnvironment();var account=test.Account(1);var manager=await test.ManagerAsync([account],"reject-login");
        Require(!(await manager.LoginAsync(account.InstanceId)).Success,"bad credentials rejects login");
        await Task.Delay(300);var view=View(manager,account);
        Require(!view.DesiredRunning&&view.WorkerProcessId is null,"credentials error doesn't repeat or retain device");
    }
}
