using System.Runtime.InteropServices;

namespace EasyFolder;

/// <summary>Cửa sổ cha của khung Explorer nhúng.</summary>
internal sealed class BrowserHost : Control
{
    public event EventHandler? FocusRequested;

    public BrowserHost()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = SystemColors.Window;
    }

    // Không cho WinForms xử lý trước phím (mũi tên, Enter, Tab...) của danh sách file,
    // nếu không các phím này bị nuốt thành phím điều hướng hộp thoại.
    public override bool PreProcessMessage(ref Message msg) => false;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        FocusRequested?.Invoke(this, e);
    }
}

/// <summary>
/// Một tab thư mục. <see cref="TargetPath"/> là đường dẫn người dùng muốn mở và chỉ đổi khi
/// điều hướng thành công hoặc người dùng tự nhập. Nếu thư mục chưa truy cập được (ví dụ ổ
/// Google Drive chưa mount lúc mới bật máy), tab hiện màn hình chờ và tự thử lại chứ không
/// bao giờ thay đường dẫn đã lưu bằng thư mục khác.
/// </summary>
internal sealed class FolderView : UserControl
{
    private const int RetryIntervalMs = 3000;
    private const int ExistsTimeoutMs = 4000;

    private readonly BrowserHost host = new() { Dock = DockStyle.Fill };
    private readonly Panel waitPanel = new() { Dock = DockStyle.Fill, Visible = false, BackColor = SystemColors.Window };
    private readonly Label waitLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    private readonly System.Windows.Forms.Timer retryTimer = new() { Interval = RetryIntervalMs };

    private IExplorerBrowser? browser;
    private IInputObject? inputObject;
    private Sink? sink;
    private uint cookie;
    private bool loaded;
    private bool focusAfterNav;
    private string? lastGoodPath;
    private Task<bool>? existsCheck;

    public string TargetPath { get; private set; }
    public string Title { get; private set; }
    public bool IsWaiting => waitPanel.Visible;
    public BrowserHost Host => host;

    /// <summary>Đường dẫn, tiêu đề hoặc trạng thái chờ vừa thay đổi.</summary>
    public event EventHandler? Changed;

    public FolderView(string path, string? title)
    {
        TargetPath = path;
        Title = string.IsNullOrWhiteSpace(title) ? TitleFromPath(path) : title;

        var retryNow = new Button { Text = "Thử lại ngay", AutoSize = true };
        var pickOther = new Button { Text = "Chọn thư mục khác…", AutoSize = true };
        retryNow.Click += (_, _) => _ = TryNavigateAsync();
        pickOther.Click += (_, _) => PickOtherFolder();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
        buttons.Controls.Add(retryNow);
        buttons.Controls.Add(pickOther);
        waitPanel.Controls.Add(waitLabel);
        waitPanel.Controls.Add(buttons);

        Controls.Add(waitPanel);
        Controls.Add(host);

        host.Resize += (_, _) => UpdateBrowserRect();
        host.FocusRequested += (_, _) =>
        {
            if (IsHandleCreated) BeginInvoke(FocusView);
        };
        retryTimer.Tick += (_, _) => _ = TryNavigateAsync();
    }

    public static bool IsShellPath(string path) =>
        path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    public static string TitleFromPath(string path)
    {
        if (IsShellPath(path))
        {
            if (Native.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0)
            {
                try { return Native.GetName(pidl, Native.SIGDN_NORMALDISPLAY) ?? path; }
                finally { Native.ILFree(pidl); }
            }
            return path;
        }
        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Tải thư mục lần đầu khi tab được hiển thị (tab chưa xem thì chưa tốn tài nguyên).</summary>
    public void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        _ = TryNavigateAsync();
    }

    public void Navigate(string path, bool focus = false)
    {
        TargetPath = path;
        Title = TitleFromPath(path);
        loaded = true;
        focusAfterNav = focus;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = TryNavigateAsync();
    }

    public void GoBack()
    {
        if (IsWaiting)
        {
            if (lastGoodPath != null) Navigate(lastGoodPath);
            return;
        }
        browser?.BrowseToIDList(IntPtr.Zero, Native.SBSP_NAVIGATEBACK);
    }

    public void GoForward()
    {
        if (!IsWaiting) browser?.BrowseToIDList(IntPtr.Zero, Native.SBSP_NAVIGATEFORWARD);
    }

    public void GoUp()
    {
        if (!IsWaiting && browser != null)
        {
            browser.BrowseToIDList(IntPtr.Zero, Native.SBSP_PARENT);
            return;
        }
        if (IsShellPath(TargetPath)) return;
        string? parent = Path.GetDirectoryName(TargetPath.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(parent)) Navigate(parent);
    }

    public void RefreshView()
    {
        if (IsWaiting || !loaded)
        {
            loaded = true;
            _ = TryNavigateAsync();
            return;
        }
        GetShellView()?.Refresh();
    }

    public void FocusView()
    {
        if (IsDisposed || IsWaiting) return;
        GetShellView()?.UIActivate(Native.SVUIA_ACTIVATE_FOCUS);
    }

    public bool ContainsHwnd(IntPtr hwnd) =>
        host.IsHandleCreated && (host.Handle == hwnd || Native.IsChild(host.Handle, hwnd));

