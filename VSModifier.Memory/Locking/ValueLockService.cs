using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace VSModifier.Memory.Locking;

public sealed class ValueLockService : IAsyncDisposable
{
    public const string DefaultGuardKey = "__safetyGuard";

    private readonly ConcurrentDictionary<string, Action> _locks = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;
    private readonly TimeSpan _period;
    private readonly bool _stopAllOnFailure;
    private readonly Action? _guard;
    private readonly string _guardKey;
    private volatile bool _faulted;

    /// <summary>
    /// <paramref name="guard"/> 會在每個週期執行所有鎖之前先跑一次，也會在每次 <see cref="Set"/>
    /// 實際寫入之前先跑一次；失敗時本週期不再執行任何鎖。這讓安全檢查的成本與鎖的數量無關，
    /// 並保證防護一定排在同一週期的寫入前面。
    /// </summary>
    public ValueLockService(
        TimeSpan? period = null,
        bool stopAllOnFailure = false,
        Action? guard = null,
        string guardKey = DefaultGuardKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guardKey);
        _period = period ?? TimeSpan.FromMilliseconds(100);
        _stopAllOnFailure = stopAllOnFailure;
        _guard = guard;
        _guardKey = guardKey;
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
        if (_faulted)
        {
            throw new InvalidOperationException("安全防護已停止所有鎖值，拒絕啟用新的鎖。");
        }

        if (RunGuard() is Exception guardFailure)
        {
            ExceptionDispatchInfo.Capture(guardFailure).Throw();
        }

        _locks[key] = enforce;
        try
        {
            enforce();
        }
        catch (Exception exception)
        {
            HandleFailure(key, exception);
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
            if (RunGuard() is not null)
            {
                continue;
            }

            foreach ((string key, Action enforce) in _locks)
            {
                if (_faulted)
                {
                    break;
                }

                try
                {
                    enforce();
                }
                catch (Exception exception)
                {
                    HandleFailure(key, exception);
                }
            }
        }
    }

    private Exception? RunGuard()
    {
        if (_guard is null)
        {
            return null;
        }

        try
        {
            _guard();
            return null;
        }
        catch (Exception exception)
        {
            HandleFailure(_guardKey, exception);
            return exception;
        }
    }

    private void HandleFailure(string key, Exception exception)
    {
        if (_stopAllOnFailure)
        {
            _faulted = true;
            _locks.Clear();
        }
        else
        {
            _locks.TryRemove(key, out _);
        }

        ValueLockFailureEventArgs args = new(key, exception);
        Delegate[] handlers = LockFailed?.GetInvocationList() ?? [];
        foreach (EventHandler<ValueLockFailureEventArgs> handler in handlers.Cast<EventHandler<ValueLockFailureEventArgs>>())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // A consumer notification must never restart writes or terminate the safety worker.
            }
        }
    }
}

public sealed class ValueLockFailureEventArgs(string key, Exception exception) : EventArgs
{
    public string Key { get; } = key;

    public Exception Exception { get; } = exception;
}
