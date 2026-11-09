// RmTemp.cpp - thin C ABI over the AMD Ryzen Master Monitoring SDK (3.1.x).
//
// Mirrors the init sequence of AMD's own sample (RyzenMasterMonitoringSampleApp\src\sampleApp.cpp):
//   registry InstallationPath -> ensure kernel driver -> LoadLibraryEx(Platform.dll) ->
//   GetPlatform().Init() -> GetIDeviceManager().GetDevice(dtCPU, 0) -> ICPUEx::GetCPUParameters().
// AMD's usage note: call GetCPUParameters at most once per second. rm_read() enforces that itself.
#include <windows.h>
#include <string>
#include "GlobalDef.h"
#include "IPlatform.h"
#include "IDeviceManager.h"
#include "ICPUEx.h"

#define RM_API extern "C" __declspec(dllexport)

typedef IPlatform& (__stdcall* GetPlatformFunc)();

namespace
{
    HMODULE   g_hPlatform = nullptr;
    IPlatform* g_platform = nullptr;
    ICPUEx*   g_cpu = nullptr;
    DWORD     g_lastWin32 = 0;
    ULONGLONG g_lastTick = 0;
    double    g_lastTemp = -1.0;
    bool      g_haveSample = false;

    bool ReadInstallPath(std::wstring& out)
    {
        DWORD cb = 0;
        LSTATUS s = RegGetValueW(HKEY_LOCAL_MACHINE, L"Software\\AMD\\RyzenMasterMonitoringSDK",
            L"InstallationPath", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, nullptr, &cb);
        if (s != ERROR_SUCCESS || cb < sizeof(wchar_t)) { g_lastWin32 = (DWORD)s; return false; }
        std::wstring buf(cb / sizeof(wchar_t), L'\0');
        s = RegGetValueW(HKEY_LOCAL_MACHINE, L"Software\\AMD\\RyzenMasterMonitoringSDK",
            L"InstallationPath", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, buf.data(), &cb);
        if (s != ERROR_SUCCESS) { g_lastWin32 = (DWORD)s; return false; }
        buf.resize(wcslen(buf.c_str()));
        if (buf.empty()) return false;
        if (buf.back() != L'\\' && buf.back() != L'/') buf += L'\\';
        out = buf;
        return true;
    }

    bool DriverOpen()
    {
        HANDLE h = CreateFileW(L"\\\\.\\" RM_DRIVER_NAME, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h == INVALID_HANDLE_VALUE) { g_lastWin32 = GetLastError(); return false; }
        CloseHandle(h);
        return true;
    }

    // Same idea as AMD's InstallDriver(): create the kernel-driver service if missing, then start it.
    bool EnsureDriver(const std::wstring& installDir)
    {
        if (DriverOpen()) return true;

        SC_HANDLE scm = OpenSCManagerW(nullptr, nullptr, SC_MANAGER_ALL_ACCESS);
        if (!scm) { g_lastWin32 = GetLastError(); return false; }

        std::wstring sys = installDir + L"bin\\AMDRyzenMasterDriver.sys";
        SC_HANDLE svc = OpenServiceW(scm, RM_DRIVER_NAME, SERVICE_ALL_ACCESS);
        if (!svc)
        {
            svc = CreateServiceW(scm, RM_DRIVER_NAME, RM_DRIVER_NAME, SERVICE_ALL_ACCESS,
                SERVICE_KERNEL_DRIVER, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, sys.c_str(),
                nullptr, nullptr, nullptr, nullptr, nullptr);
        }
        if (!svc) { g_lastWin32 = GetLastError(); CloseServiceHandle(scm); return false; }

        if (!StartServiceW(svc, 0, nullptr))
        {
            DWORD e = GetLastError();
            if (e != ERROR_SERVICE_ALREADY_RUNNING) { g_lastWin32 = e; CloseServiceHandle(svc); CloseServiceHandle(scm); return false; }
        }
        CloseServiceHandle(svc);
        CloseServiceHandle(scm);
        return DriverOpen();
    }
}

// 0 ok | 1 SDK registry key missing | 2 driver unavailable | 3 Platform.dll load failed
// 4 GetPlatform missing | 5 Platform.Init failed | 6 no CPU device
RM_API int rm_init()
{
    if (g_cpu) return 0;
    g_lastWin32 = 0;

    std::wstring dir;
    if (!ReadInstallPath(dir)) return 1;
    if (!EnsureDriver(dir)) return 2;

    // AMD's sample passes only LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR; that works there because the sample exe already
    // has the shared VC runtime loaded. Our DLL links the CRT statically, so Platform.dll/Device.dll's own
    // imports (VCRUNTIME140, MSVCP140, NETAPI32...) must be found in System32 too.
    g_hPlatform = LoadLibraryExW((dir + L"bin\\Platform.dll").c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!g_hPlatform) { g_lastWin32 = GetLastError(); return 3; }

    auto getPlatform = (GetPlatformFunc)GetProcAddress(g_hPlatform, "GetPlatform");
    if (!getPlatform) { FreeLibrary(g_hPlatform); g_hPlatform = nullptr; return 4; }

    IPlatform& platform = getPlatform();
    if (!platform.Init()) { FreeLibrary(g_hPlatform); g_hPlatform = nullptr; return 5; }
    g_platform = &platform;

    g_cpu = (ICPUEx*)platform.GetIDeviceManager().GetDevice(dtCPU, 0);
    if (!g_cpu)
    {
        platform.UnInit(); g_platform = nullptr;
        FreeLibrary(g_hPlatform); g_hPlatform = nullptr;
        return 6;
    }
    return 0;
}

RM_API int rm_last_win32() { return (int)g_lastWin32; }

// Returns 0 and the package temperature in Celsius, or an SDK status code (-1 failure, 4 unsupported)
// / -100 not initialised / -101 SDK reported no valid temperature.
RM_API int rm_read(double* tempC)
{
    if (!g_cpu || !tempC) return -100;

    ULONGLONG now = GetTickCount64();
    if (g_haveSample && now - g_lastTick < 1000)   // AMD: no faster than once per second
    {
        *tempC = g_lastTemp;
        return 0;
    }

    CPUParameters p;
    int rc = g_cpu->GetCPUParameters(p);
    g_lastTick = now;
    if (rc != 0) return rc;
    if (p.dTemperature <= 0.0) return -101;

    g_lastTemp = p.dTemperature;
    g_haveSample = true;
    *tempC = g_lastTemp;
    return 0;
}

RM_API void rm_uninit()
{
    if (g_platform) { g_platform->UnInit(); g_platform = nullptr; }
    g_cpu = nullptr;
    if (g_hPlatform) { FreeLibrary(g_hPlatform); g_hPlatform = nullptr; }
    g_haveSample = false;
}
