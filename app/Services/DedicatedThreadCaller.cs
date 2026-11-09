namespace CpuTempWidget.Services;

public enum CallStatus
{
    Ok,
    /// <summary>The call did not finish within the timeout; it is still running on the worker thread.</summary>
    TimedOut,
    /// <summary>A previous call is still running (it timed out earlier); this one was not started.</summary>
    Busy,
    Faulted,
    Disposed,
}

public readonly record struct CallResult<T>(CallStatus Status, T? Value = default, Exception? Error = null);

/// <summary>
/// Runs a blocking, possibly-hanging call (here: the AMD SDK, which is native and not documented as
/// thread-safe) on ONE dedicated, named thread, with a timeout for the caller.
/// <list type="bullet">
/// <item>All calls happen on the same thread, as in AMD's own sample.</item>
/// <item>A hang never blocks the caller beyond the timeout, and further calls fail fast (<see cref="CallStatus.Busy"/>)
/// instead of piling up threads; once the stuck call returns, calls work again.</item>
/// <item>The thread has a name, so a stack dump (dotnet-stack) shows exactly where it is stuck.</item>
/// </list>
/// </summary>
public sealed class DedicatedThreadCaller<T> : IDisposable
{
    private readonly Func<T> _work;
    private readonly Action? _onExit;
    private readonly Thread _thread;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private TaskCompletionSource<CallResult<T>>? _inFlight;
    private bool _disposed;

    public DedicatedThreadCaller(string threadName, Func<T> work, Action? onExit = null)
    {
        _work = work;
        _onExit = onExit;
        _thread = new Thread(Loop) { IsBackground = true, Name = threadName };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public CallResult<T> Call(TimeSpan timeout)
    {
        TaskCompletionSource<CallResult<T>> tcs;
        lock (_gate)
        {
            if (_disposed) return new CallResult<T>(CallStatus.Disposed);
            if (_inFlight is not null) return new CallResult<T>(CallStatus.Busy);
            tcs = _inFlight = new TaskCompletionSource<CallResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _wake.Release();
        return tcs.Task.Wait(timeout) ? tcs.Task.Result : new CallResult<T>(CallStatus.TimedOut);
    }

    private void Loop()
    {
        try
        {
            while (true)
            {
                _wake.Wait();
                TaskCompletionSource<CallResult<T>>? tcs;
                lock (_gate)
                {
                    if (_disposed) break;
                    tcs = _inFlight;
                }
                if (tcs is null) continue;

                CallResult<T> result;
                try { result = new CallResult<T>(CallStatus.Ok, _work()); }
                catch (Exception ex) { result = new CallResult<T>(CallStatus.Faulted, default, ex); }

                lock (_gate) _inFlight = null;     // only now may a new call start
                tcs.TrySetResult(result);
            }
        }
        finally
        {
            try { _onExit?.Invoke(); } catch { /* shutting down */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _wake.Release();
        // If the native call is hung we give up after 2 s; the thread is a background thread and dies with the process.
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
