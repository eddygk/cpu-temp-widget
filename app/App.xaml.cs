using System.Windows;
using CpuTempWidget.Services;
using CpuTempWidget.ViewModels;

namespace CpuTempWidget;

/// <summary>Composition root: wires the SDK-backed source and settings store into the view model.</summary>
public partial class App : Application
{
    private Mutex? _single;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One widget only (the logon task and a manual launch must not stack two overlays).
        _single = new Mutex(true, @"Global\CpuTempWidget.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _viewModel = new MainViewModel(new RyzenMasterTemperatureSource(), new JsonSettingsStore());
        new MainWindow(_viewModel).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();   // stops polling and releases the AMD platform
        _single?.Dispose();
        base.OnExit(e);
    }
}
