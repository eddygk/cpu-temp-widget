using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CpuTempWidget.Models;
using CpuTempWidget.Services;

namespace CpuTempWidget.ViewModels;

/// <summary>
/// All widget state and behaviour; the window only displays it. Polling runs on a PeriodicTimer started
/// from the UI thread: each tick awaits the blocking SDK read on a worker thread and resumes on the UI
/// context, so every property change (and the bound UI update) happens on the UI thread.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>AMD: call GetCPUParameters no faster than once per second.</summary>
    public const int MinIntervalMs = 1000;

    private readonly ITemperatureSource _source;
    private readonly ISettingsStore _store;
    private readonly Settings _settings;
    private readonly TemperatureHistory _history = new(TimeSpan.FromMinutes(5));
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;

    public MainViewModel(ITemperatureSource source, ISettingsStore store)
    {
        _source = source;
        _store = store;
        _settings = store.Load();
        _intervalMs = Math.Max(MinIntervalMs, _settings.IntervalMs);
        _topmost = _settings.Topmost;
    }

    [ObservableProperty]
    private double? _currentTemp;

    [ObservableProperty]
    private string _statusText = "Starting...";

    [ObservableProperty]
    private IReadOnlyList<TemperatureSample> _samples = [];

    [ObservableProperty]
    private double? _minTemp;

    [ObservableProperty]
    private double? _maxTemp;

    [ObservableProperty]
    private bool _topmost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRate1), nameof(IsRate2), nameof(IsRate5))]
    private int _intervalMs;

    public bool IsRate1 => IntervalMs == 1000;
    public bool IsRate2 => IntervalMs == 2000;
    public bool IsRate5 => IntervalMs == 5000;

    /// <summary>Saved window position (view concern, but persisted with the rest of the settings).</summary>
    public double? WindowLeft => _settings.Left;
    public double? WindowTop => _settings.Top;

    partial void OnTopmostChanged(bool value)
    {
        _settings.Topmost = value;
        _store.Save(_settings);
    }

    partial void OnIntervalMsChanged(int value)
    {
        _settings.IntervalMs = value;
        _store.Save(_settings);
        try { if (_timer is not null) _timer.Period = TimeSpan.FromMilliseconds(value); }
        catch (ObjectDisposedException) { /* loop already stopped */ }
    }

    [RelayCommand]
    private void SetInterval(int milliseconds) => IntervalMs = Math.Max(MinIntervalMs, milliseconds);

    [RelayCommand]
    private void ToggleTopmost() => Topmost = !Topmost;

    public void SavePosition(double left, double top)
    {
        _settings.Left = left;
        _settings.Top = top;
        _store.Save(_settings);
    }

    /// <summary>Starts polling. Call once, from the UI thread.</summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await RefreshSafelyAsync(ct);                 // first reading right away
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IntervalMs));
            _timer = timer;
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshSafelyAsync(ct);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally
        {
            _timer = null;
            DiagnosticLog.Info("poll loop ended");
        }
    }

    /// <summary>
    /// This loop is fire-and-forget, so an exception escaping it would end polling silently and leave the last
    /// value on screen looking live. Anything unexpected is logged, shown as "--", and polling carries on.
    /// </summary>
    private async Task RefreshSafelyAsync(CancellationToken ct)
    {
        try
        {
            await RefreshAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("poll tick failed", ex);
            CurrentTemp = null;
            StatusText = $"internal error: {ex.Message}";
        }
    }

    /// <summary>One poll: read off the UI thread, then publish on it. Public so tests can drive it.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        TemperatureReading reading = await Task.Run(_source.Read, ct);
        TimeSpan at = _clock.Elapsed;

        if (reading.Ok)
        {
            _history.Add(new TemperatureSample(at, reading.Celsius));
            CurrentTemp = reading.Celsius;
            StatusText = $"CPU package temperature: {reading.Celsius:0.0} °C";
        }
        else
        {
            _history.Add(new TemperatureSample(at, double.NaN));
            CurrentTemp = null;
            StatusText = reading.Error ?? "temperature unavailable";
        }

        Samples = _history.Snapshot();
        MinTemp = _history.Min;
        MaxTemp = _history.Max;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _source.Dispose();   // waits (briefly) for any in-flight read via the source's own lock
    }
}
