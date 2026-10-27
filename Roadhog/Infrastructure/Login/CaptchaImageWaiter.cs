namespace Roadhog.Infrastructure.Login;

// Waits for a positively bound image below the provider boundary. Polling never
// clicks Refresh; the caller keeps one baseline throughout this wait.
internal static class CaptchaImageWaiter
{
    internal static async Task<T?> WaitAsync<T>(Func<CancellationToken,Task<T?>> readReady,
        CancellationToken token, TimeSpan? timeout = null, TimeSpan? pollInterval = null) where T : class
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout??TimeSpan.FromSeconds(10));
        while(true)
        {
            token.ThrowIfCancellationRequested();
            T? image;
            try { image=await readReady(deadline.Token).ConfigureAwait(false); }
            catch(OperationCanceledException) when(!token.IsCancellationRequested)
            {
                // A native read was still running at the image deadline. Its owner
                // retires the session; do not turn this into permission to refresh.
                throw new TimeoutException("验证码图片读取等待超时，已停止本次登录输入。");
            }
            if(image is not null)return image;
            try { await Task.Delay(pollInterval??TimeSpan.FromMilliseconds(300),deadline.Token).ConfigureAwait(false); }
            catch(OperationCanceledException) when(!token.IsCancellationRequested) { return null; }
        }
    }
}
