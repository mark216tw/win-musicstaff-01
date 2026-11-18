using System.Collections.Concurrent;

namespace Com.Example.MusicStaffViewer.Rendering;

internal sealed class RenderWorker : IAsyncDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public RenderWorker()
    {
        _thread = new Thread(() =>
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        })
        { IsBackground = true, Name = "AlphaTab renderer" };
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, cancellationToken);
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        _queue.CompleteAdding();
        await Task.Run(_thread.Join).ConfigureAwait(false);
        _queue.Dispose();
    }
}
