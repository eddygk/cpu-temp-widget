using System.IO;
using System.Runtime.CompilerServices;
using CpuTempWidget.Services;

namespace CpuTempWidget.Tests;

/// <summary>
/// Runs once when the test assembly loads: anything the code under test logs goes to a temp folder, never to the
/// real %APPDATA%\CpuTempWidget\widget.log of whoever runs the tests.
/// </summary>
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Init() =>
        DiagnosticLog.FilePath = Path.Combine(Path.GetTempPath(), "CpuTempWidget.Tests", "widget.log");
}