    /// <summary>Chuyển phím tắt cho khung Explorer (F2, Delete, Ctrl+C, Backspace...).</summary>
    public bool TranslateAccelerator(ref Message m)
    {
        if (inputObject == null) return false;
        var msg = new Native.MSG
        {
            hwnd = m.HWnd,
            message = (uint)m.Msg,
            wParam = m.WParam,
            lParam = m.LParam,
            time = (uint)Environment.TickCount,
        };
        return inputObject.TranslateAcceleratorIO(ref msg) == 0;
    }

    private async Task TryNavigateAsync()
    {
        if (IsDisposed) return;
        string path = TargetPath;

        if (!IsShellPath(path))
        {
            // Lần kiểm tra trước còn treo (ổ mạng/ổ ảo chưa phản hồi): chờ lượt thử sau.
            if (existsCheck is { IsCompleted: false })
            {
                ShowWaiting();
                return;
            }

            // Directory.Exists có thể treo lâu với ổ chưa sẵn sàng nên chạy nền, có giới hạn thời gian.
            var check = existsCheck = Task.Run(() => Directory.Exists(path));
            await Task.WhenAny(check, Task.Delay(ExistsTimeoutMs));
            if (IsDisposed) return;

            if (path != TargetPath)
            {
                _ = TryNavigateAsync();
                return;
            }
            if (!check.IsCompletedSuccessfully || !check.Result)
            {
                ShowWaiting();
                return;
            }
        }

        if (!Browse(path)) ShowWaiting();
    }

    private bool Browse(string path)
    {
        try
        {
            EnsureBrowser();
            if (Native.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _) != 0) return false;
            int hr;
            try { hr = browser!.BrowseToIDList(pidl, Native.SBSP_ABSOLUTE); }
            finally { Native.ILFree(pidl); }
            if (hr < 0) return false;
            ShowBrowser();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureBrowser()
    {
        if (browser != null) return;

        var created = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_ExplorerBrowser, true)!)!;
        var rc = ClientRect();
        var fs = new Native.FOLDERSETTINGS
        {
            ViewMode = Native.FVM_DETAILS,
            fFlags = Native.FWF_AUTOARRANGE | Native.FWF_NOWEBVIEW,
        };
        Marshal.ThrowExceptionForHR(created.Initialize(host.Handle, ref rc, ref fs));
        created.SetOptions(Native.EBO_NOBORDER);
        // Cho Explorer nhớ kiểu xem/cột theo từng thư mục, tách riêng khỏi File Explorer.
        created.SetPropertyBag("EasyFolder");
        sink = new Sink(this);
        created.Advise(sink, out cookie);

        browser = created;
        inputObject = created as IInputObject;
    }

    private Native.RECT ClientRect() => new() { Left = 0, Top = 0, Right = host.ClientSize.Width, Bottom = host.ClientSize.Height };

    private void UpdateBrowserRect()
    {
        if (browser == null) return;
        IntPtr hdwp = IntPtr.Zero;
        browser.SetRect(ref hdwp, ClientRect());
    }

    private IShellView? GetShellView()
    {
        if (browser == null) return null;
        Guid iid = Native.IID_IShellView;
        return browser.GetCurrentView(ref iid, out object? view) == 0 ? view as IShellView : null;
    }

    private void ShowWaiting()
    {
        waitLabel.Text =
            $"Chưa truy cập được thư mục:\n\n{TargetPath}\n\n" +
            "Đường dẫn vẫn được giữ nguyên trong phiên.\n" +
            "Đang tự thử lại, thư mục sẽ mở ngay khi ổ đĩa sẵn sàng…";
        bool wasWaiting = waitPanel.Visible;
        waitPanel.Visible = true;
        waitPanel.BringToFront();
        retryTimer.Start();
        if (!wasWaiting) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ShowBrowser()
    {
        retryTimer.Stop();
        if (!waitPanel.Visible) return;
        waitPanel.Visible = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PickOtherFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Chọn thư mục để mở trong tab này", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) Navigate(dlg.SelectedPath, true);
    }

    private void OnNavigationComplete(IntPtr pidl)
    {
        string? path = Native.GetName(pidl, Native.SIGDN_DESKTOPABSOLUTEPARSING);
        if (string.IsNullOrEmpty(path)) return;

        TargetPath = path;
        lastGoodPath = path;
        Title = Native.GetName(pidl, Native.SIGDN_NORMALDISPLAY) ?? TitleFromPath(path);
        ShowBrowser();

        // Chuyển focus trước khi báo Changed để ô địa chỉ (đang hết focus) nhận đường dẫn mới.
        if (focusAfterNav)
        {
            focusAfterNav = false;
            FocusView();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            retryTimer.Dispose();
            if (browser != null)
            {
                try
                {
                    if (cookie != 0) browser.Unadvise(cookie);
                    browser.Destroy();
                    Marshal.ReleaseComObject(browser);
                }
                catch { /* đang đóng, bỏ qua lỗi COM */ }
                browser = null;
                inputObject = null;
            }
        }
        base.Dispose(disposing);
    }

    [ComVisible(true)]
    private sealed class Sink : IExplorerBrowserEvents
    {
        private readonly FolderView owner;
        public Sink(FolderView owner) => this.owner = owner;

        public int OnNavigationPending(IntPtr pidlFolder) => 0;
        public int OnViewCreated(object psv) => 0;
        public int OnNavigationFailed(IntPtr pidlFolder) => 0;

        public int OnNavigationComplete(IntPtr pidlFolder)
        {
            if (!owner.IsDisposed) owner.OnNavigationComplete(pidlFolder);
            return 0;
        }
    }
}
