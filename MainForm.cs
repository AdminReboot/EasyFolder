using System.Diagnostics;
using Microsoft.Win32;

namespace EasyFolder;

internal sealed class MainForm : Form, IMessageFilter
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "EasyFolder";

    private static readonly (int Rows, int Cols, string Name)[] Layouts =
    {
        (1, 1, "1 khung"),
        (1, 2, "2 khung trái | phải"),
        (2, 1, "2 khung trên / dưới"),
        (1, 3, "3 khung ngang hàng"),
        (2, 2, "4 khung (2 × 2)"),
        (2, 3, "6 khung (2 × 3)"),
    };

    private readonly AppData data;
    private readonly List<PaneControl> panes = new();
    private readonly List<SplitContainer> splits = new();
    private readonly Panel panesHost = new() { Dock = DockStyle.Fill };
    private readonly MenuStrip menu = new();
    private readonly ToolStripMenuItem miSession = new("&Phiên");
    private readonly ToolStripMenuItem miLayout = new("&Bố cục");
    private readonly ToolStripMenuItem miFavorites = new("&Yêu thích");
    private readonly ToolStripStatusLabel lblPath = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel lblFree = new();
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 1500 };
    private readonly string? loadWarning;

    private PaneControl? activePane;
    private bool ready;
    private bool applyingSplits;
    private bool checkingUpdate;

    public MainForm()
    {
        data = Store.Load(out loadWarning);

        Text = "Easy Folder";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* dùng icon mặc định */ }
        MinimumSize = new Size(600, 400);
        RestoreWindowPlacement();

        var status = new StatusStrip();
        status.Items.Add(lblPath);
        status.Items.Add(lblFree);

        BuildMenu();
        Controls.Add(panesHost);
        Controls.Add(status);
        Controls.Add(menu);
        MainMenuStrip = menu;

        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            SaveNow();
        };
        Application.AddMessageFilter(this);
    }

    // ---------- Vòng đời ----------

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        BuildLayout();
        ready = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Cửa sổ giờ mới có kích thước cuối cùng (kể cả khi phóng to) nên đặt lại vách ngăn.
        ApplySplits();
        if (loadWarning != null)
            MessageBox.Show(this, loadWarning, "Easy Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (data.CheckUpdates) _ = CheckForUpdateAsync(true);
    }

    // ---------- Cập nhật ----------

    /// <param name="silent">Kiểm tra tự động lúc mở: không báo gì nếu không có bản mới hoặc lỗi mạng.</param>
    private async Task CheckForUpdateAsync(bool silent)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        try
        {
            if (silent) await Task.Delay(3000);

            Updater.Release? release;
            try
            {
                release = await Updater.CheckAsync();
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show(this, $"Không kiểm tra được bản cập nhật:\n{ex.Message}", "Cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (IsDisposed) return;

            if (release == null)
            {
                if (!silent)
                    MessageBox.Show(this, $"Bạn đang dùng bản mới nhất ({Updater.Current}).", "Cập nhật", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (silent && release.Tag == data.SkippedVersion) return;

            string notes = release.Notes.Length > 600 ? release.Notes[..600] + "…" : release.Notes;
            var answer = MessageBox.Show(this,
                $"Đã có bản {release.Version} (bạn đang dùng {Updater.Current}).\n\n{notes}\n\n" +
                "Cập nhật ngay? Chương trình sẽ tự khởi động lại và mở lại đúng các thư mục đang mở.",
                "Cập nhật Easy Folder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                // Không hỏi lại bản này mỗi lần mở máy; vẫn cập nhật được từ menu Trợ giúp.
                data.SkippedVersion = release.Tag;
                MarkDirty();
                return;
            }

            try
            {
                UseWaitCursor = true;
                await Updater.ApplyAsync(release);
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                if (MessageBox.Show(this, $"Cập nhật thất bại:\n{ex.Message}\n\nMở trang tải về để cập nhật thủ công?", "Cập nhật",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true });
                return;
            }

            data.SkippedVersion = null;
            Updater.StartNewVersion();
            Close();
        }
        finally
        {
            checkingUpdate = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        saveTimer.Stop();
        SaveNow();
        Application.RemoveMessageFilter(this);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        MarkDirty();
    }

    // ---------- Lưu / khôi phục ----------

    private void MarkDirty()
    {
        if (!ready) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private void SaveNow()
    {
        if (!ready) return;
        CaptureCurrent();
        Store.Save(data);
    }

    private void CaptureCurrent()
    {
        var st = data.Current;
        for (int i = 0; i < panes.Count && i < st.Panes.Count; i++) st.Panes[i] = panes[i].CaptureState();

        if (splits.Count == 0) st.Splits = new();
        else if (WindowState != FormWindowState.Minimized) st.Splits = splits.Select(SplitFraction).ToList();

        Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        data.Window = new WindowPlacement
        {
            X = b.X,
            Y = b.Y,
            Width = b.Width,
            Height = b.Height,
            Maximized = WindowState == FormWindowState.Maximized
                        || (WindowState == FormWindowState.Minimized && data.Window?.Maximized == true),
        };
    }

    private void RestoreWindowPlacement()
    {
        var w = data.Window;
        if (w == null || w.Width < 300 || w.Height < 200)
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1400, 850);
            return;
        }

        var bounds = new Rectangle(w.X, w.Y, w.Width, w.Height);
        // Màn hình phụ đã rút ra: đừng mở cửa sổ ở chỗ không nhìn thấy.
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = bounds.Size;
        }
        if (w.Maximized) WindowState = FormWindowState.Maximized;
    }

    // ---------- Bố cục ----------

    private void BuildLayout()
    {
        var st = data.Current;
        Store.SanitizeLayout(st, false);

        panesHost.SuspendLayout();
        foreach (Control old in panesHost.Controls.Cast<Control>().ToList())
        {
            panesHost.Controls.Remove(old);
            old.Dispose();
        }
        panes.Clear();
        splits.Clear();
        activePane = null;

        var rows = new List<Control>();
        for (int r = 0; r < st.Rows; r++)
        {
            var rowPanes = new List<Control>();
            for (int c = 0; c < st.Cols; c++)
            {
                var pane = new PaneControl(st.Panes[r * st.Cols + c]) { Dock = DockStyle.Fill };
                pane.StateChanged += (_, _) =>
                {
                    if (pane == activePane) UpdateStatus();
                    MarkDirty();
                };
                pane.AddFavoriteRequested += AddFavorite;
                panes.Add(pane);
                rowPanes.Add(pane);
            }
            rows.Add(Chain(rowPanes, Orientation.Vertical));
        }
        panesHost.Controls.Add(Chain(rows, Orientation.Horizontal));
        panesHost.ResumeLayout(true);

        ApplySplits();
        SetActivePane(panes[0]);
        foreach (var pane in panes) BeginInvoke(pane.LoadActive);
        UpdateChrome();
    }

    /// <summary>Xếp n ô cạnh nhau bằng các SplitContainer lồng nhau.</summary>
    private Control Chain(List<Control> items, Orientation orientation)
    {
        if (items.Count == 1) return items[0];

        var sc = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = orientation,
            SplitterWidth = 5,
            Panel1MinSize = 60,
            Panel2MinSize = 60,
            Tag = 1.0 / items.Count,
        };
        sc.SplitterMoved += (_, _) =>
        {
            if (!applyingSplits) MarkDirty();
        };
        splits.Add(sc);
        sc.Panel1.Controls.Add(items[0]);
        sc.Panel2.Controls.Add(Chain(items.GetRange(1, items.Count - 1), orientation));
        return sc;
    }

    private static int SplitLength(SplitContainer sc) => sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height;

    private static double SplitFraction(SplitContainer sc)
    {
        int total = SplitLength(sc);
        return total > 0 ? Math.Round((double)sc.SplitterDistance / total, 4) : (double)sc.Tag!;
    }

    private void ApplySplits()
    {
        var saved = data.Current.Splits;
        bool useSaved = saved.Count == splits.Count;
        applyingSplits = true;
        try
        {
            // Hai lượt: vách trong phụ thuộc kích thước do vách ngoài quyết định.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < splits.Count; i++)
                {
                    var sc = splits[i];
                    double fraction = useSaved ? saved[i] : (double)sc.Tag!;
                    int total = SplitLength(sc);
                    int max = total - sc.Panel2MinSize - sc.SplitterWidth;
                    if (max < sc.Panel1MinSize) continue;
                    try { sc.SplitterDistance = Math.Clamp((int)Math.Round(fraction * total), sc.Panel1MinSize, max); }
                    catch { /* cửa sổ quá nhỏ, giữ nguyên */ }
                }
            }
        }
        finally
        {
            applyingSplits = false;
        }
    }

    private void ChangeLayout(int rows, int cols)
    {
        CaptureCurrent();
        data.Current.Rows = rows;
        data.Current.Cols = cols;
        data.Current.Splits = new();
        BuildLayout();
        MarkDirty();
    }

    private void SetActivePane(PaneControl pane)
    {
        if (activePane == pane) return;
        activePane = pane;
        foreach (var p in panes) p.IsActive = p == pane;
        UpdateStatus();
    }

    // ---------- Phiên ----------

    private void SaveSession(bool askName)
    {
        string? name = data.CurrentSession;
        if (askName || name == null)
        {
            name = InputDialog.Ask(this, "Lưu phiên", "Tên phiên:", name ?? "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (data.Sessions.ContainsKey(name) && name != data.CurrentSession &&
                MessageBox.Show(this, $"Phiên \"{name}\" đã có. Ghi đè?", "Lưu phiên",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
        }

        CaptureCurrent();
        data.Sessions[name] = data.Current.Clone();
        data.CurrentSession = name;
        Store.Save(data);
        UpdateChrome();
    }

    private void LoadSession(string name)
    {
        if (!data.Sessions.TryGetValue(name, out var session)) return;
        if (data.CurrentSession == null &&
            MessageBox.Show(this, "Bố cục hiện tại chưa được lưu thành phiên và sẽ bị thay thế. Tiếp tục?", "Mở phiên",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        data.Current = session.Clone();
        data.CurrentSession = name;
        BuildLayout();
        Store.Save(data);
    }

    private void DeleteSession(string name)
    {
        if (MessageBox.Show(this, $"Xoá phiên \"{name}\"?", "Xoá phiên",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        data.Sessions.Remove(name);
        if (data.CurrentSession == name) data.CurrentSession = null;
        Store.Save(data);
        UpdateChrome();
    }

    // ---------- Yêu thích ----------

    private void AddFavorite(string name, string path)
    {
        if (data.Favorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Thư mục này đã có trong Yêu thích.", "Yêu thích", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? chosen = InputDialog.Ask(this, "Thêm vào Yêu thích", $"Tên hiển thị cho:\n{path}", name);
        if (string.IsNullOrWhiteSpace(chosen)) return;
        data.Favorites.Add(new Favorite { Name = chosen.Trim(), Path = path });
        Store.Save(data);
        UpdateChrome();
    }

    private void OpenFavorite(Favorite f)
    {
        if (activePane == null) return;
        if (ModifierKeys.HasFlag(Keys.Control)) activePane.NewTab(f.Path);
        else activePane.Navigate(f.Path);
    }

    // ---------- Menu / thanh trạng thái ----------

    private void BuildMenu()
    {
        var miTab = new ToolStripMenuItem("&Tab");
        miTab.DropDownItems.Add(Item("Tab mới", "Ctrl+T", () => activePane?.NewTab()));
        miTab.DropDownItems.Add(Item("Đóng tab", "Ctrl+W", () => activePane?.CloseActiveTab()));
        miTab.DropDownItems.Add(Item("Tab kế tiếp", "Ctrl+Tab", () => activePane?.CycleTab(1)));
        miTab.DropDownItems.Add(new ToolStripSeparator());
        miTab.DropDownItems.Add(Item("Mở thư mục…", null, () =>
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK) activePane?.Navigate(dlg.SelectedPath);
        }));
        miTab.DropDownItems.Add(Item("Tới ô địa chỉ", "Ctrl+L", () => activePane?.FocusAddress()));

        var miOptions = new ToolStripMenuItem("&Tuỳ chọn");
        var miStartup = new ToolStripMenuItem("Khởi động cùng Windows") { Checked = IsStartupEnabled() };
        miStartup.Click += (_, _) =>
        {
            SetStartup(!miStartup.Checked);
            miStartup.Checked = IsStartupEnabled();
        };
        miOptions.DropDownItems.Add(miStartup);
        var miAutoUpdate = new ToolStripMenuItem("Tự kiểm tra bản cập nhật khi mở") { Checked = data.CheckUpdates, CheckOnClick = true };
        miAutoUpdate.Click += (_, _) =>
        {
            data.CheckUpdates = miAutoUpdate.Checked;
            MarkDirty();
        };
        miOptions.DropDownItems.Add(miAutoUpdate);
        miOptions.DropDownItems.Add(Item("Mở thư mục dữ liệu", null, () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.Dir}\"") { UseShellExecute = true })));

        var miHelp = new ToolStripMenuItem("Trợ &giúp");
        miHelp.DropDownItems.Add(Item("Phím tắt", null, ShowHelp));
        miHelp.DropDownItems.Add(Item("Kiểm tra cập nhật…", null, () => _ = CheckForUpdateAsync(false)));
        miHelp.DropDownItems.Add(new ToolStripMenuItem($"Phiên bản {Updater.Current}") { Enabled = false });

        menu.Items.AddRange(new ToolStripItem[] { miSession, miLayout, miFavorites, miTab, miOptions, miHelp });
    }

    private static ToolStripMenuItem Item(string text, string? shortcut, Action onClick)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>Dựng lại các menu phụ thuộc dữ liệu và tiêu đề cửa sổ.</summary>
    private void UpdateChrome()
    {
        Text = data.CurrentSession == null ? "Easy Folder" : $"Easy Folder — {data.CurrentSession}";
        var names = data.Sessions.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        miSession.DropDownItems.Clear();
        miSession.DropDownItems.Add(Item(
            data.CurrentSession == null ? "Lưu phiên…" : $"Lưu phiên \"{data.CurrentSession}\"", "Ctrl+S", () => SaveSession(false)));
        miSession.DropDownItems.Add(Item("Lưu thành phiên mới…", null, () => SaveSession(true)));
        miSession.DropDownItems.Add(new ToolStripSeparator());
        if (names.Count == 0)
            miSession.DropDownItems.Add(new ToolStripMenuItem("(chưa có phiên nào được lưu)") { Enabled = false });
        foreach (string name in names)
        {
            var item = Item(name, null, () => LoadSession(name));
            item.Checked = name == data.CurrentSession;
            miSession.DropDownItems.Add(item);
        }
        if (names.Count > 0)
        {
            var del = new ToolStripMenuItem("Xoá phiên");
            foreach (string name in names) del.DropDownItems.Add(Item(name, null, () => DeleteSession(name)));
            miSession.DropDownItems.Add(new ToolStripSeparator());
            miSession.DropDownItems.Add(del);
        }
        miSession.DropDownItems.Add(new ToolStripSeparator());
        miSession.DropDownItems.Add(Item("Thoát", "Alt+F4", Close));

        miLayout.DropDownItems.Clear();
        foreach (var (rows, cols, name) in Layouts)
        {
            var item = Item(name, null, () => ChangeLayout(rows, cols));
            item.Checked = rows == data.Current.Rows && cols == data.Current.Cols;
            miLayout.DropDownItems.Add(item);
        }

        miFavorites.DropDownItems.Clear();
        miFavorites.DropDownItems.Add(Item("Thêm thư mục hiện tại…", null, () => activePane?.RequestAddFavorite()));
        if (data.Favorites.Count > 0)
        {
            miFavorites.DropDownItems.Add(new ToolStripSeparator());
            var del = new ToolStripMenuItem("Xoá khỏi Yêu thích");
            foreach (var f in data.Favorites)
            {
                var item = Item(f.Name, null, () => OpenFavorite(f));
                item.ToolTipText = f.Path + "\n(giữ Ctrl để mở trong tab mới)";
                miFavorites.DropDownItems.Add(item);
                del.DropDownItems.Add(Item(f.Name, null, () =>
                {
                    data.Favorites.Remove(f);
                    Store.Save(data);
                    UpdateChrome();
                }));
            }
            miFavorites.DropDownItems.Add(new ToolStripSeparator());
            miFavorites.DropDownItems.Add(del);
        }
    }

    private async void UpdateStatus()
    {
        string path = activePane?.ActiveView?.TargetPath ?? "";
        lblPath.Text = FolderView.IsShellPath(path) ? activePane?.ActiveView?.Title ?? "" : path;
        lblFree.Text = "";
        if (path.Length == 0 || FolderView.IsShellPath(path)) return;

        // Hỏi dung lượng ở luồng nền: ổ chưa sẵn sàng có thể trả lời rất chậm.
        string free = await Task.Run(() =>
        {
            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return "";
                var drive = new DriveInfo(root);
                return drive.IsReady ? $"Trống: {FormatSize(drive.AvailableFreeSpace)} / {FormatSize(drive.TotalSize)}" : "";
            }
            catch
            {
                return "";
            }
        });
        if (!IsDisposed && path == (activePane?.ActiveView?.TargetPath ?? "")) lblFree.Text = free;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Ctrl+T\t\tTab mới (cùng thư mục đang xem)\n" +
            "Ctrl+W\t\tĐóng tab (hoặc bấm chuột giữa lên tab)\n" +
            "Ctrl+Tab\t\tChuyển tab\n" +
            "Alt+← / Alt+→\tQuay lại / tiến tới (cũng dùng được nút bên hông chuột)\n" +
            "Alt+↑\t\tLên thư mục cha\n" +
            "Ctrl+L, Alt+D, F4\tTới ô địa chỉ\n" +
            "Ctrl+S\t\tLưu phiên\n" +
            "F5\t\tLàm mới\n\n" +
            "Vị trí các thư mục đang mở được tự lưu liên tục và khôi phục khi mở lại.\n" +
            "Thư mục chưa truy cập được (ổ Google Drive chưa sẵn sàng…) vẫn được giữ nguyên và tự mở lại khi ổ có mặt.\n\n" +
            $"Dữ liệu lưu tại:\n{Store.DataFile}",
            "Easy Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) != null;
    }

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue(RunValue, $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue(RunValue, false);
    }

    // ---------- Chuột / bàn phím toàn cục ----------

    // Khung Explorer là cửa sổ native nên WinForms không nhận được sự kiện focus/phím của nó;
    // mọi thứ liên quan (khung đang chọn, phím tắt, phím của Explorer) đi qua bộ lọc này.
    public bool PreFilterMessage(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_LBUTTONDOWN:
            case Native.WM_RBUTTONDOWN:
            case Native.WM_MBUTTONDOWN:
                if (PaneFromHwnd(m.HWnd) is { } clicked)
                {
                    SetActivePane(clicked);
                    if (clicked.ActiveView is { } view && view.ContainsHwnd(m.HWnd)) SelectHost(view.Host);
                }
                break;

            case Native.WM_XBUTTONDOWN:
                if (PaneFromHwnd(m.HWnd) is { } pane)
                {
                    SetActivePane(pane);
                    int button = (int)(((long)m.WParam >> 16) & 0xFFFF);
                    if (button == 1) pane.GoBack();
                    else if (button == 2) pane.GoForward();
                    return true;
                }
                break;

            case Native.WM_KEYDOWN:
            case Native.WM_SYSKEYDOWN:
                if (ActiveForm == this && HandleShortcut((Keys)(int)(long)m.WParam | ModifierKeys)) return true;
                break;
        }

        if (m.Msg >= Native.WM_KEYFIRST && m.Msg <= Native.WM_KEYLAST)
        {
            foreach (var pane in panes)
            {
                if (pane.ActiveView is { } view && view.ContainsHwnd(m.HWnd))
                    return view.TranslateAccelerator(ref m);
            }
        }
        return false;
    }

    private PaneControl? PaneFromHwnd(IntPtr hwnd)
    {
        foreach (var pane in panes)
        {
            if (pane.IsHandleCreated && (pane.Handle == hwnd || Native.IsChild(pane.Handle, hwnd))) return pane;
        }
        return null;
    }

    /// <summary>Cho WinForms biết khung Explorer vừa được bấm để nó trả focus về đúng chỗ khi quay lại cửa sổ.</summary>
    private void SelectHost(BrowserHost host)
    {
        Control current = this;
        while (current is ContainerControl { ActiveControl: { } inner }) current = inner;
        if (current != host) host.Select();
    }

    private bool HandleShortcut(Keys keys)
    {
        if (activePane == null) return false;
        switch (keys)
        {
            case Keys.Control | Keys.T: activePane.NewTab(); return true;
            case Keys.Control | Keys.W: activePane.CloseActiveTab(); return true;
            case Keys.Control | Keys.Tab: activePane.CycleTab(1); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: activePane.CycleTab(-1); return true;
            case Keys.Alt | Keys.Left: activePane.GoBack(); return true;
            case Keys.Alt | Keys.Right: activePane.GoForward(); return true;
            case Keys.Alt | Keys.Up: activePane.GoUp(); return true;
            case Keys.Control | Keys.L:
            case Keys.Alt | Keys.D:
            case Keys.F4:
                activePane.FocusAddress();
                return true;
            case Keys.Control | Keys.S: SaveSession(false); return true;
            default: return false;
        }
    }
}

internal static class InputDialog
{
    public static string? Ask(IWin32Window owner, string title, string prompt, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(420, 150),
        };
        var label = new Label { Text = prompt, Location = new Point(12, 12), Size = new Size(396, 50) };
        var box = new TextBox { Text = initial, Location = new Point(12, 68), Width = 396 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(232, 108), Size = new Size(84, 28) };
        var cancel = new Button { Text = "Huỷ", DialogResult = DialogResult.Cancel, Location = new Point(324, 108), Size = new Size(84, 28) };
        form.Controls.AddRange(new Control[] { label, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        box.SelectAll();
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}
