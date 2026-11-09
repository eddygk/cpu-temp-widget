using System.Runtime.InteropServices;

namespace CpuTempWidget;

/// <summary>P/Invoke surface of native\RmTemp.dll (see RmTemp.cpp for return codes).</summary>
internal static class Native
{
    [DllImport("RmTemp.dll")] internal static extern int rm_init();
    [DllImport("RmTemp.dll")] internal static extern int rm_last_win32();
    [DllImport("RmTemp.dll")] internal static extern int rm_read(out double tempC);
    [DllImport("RmTemp.dll")] internal static extern void rm_uninit();

    internal static string DescribeInit(int code) => code switch
    {
        1 => "AMD Ryzen Master Monitoring SDK not found (registry key missing)",
        2 => "AMD driver unavailable (run as administrator; check HVCI / driver service)",
        3 => "Platform.dll failed to load",
        4 => "Platform.dll has no GetPlatform export",
        5 => "AMD platform init failed (unsupported CPU?)",
        6 => "No CPU device found",
        _ => $"init error {code}",
    };
}
