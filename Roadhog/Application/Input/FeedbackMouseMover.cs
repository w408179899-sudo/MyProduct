using System.Diagnostics;
using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Input;

// Movement only: callers may recover positioning, but must never replay a click
// or trade submission on the strength of this exception.
public sealed class MouseMovementTimeoutException(string message) : TimeoutException(message);

/// <summary>Shared cursor feedback for shop clicks and inventory drags; never resets to a screen corner.</summary>
public sealed class FeedbackMouseMover(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null, TimeSpan? confirmationTimeout = null)
{
    public async Task MoveAsync(GameUiPoint point, CancellationToken token, int arrivalTolerance = 1)
    {
        if (arrivalTolerance is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(arrivalTolerance));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var readCancellation = SnapshotReadBudget.EnterCancellation(deadline.Token);
        GameUiPoint? last = null;
        var commands = 0;
        try
        {
            var cursor = await Read(deadline.Token);
            for (; commands < 60;)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Validate(point, cursor);
                last = cursor.Position;
                if (Arrived(last, point, arrivalTolerance)) return;
                var dx = Math.Clamp(point.X - last.X, -70, 70);
                var dy = Math.Clamp(point.Y - last.Y, -70, 70);
                var result = await input.MoveMouseRelativeAsync(dx, dy, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                if (!result.Success) throw new InvalidOperationException(result.Error ?? "鼠标移动失败。");
                commands++;
                // Confirm this input before issuing another correction from the cursor position.
                cursor = await ConfirmMove(last, new(last.X + dx, last.Y + dy), point, deadline.Token, arrivalTolerance);
                last = cursor.Position;
                if (Arrived(last, point, arrivalTolerance)) return;
            }
            throw new MouseMovementTimeoutException($"鼠标未到达目标 ({point.X},{point.Y})；最后位置 {last}，已发送 {commands} 步。");
        }
        catch (SnapshotReadBudgetExceededException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new MouseMovementTimeoutException($"鼠标移动超时：目标 ({point.X},{point.Y})，最后位置 {last}，已发送 {commands} 步。");
        }
        finally { deadline.Cancel(); }
    }

    private async Task<GameUiCursorSnapshot> ConfirmMove(GameUiPoint origin, GameUiPoint expected,
        GameUiPoint target, CancellationToken token, int arrivalTolerance)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(confirmationTimeout ?? TimeSpan.FromSeconds(1));
        using var readCancellation = SnapshotReadBudget.EnterCancellation(deadline.Token);
        var last = origin;
        var lastMovement = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                await (delay?.Invoke(10, deadline.Token) ?? Task.Delay(10, deadline.Token)).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                var cursor = await Read(deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                Validate(target, cursor);
                if (Arrived(cursor.Position, expected) || Arrived(cursor.Position, target, arrivalTolerance)) return cursor;
                if (cursor.Position != last)
                {
                    last = cursor.Position;
                    lastMovement = Stopwatch.GetTimestamp();
                }
                // Pointer acceleration can change the displacement. Once the submitted move
                // has visibly stopped, correct from its actual endpoint, never replay it blindly.
                else if (last != origin && Stopwatch.GetElapsedTime(lastMovement) >= TimeSpan.FromMilliseconds(20))
                    return cursor;
            }
        }
        catch (SnapshotReadBudgetExceededException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new MouseMovementTimeoutException($"鼠标移动响应超时：起点 {origin}，本步目标 {expected}，最后位置 {last}；未重复发送本步。");
        }
        finally { deadline.Cancel(); }
    }

    private async Task<GameUiCursorSnapshot> Read(CancellationToken token) =>
        (await snapshots.ReadUiCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value;

    private static bool Arrived(GameUiPoint actual, GameUiPoint expected, int tolerance = 1) =>
        Math.Abs(actual.X - expected.X) <= tolerance && Math.Abs(actual.Y - expected.Y) <= tolerance;

    private static void Validate(GameUiPoint point, GameUiCursorSnapshot cursor)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= cursor.Width || point.Y >= cursor.Height)
            throw new InvalidOperationException("目标在游戏窗口外。");
    }
}
