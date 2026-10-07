using System.Runtime.InteropServices;

namespace EasyFolder;

/// <summary>Khai báo Win32 / COM cần để nhúng khung Explorer (IExplorerBrowser).</summary>
internal static class Native
{
    public const int WM_KEYFIRST = 0x0100;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_KEYLAST = 0x0109;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_XBUTTONDOWN = 0x020B;

    public const int SW_RESTORE = 9;

    public const uint SBSP_ABSOLUTE = 0x0000;
    public const uint SBSP_PARENT = 0x2000;
    public const uint SBSP_NAVIGATEBACK = 0x4000;
    public const uint SBSP_NAVIGATEFORWARD = 0x8000;

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    public const uint FVM_ICON = 1;
    public const uint FVM_SMALLICON = 2;
    public const uint FVM_LIST = 3;
    public const uint FVM_DETAILS = 4;
    public const uint FVM_TILE = 6;
    public const uint FVM_CONTENT = 8;
    // SVSI_EDIT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED
    public const uint SVSI_SELECT_AND_RENAME = 0x3 | 0x4 | 0x8 | 0x10;
    public const uint FWF_AUTOARRANGE = 0x00000001;
    public const uint FWF_NOWEBVIEW = 0x00010000;
    public const uint EBO_NOBORDER = 0x00000040;
    public const uint SVUIA_ACTIVATE_FOCUS = 2;

    public static readonly Guid CLSID_ExplorerBrowser = new("71f96385-ddd6-48d3-a0c1-ae06e8b055fb");
    public static readonly Guid IID_IShellView = new("000214E3-0000-0000-C000-000000000046");
    public static readonly Guid IID_IFolderView = new("cde725b0-ccc9-4519-917e-325d72fab4ce");

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FOLDERSETTINGS
    {
        public uint ViewMode;
        public uint fFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, out IntPtr ppszName);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    /// <summary>Trỏ tới phần tử cuối (tên item) bên trong pidl; không cấp phát, đừng giải phóng riêng.</summary>
    [DllImport("shell32.dll")]
    public static extern IntPtr ILFindLastID(IntPtr pidl);

    [DllImport("user32.dll")]
    public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    public static string? GetName(IntPtr pidl, uint sigdn)
    {
        if (SHGetNameFromIDList(pidl, sigdn, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }
}

[ComImport, Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowser
{
    [PreserveSig] int Initialize(IntPtr hwndParent, [In] ref Native.RECT prc, [In] ref Native.FOLDERSETTINGS pfs);
    [PreserveSig] int Destroy();
    [PreserveSig] int SetRect([In, Out] ref IntPtr phdwp, Native.RECT rcBrowser);
    [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
    [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
    [PreserveSig] int SetFolderSettings([In] ref Native.FOLDERSETTINGS pfs);
    [PreserveSig] int Advise([MarshalAs(UnmanagedType.Interface)] IExplorerBrowserEvents psbe, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOptions(uint dwFlag);
    [PreserveSig] int GetOptions(out uint pdwFlag);
    [PreserveSig] int BrowseToIDList(IntPtr pidl, uint uFlags);
    [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
    [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint dwFlags);
    [PreserveSig] int RemoveAll();
    [PreserveSig] int GetCurrentView(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[ComImport, Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
    [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object psv);
    [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
    [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
}

[ComImport, Guid("68284faa-6a48-11d0-8c78-00c04fd918b4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInputObject
{
    [PreserveSig] int UIActivateIO(int fActivate, ref Native.MSG pMsg);
    [PreserveSig] int HasFocusIO();
    [PreserveSig] int TranslateAcceleratorIO(ref Native.MSG pMsg);
}

// Khai báo tới SelectItem. Các hàm "Unused" chỉ để giữ đúng thứ tự vtable, không được gọi.
[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
    [PreserveSig] int TranslateAccelerator(ref Native.MSG pmsg);
    [PreserveSig] int EnableModeless(int fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
    [PreserveSig] int UnusedCreateViewWindow();
    [PreserveSig] int UnusedDestroyViewWindow();
    [PreserveSig] int UnusedGetCurrentInfo();
    [PreserveSig] int UnusedAddPropertySheetPages();
    [PreserveSig] int UnusedSaveViewState();
    [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
}

// Chỉ khai báo hai hàm đầu; các hàm phía sau trong vtable không dùng tới.
[ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView
{
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint viewMode);
}
