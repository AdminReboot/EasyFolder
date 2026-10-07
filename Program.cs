using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EasyFolder;

internal static class Program
{
    private const int AfterUpdateWaitMs = 15000;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--update")) return Updater.RunHeadless();

        // Chỉ cho chạy một bản trên mỗi file dữ liệu: hai bản cùng ghi một file sẽ ghi đè lên nhau.
        // Bản portable có dữ liệu riêng nên được chạy song song với bản thường.
        string mutexName = Store.IsPortable
            ? "EasyFolder_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Store.Dir.ToLowerInvariant())))[..16]
            : "EasyFolder_SingleInstance";
        using var mutex = new Mutex(true, mutexName, out bool isFirst);
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
