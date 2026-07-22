using System.Collections.Concurrent;

namespace VSModifier.Memory.Locking;

public sealed class ValueLockService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Action> _locks = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private readonly TimeSpan _period;

    public ValueLockService(TimeSpan? period = null)
    {
        _period = period ?? TimeSpan.FromMilliseconds(100);
        if (_period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        _worker = Task.Run(RunAsync);
    }

    public event EventHandler<ValueLockFailureEventArgs>? LockFailed;

    public IReadOnlyCollection<string> ActiveLocks => _locks.Keys.ToArray();

    public void Set(string key, Action enforce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(enforce);
        _locks[key] = enforce;
        try
        {
            enforce();
        }
        catch
        {
            _locks.TryRemove(key, out _);
            throw;
        }
    }

    public bool Remove(string key) => _locks.TryRemove(key, out _);

    public void Clear() => _locks.Clear();

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(_period);
        while (await timer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
        {
            foreach ((string key, Action enforce) in _locks.ToArray())
            {
                try
                {
                    enforce();
                }
                catch (Exception exception)
                {
                    _locks.TryRemove(key, out _);
                    LockFailed?.Invoke(this, new ValueLockFailureEventArgs(key, exception));
                }
            }
        }
    }
}

public sealed class ValueLockFailureEventArgs(string key, Exception exception) : EventArgs
{
    public string Key { get; } = key;

    public Exception Exception { get; } = exception;
}
