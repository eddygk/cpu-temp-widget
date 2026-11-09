using System.Diagnostics;

namespace CpuTempWidget.Services;

/// <summary>
/// Reads the CPU package temperature through native\RmTemp.dll (a C wrapper over the AMD Ryzen Master
/// Monitoring SDK). Every SDK call runs on one dedicated thread ("AMD SDK reader") with a timeout, because a
/// native call into the SDK can hang indefinitely (observed under load): the widget then shows "--" and a
/// reason instead of a frozen, stale number. Init is retried every 10 s while the SDK is unavailable.
/// State changes are written to <see cref="DiagnosticLog"/>.
/// </summary>
public sealed class RyzenMasterTemperatureSource : ITemperatureSource
{
    /// <summary>A healthy read takes milliseconds; anything past this is treated as a hang.</summary>
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly DedicatedThreadCaller<TemperatureReading> _caller;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _stuckSinceMs = -1;
    private string? _lastLoggedError;

    // Touched only on the SDK thread:
    private bool _inited;
    private DateTime _nextInitAttempt = DateTime.MinValue;
    private long _reads;
    private long _lastHeartbeatMs;

    public RyzenMasterTemperatureSource()
    {
        DiagnosticLog.Info($"widget started (pid {Environment.ProcessId}); SDK read timeout {ReadTimeout.TotalSeconds:0} s");
        _caller = new DedicatedThreadCaller<TemperatureReading>(
            "AMD SDK reader", ReadCore, onExit: () => { if (_inited) Native.rm_uninit(); });
    }

    public TemperatureReading Read()
    {
        CallResult<TemperatureReading> r = _caller.Call(ReadTimeout);
        TemperatureReading reading = r.Status switch
        {
            CallStatus.Ok => r.Value,
            CallStatus.TimedOut => TemperatureReading.Failure($"AMD SDK not responding (read took over {ReadTimeout.TotalSeconds:0} s)"),
            CallStatus.Busy => TemperatureReading.Failure("AMD SDK not responding (previous read still running)"),
            CallStatus.Faulted => TemperatureReading.Failure($"SDK call failed: {r.Error?.Message}"),
            _ => TemperatureReading.Failure("shutting down"),
        };

        TrackStuck(r.Status);
        TrackErrors(reading);
        return reading;
    }

    private void TrackStuck(CallStatus status)
    {
        bool stuck = status is CallStatus.TimedOut or CallStatus.Busy;
        if (stuck && _stuckSinceMs < 0)
        {
            _stuckSinceMs = _clock.ElapsedMilliseconds;
            DiagnosticLog.Warn($"AMD SDK read stuck (no result after {ReadTimeout.TotalSeconds:0} s); further reads are skipped until it returns");
        }
        else if (status == CallStatus.Ok && _stuckSinceMs >= 0)
        {
            DiagnosticLog.Info($"AMD SDK read returned again after about {(_clock.ElapsedMilliseconds - _stuckSinceMs) / 1000.0:0.0} s");
            _stuckSinceMs = -1;
        }
    }

    private void TrackErrors(TemperatureReading reading)
    {
        string? error = reading.Ok ? null : reading.Error;
        if (error == _lastLoggedError) return;     // log changes only: no spam while an error persists
        if (error is not null) DiagnosticLog.Warn($"read failed: {error}");
        else DiagnosticLog.Info("reads OK");
        _lastLoggedError = error;
    }

    /// <summary>Runs on the SDK thread only.</summary>
    private TemperatureReading ReadCore()
    {
        try
        {
            if (!_inited)
            {
                if (DateTime.UtcNow < _nextInitAttempt)
                    return TemperatureReading.Failure("waiting to retry SDK init");

                int r = Native.rm_init();
                if (r != 0)
                {
                    _nextInitAttempt = DateTime.UtcNow.AddSeconds(10);
                    return TemperatureReading.Failure($"{Native.DescribeInit(r)} (win32 {Native.rm_last_win32()})");
                }
                _inited = true;
                DiagnosticLog.Info("AMD platform initialised");
            }

            int rc = Native.rm_read(out double celsius);
            if (rc != 0) return TemperatureReading.Failure($"GetCPUParameters returned {rc}");

            _reads++;
            long now = _clock.ElapsedMilliseconds;
            if (now - _lastHeartbeatMs >= 60_000)
            {
                _lastHeartbeatMs = now;
                DiagnosticLog.Info($"heartbeat: {_reads} reads so far, last {celsius:0.0} C");
            }
            return TemperatureReading.Success(celsius);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return TemperatureReading.Failure($"RmTemp.dll problem: {ex.Message}");
        }
    }

    public void Dispose()
    {
        DiagnosticLog.Info("widget stopping");
        _caller.Dispose();
    }
}
