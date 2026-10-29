using System.Text.Json;
using Roadhog.Application.Login;
using static Roadhog.Infrastructure.Login.Native.LoginNativeReader;

namespace Roadhog.Infrastructure.Login.Native;

// One capture only: a process is a candidate, not proof of a usable accelerator.
// Keep each validated page bound to its own PID/window. No cross-process UI merge.
internal sealed class UuLoginCandidates(IEnumerable<uint> pids, Func<uint, JsonElement[]> readWindows,
    Func<uint, JsonElement, JsonElement> readUi, string session, uint sessionPid)
{
    private sealed class Candidate(uint pid)
    {
        internal readonly uint Pid = pid;
        internal JsonElement[] Windows = [];
        internal Exception? Failure;
    }

    private readonly Candidate[] _candidates = pids.Distinct().Order().Select(pid => new Candidate(pid)).ToArray();
    private bool _captured;

    internal LoginObservation? ReadReconnect()
    {
        var matches = new List<LoginObservation>();
        foreach (var candidate in _candidates)
        {
            try
            {
                candidate.Windows = readWindows(candidate.Pid);
                var reconnect = ProjectUuReconnectWindows(candidate.Windows,
                    window => readUi(candidate.Pid, window), session, candidate.Pid, sessionPid);
                if (reconnect is not null) matches.Add(reconnect);
            }
            catch (Exception exception) when (LoginReadErrors.IsRetryable(exception))
            { candidate.Failure = exception; }
        }
        _captured = true;
        var selected = Select(matches);
        if (selected is not null) return selected;
        if (_candidates.Length > 0 && _candidates.All(candidate => candidate.Failure is not null))
            throw Unreadable(_candidates[0].Failure!);
        return null;
    }

    internal LoginObservation? ReadMain()
    {
        if (!_captured) throw new InvalidOperationException("UU reconnect inspection must precede main-window selection.");
        var matches = new List<LoginObservation>();
        Exception? failure = _candidates.FirstOrDefault(candidate => candidate.Failure is not null)?.Failure;
        foreach (var candidate in _candidates.Where(candidate => candidate.Failure is null))
        {
            foreach (var window in candidate.Windows)
            {
                try
                {
                    if (!Flag(window,"Visible") || Flag(window,"Disabled")
                        || (Address(Text(window,"Style")) & 0x60000000) != 0
                        || Text(window,"Manager") == "0x0" || Client(window).Width < 800 || Client(window).Height <= 0)
                        continue;
                    matches.Add(ProjectUu(readUi(candidate.Pid,window),window,session,candidate.Pid,sessionPid));
                }
                catch (Exception exception) when (LoginReadErrors.IsRetryable(exception))
                { failure ??= exception; }
            }
        }
        var selected = Select(matches);
        if (selected is not null) return selected;
        // A failed candidate is never evidence that UU is absent. Let the existing
        // provider retry/hold pipeline handle it instead of opening another copy.
        if (failure is not null) throw Unreadable(failure);
        return null;
    }

    private static LoginObservation? Select(IEnumerable<LoginObservation> matches) => matches
        .OrderBy(match => match.Snapshot.Action switch
        {
            LoginAction.Reaccelerate or LoginAction.LaunchGame => 0,
            _ when match.Snapshot.Stage == LoginStage.Accelerating => 1,
            LoginAction.Accelerate => 2,
            _ => 3
        })
        // Stable ordering avoids changing the chosen process while focus cycles.
        .ThenBy(match => match.Pid).ThenBy(match => Address(match.Window)).FirstOrDefault();

    private static InvalidOperationException Unreadable(Exception cause) =>
        new("等待可用的 UU 窗口读取完成：" + cause.Message, cause);
}
