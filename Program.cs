using System.Diagnostics;

namespace EasyFolder;

internal static class Program
{
    private const int AfterUpdateWaitMs = 15000;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--update")) return Updater.RunHeadless();

        // Chỉ cho chạy một bản: hai bản cùng ghi một file dữ liệu sẽ ghi đè lên nhau.
        using var mutex = new Mutex(true, "EasyFolder_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Vừa cập nhật xong: bản cũ đang thoát, chờ nó nhả mutex rồi chạy tiếp.
            if (!args.Contains(Updater.AfterUpdateArg) || !WaitForPreviousInstance(mutex))
            {
                ActivateRunningInstance();
                return 0;
            }
        }

        Updater.CleanupOldFiles();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static bool WaitForPreviousInstance(Mutex mutex)
    {
        try { return mutex.WaitOne(AfterUpdateWaitMs); }
        catch (AbandonedMutexException) { return true; }
    }

    private static void ActivateRunningInstance()
    {
        using var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
            if (Native.IsIconic(p.MainWindowHandle)) Native.ShowWindow(p.MainWindowHandle, Native.SW_RESTORE);
            Native.SetForegroundWindow(p.MainWindowHandle);
            return;
        }
    }
}
