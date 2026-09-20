using Roadhog.Core.Api;
using Roadhog.Core.Input;
using Roadhog.Core.Model;

namespace Roadhog.Application.Input;

/// <summary>Shared cursor feedback for shop clicks and inventory drags; never resets to a screen corner.</summary>
public sealed class FeedbackMouseMover(IKeyboardInput input, IRoadhogSnapshotReader snapshots,
    Func<int, CancellationToken, Task>? delay = null)
{
    public async Task MoveAsync(GameUiPoint point, CancellationToken token)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var cursor = (await snapshots.ReadUiCursorAsync().WaitAsync(token).ConfigureAwait(false)).Value;
            if (point.X < 0 || point.Y < 0 || point.X >= cursor.Width || point.Y >= cursor.Height)
                throw new InvalidOperationException("目标在游戏窗口外。");
            var dx = point.X - cursor.Position.X; var dy = point.Y - cursor.Position.Y;
            if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) return;
            var result = await input.MoveMouseRelativeAsync(Math.Clamp(dx, -70, 70), Math.Clamp(dy, -70, 70), token).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException(result.Error ?? "鼠标移动失败。");
            await (delay?.Invoke(70, token) ?? Task.Delay(70, token)).ConfigureAwait(false);
        }
        throw new TimeoutException("鼠标未到达目标。");
    }
}
