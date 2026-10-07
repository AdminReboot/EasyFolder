using System.Diagnostics;

namespace EasyFolder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Chỉ cho chạy một bản: hai bản cùng ghi một file dữ liệu sẽ ghi đè lên nhau.
        using var mutex = new Mutex(true, "EasyFolder_SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            ActivateRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
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
