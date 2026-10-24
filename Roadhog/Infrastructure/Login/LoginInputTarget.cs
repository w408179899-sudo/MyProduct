using Roadhog.Infrastructure.Login.Native;

namespace Roadhog.Infrastructure.Login;

internal static class LoginInputTarget
{
    internal static void Validate(LoginObservation expected,LoginObservation actual,bool compareAction=false)
    {
        if(actual.Snapshot.Session!=expected.Snapshot.Session||actual.Pid!=expected.Pid||actual.SessionPid!=expected.SessionPid
            ||actual.Window!=expected.Window||actual.Bounds!=expected.Bounds||actual.Snapshot.Stage!=expected.Snapshot.Stage
            ||(compareAction&&(actual.Snapshot.ActionIdentity!=expected.Snapshot.ActionIdentity||actual.Snapshot.Action!=expected.Snapshot.Action)))
            throw new InvalidOperationException("登录输入目标已变化，重新识别当前页面。");
    }
}
