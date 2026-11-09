using System.Windows;
using System.Windows.Input;
using CpuTempWidget.ViewModels;

namespace CpuTempWidget;

/// <summary>View-only code-behind: window chrome concerns (dragging, remembering where it sits).</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.Manual;
        if (viewModel.WindowLeft is double l && viewModel.WindowTop is double t)
        {
            // Keep a restored position fully on screen (the card got wider, or monitors changed).
            Left = Math.Clamp(l, SystemParameters.VirtualScreenLeft,
                Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(t, SystemParameters.VirtualScreenTop,
                Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 24;
            Top = 24;
        }

        Loaded += (_, _) => _viewModel.Start();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DragMove();   // blocks until the mouse is released
        _viewModel.SavePosition(Left, Top);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _viewModel.SavePosition(Left, Top);
}
