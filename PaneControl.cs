using System.Diagnostics;

namespace EasyFolder;

internal sealed class TabStrip : TabControl
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    // Tab control gốc coi khoảng trống cạnh các tab là "trong suốt" nên chuột bấm vào đó
    // rơi xuống cửa sổ cha. Nhận lại để bắt được bấm đúp mở tab mới.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && (int)(long)m.Result == HTTRANSPARENT) m.Result = (IntPtr)HTCLIENT;
    }
}

/// <summary>Một khung: thanh điều hướng + ô địa chỉ + các tab thư mục.</summary>
internal sealed class PaneControl : UserControl
{
    // Bề rộng tab (px ở 100%): rộng tối đa khi còn chỗ, co dần khi nhiều tab như trình duyệt.
    private const int MaxTabWidth = 240;
    private const int MinTabWidth = 80;
    private const int StripSpare = 48;
    private static readonly Color ActiveBar = Color.FromArgb(204, 228, 247);
    private static readonly Color ActiveAccent = Color.FromArgb(255, 140, 0);

    private readonly TableLayoutPanel bar = new() { Dock = DockStyle.Top, RowCount = 1 };
    private readonly TextBox address = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private static readonly (string Name, uint Mode)[] ViewModes =
    {
        ("Chi tiết", Native.FVM_DETAILS),
        ("Danh sách", Native.FVM_LIST),
        ("Biểu tượng", Native.FVM_ICON),
        ("Biểu tượng nhỏ", Native.FVM_SMALLICON),
        ("Ô xếp", Native.FVM_TILE),
        ("Nội dung", Native.FVM_CONTENT),
    };

    private readonly TabStrip tabs = new() { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, ShowToolTips = true };
    private readonly ContextMenuStrip tabMenu = new();
    private readonly Font glyphFont = new("Segoe MDL2 Assets", 10f);
    private readonly Font closeFont = new("Segoe UI", 7f);
    private bool active;
    private bool updatingTabWidth;
    private long lastStripClickTick;
    private Point lastStripClickPoint;

    public event EventHandler? StateChanged;
    public event Action<string, string>? AddFavoriteRequested;

    public PaneControl(PaneState state)
    {
        BuildBar();
        BuildTabMenu();

        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.DrawItem += DrawTab;
        tabs.MouseUp += TabsMouseUp;
        tabs.MouseDown += TabsMouseDown;
        tabs.Resize += (_, _) => UpdateTabWidth();
        // Bấm đúp mặc định chỉ chọn một từ; ở đây cần cả đường dẫn để sao chép.
        address.MouseDoubleClick += (_, _) => BeginInvoke(address.SelectAll);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (IsHandleCreated) BeginInvoke(LoadActive);
            StateChanged?.Invoke(this, EventArgs.Empty);
        };

        Controls.Add(tabs);
        Controls.Add(bar);

        foreach (var t in state.Tabs) AddTab(t.Path, t.Title, false);
        tabs.SelectedIndex = Math.Clamp(state.Active, 0, tabs.TabCount - 1);
    }

    public FolderView? ActiveView => tabs.SelectedTab?.Tag as FolderView;

    public bool IsActive
    {
        get => active;
        set
        {
            active = value;
            bar.BackColor = value ? ActiveBar : SystemColors.Control;
            tabs.Invalidate();
        }
    }

    public PaneState CaptureState()
    {
        var st = new PaneState { Active = Math.Max(0, tabs.SelectedIndex) };
        foreach (TabPage page in tabs.TabPages)
        {
            var view = (FolderView)page.Tag!;
            st.Tabs.Add(new TabState { Path = view.TargetPath, Title = view.Title });
        }
        return st;
    }

    /// <summary>Tải thư mục của tab đang chọn và cập nhật ô địa chỉ.</summary>
    public void LoadActive()
    {
        if (IsDisposed) return;
        ActiveView?.EnsureLoaded();
        UpdateAddress();
    }

    public void NewTab(string? path = null)
    {
        var current = ActiveView;
        AddTab(path ?? current?.TargetPath ?? Store.ThisPC, path == null ? current?.Title : null, true);
    }

    public void CloseActiveTab() => CloseTab(tabs.SelectedIndex);

    public void CycleTab(int step)
    {
        if (tabs.TabCount < 2) return;
        tabs.SelectedIndex = (tabs.SelectedIndex + step + tabs.TabCount) % tabs.TabCount;
    }

    public void Navigate(string path) => ActiveView?.Navigate(path, true);
    public void GoBack() => ActiveView?.GoBack();
    public void GoForward() => ActiveView?.GoForward();
    public void GoUp() => ActiveView?.GoUp();

    public void FocusAddress()
    {
        address.Focus();
        address.SelectAll();
    }

    public void RequestAddFavorite()
    {
        if (ActiveView is { } v) AddFavoriteRequested?.Invoke(v.Title, v.TargetPath);
    }

    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    private void BuildBar()
    {
        bar.Height = S(32);
        bar.ColumnCount = 10;
        for (int i = 0; i < 4; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tips = new ToolTip();
        Button Glyph(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                Text = glyph,
                Font = glyphFont,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(S(30), S(26)),
                Margin = new Padding(S(1), S(3), S(1), S(3)),
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (_, _) => onClick();
            tips.SetToolTip(b, tip);
            return b;
        }

        bar.Controls.Add(Glyph("", "Quay lại (Alt+←)", GoBack), 0, 0);
        bar.Controls.Add(Glyph("", "Tiến tới (Alt+→)", GoForward), 1, 0);
        bar.Controls.Add(Glyph("", "Lên thư mục cha (Alt+↑)", GoUp), 2, 0);
        bar.Controls.Add(Glyph("", "Làm mới (F5)", () => ActiveView?.RefreshView()), 3, 0);

        address.Margin = new Padding(S(4), S(4), S(4), S(4));
        address.AutoCompleteMode = AutoCompleteMode.Suggest;
        address.AutoCompleteSource = AutoCompleteSource.FileSystemDirectories;
        address.KeyDown += AddressKeyDown;
        bar.Controls.Add(address, 4, 0);

        var viewMenu = new ContextMenuStrip();
        foreach (var (name, mode) in ViewModes)
            viewMenu.Items.Add(name, null, (_, _) => ActiveView?.SetViewMode(mode));
        viewMenu.Opening += (_, _) =>
        {
            uint? current = ActiveView?.GetViewMode();
            for (int i = 0; i < ViewModes.Length; i++)
                ((ToolStripMenuItem)viewMenu.Items[i]).Checked = ViewModes[i].Mode == current;
        };
        Button? viewButton = null;
        viewButton = Glyph("", "Kiểu hiển thị", () => viewMenu.Show(viewButton!, new Point(0, viewButton!.Height)));
        bar.Controls.Add(viewButton, 5, 0);

        bar.Controls.Add(Glyph("", "Thư mục mới", () => ActiveView?.CreateNew(true)), 6, 0);
        bar.Controls.Add(Glyph("", "File văn bản mới (.txt)", () => ActiveView?.CreateNew(false)), 7, 0);
        bar.Controls.Add(Glyph("", "Thêm thư mục này vào Yêu thích", RequestAddFavorite), 8, 0);
        bar.Controls.Add(Glyph("", "Tab mới (Ctrl+T)", () => NewTab()), 9, 0);
    }

    private void BuildTabMenu()
    {
        tabMenu.Items.Add("Tab mới", null, (_, _) => NewTab());
        tabMenu.Items.Add("Đóng tab", null, (_, _) => CloseActiveTab());
        tabMenu.Items.Add("Đóng các tab khác", null, (_, _) => CloseOtherTabs());
        tabMenu.Items.Add(new ToolStripSeparator());
        tabMenu.Items.Add("Sao chép đường dẫn", null, (_, _) =>
        {
            if (ActiveView is { } v) Clipboard.SetText(v.TargetPath);
        });
        tabMenu.Items.Add("Mở trong File Explorer", null, (_, _) =>
        {
            if (ActiveView is { } v) OpenInExplorer(v.TargetPath);
        });
        tabMenu.Items.Add("Thêm vào Yêu thích", null, (_, _) => RequestAddFavorite());
    }

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch { /* không mở được thì thôi */ }
    }

    private void AddTab(string path, string? title, bool select)
    {
        var view = new FolderView(path, title) { Dock = DockStyle.Fill };
        var page = new TabPage { Tag = view, UseVisualStyleBackColor = true };
        page.Controls.Add(view);
        ApplyTitle(page, view);
        view.Changed += (_, _) => ViewChanged(page, view);
        tabs.TabPages.Add(page);
        UpdateTabWidth();
        if (select)
        {
            tabs.SelectedTab = page;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CloseTab(int index)
    {
        if (index < 0 || index >= tabs.TabCount) return;
        if (tabs.TabCount == 1)
        {
            // Luôn giữ ít nhất một tab trong khung.
            ActiveView?.Navigate(Store.ThisPC);
            return;
        }
        var page = tabs.TabPages[index];
        if (index == tabs.SelectedIndex) tabs.SelectedIndex = index > 0 ? index - 1 : 1;
        tabs.TabPages.Remove(page);
        page.Dispose();
        UpdateTabWidth();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseOtherTabs()
    {
        var keep = tabs.SelectedTab;
        foreach (var page in tabs.TabPages.Cast<TabPage>().Where(p => p != keep).ToList())
        {
            tabs.TabPages.Remove(page);
            page.Dispose();
        }
        UpdateTabWidth();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ViewChanged(TabPage page, FolderView view)
    {
        if (IsDisposed || page.IsDisposed) return;
        ApplyTitle(page, view);
        if (view == ActiveView) UpdateAddress();
        tabs.Invalidate();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyTitle(TabPage page, FolderView view)
    {
        if (page.Text != view.Title) page.Text = view.Title;
        page.ToolTipText = view.TargetPath;
    }

    private void UpdateAddress()
    {
        if (ActiveView is not { } v) return;
        string text = FolderView.IsShellPath(v.TargetPath) ? v.Title : v.TargetPath;
        if (!address.Focused || address.Text.Length == 0) address.Text = text;
    }

    private void AddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (ActiveView is not { } v) return;

        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            address.Text = FolderView.IsShellPath(v.TargetPath) ? v.Title : v.TargetPath;
            v.FocusView();
            return;
        }
        if (e.KeyCode != Keys.Enter) return;

        e.SuppressKeyPress = true;
        string text = Environment.ExpandEnvironmentVariables(address.Text.Trim().Trim('"'));
        if (text.Length == 0) return;
        if (FolderView.IsShellPath(v.TargetPath) && text == v.Title)
        {
            v.FocusView();
            return;
        }
        // Dán đường dẫn tới một file thì mở thư mục chứa nó.
        if (File.Exists(text)) text = Path.GetDirectoryName(text) ?? text;
        v.Navigate(text, true);
    }

    private void UpdateTabWidth()
    {
        // Gán ItemSize làm TabControl tự đổi kích thước qua lại (rộng thêm 1px rồi trả về) để vẽ lại,
        // tức là lại phát Resize và gọi vào đây. Không chặn thì hai bên gọi nhau vô hạn tới khi sập.
        if (updatingTabWidth || tabs.TabCount == 0) return;
        updatingTabWidth = true;
        try
        {
            // Luôn chừa một khoảng trống cuối dải tab để bấm đúp mở tab mới.
            int available = tabs.ClientSize.Width - S(StripSpare);
            int width = Math.Clamp(available / tabs.TabCount, S(MinTabWidth), S(MaxTabWidth));
            var size = new Size(width, S(26));
            if (tabs.ItemSize != size) tabs.ItemSize = size;
        }
        finally
        {
            updatingTabWidth = false;
        }
    }

    // Tự nhận biết hai lần bấm liên tiếp vào khoảng trống bên phải các tab (lần bấm thứ hai
    // có thể tới dưới dạng bấm thường hoặc bấm đúp tuỳ lớp cửa sổ, cả hai đều qua MouseDown).
    private void TabsMouseDown(object? sender, MouseEventArgs e)
    {
        bool onEmptyStrip = e.Button == MouseButtons.Left && tabs.TabCount > 0
                            && e.Y <= tabs.GetTabRect(0).Bottom && TabIndexAt(e.Location) < 0;
        if (!onEmptyStrip)
        {
            lastStripClickTick = 0;
            return;
        }

        long now = Environment.TickCount64;
        Size slop = SystemInformation.DoubleClickSize;
        bool isDouble = now - lastStripClickTick <= SystemInformation.DoubleClickTime
                        && Math.Abs(e.X - lastStripClickPoint.X) <= slop.Width
                        && Math.Abs(e.Y - lastStripClickPoint.Y) <= slop.Height;
        if (isDouble)
        {
            lastStripClickTick = 0;
            NewTab();
        }
        else
        {
            lastStripClickTick = now;
            lastStripClickPoint = e.Location;
        }
    }

    private int TabIndexAt(Point p)
    {
        for (int i = 0; i < tabs.TabCount; i++)
            if (tabs.GetTabRect(i).Contains(p)) return i;
        return -1;
    }

    private Rectangle CloseRect(Rectangle tab) =>
        new(tab.Right - S(20), tab.Top + (tab.Height - S(16)) / 2, S(16), S(16));

    private void TabsMouseUp(object? sender, MouseEventArgs e)
    {
        int index = TabIndexAt(e.Location);
        if (index < 0) return;

        if (e.Button == MouseButtons.Middle)
        {
            CloseTab(index);
        }
        else if (e.Button == MouseButtons.Left && CloseRect(tabs.GetTabRect(index)).Contains(e.Location))
        {
            CloseTab(index);
        }
        else if (e.Button == MouseButtons.Right)
        {
            tabs.SelectedIndex = index;
            tabMenu.Show(tabs, e.Location);
        }
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= tabs.TabCount) return;
        var page = tabs.TabPages[e.Index];
        var view = page.Tag as FolderView;
        Rectangle r = tabs.GetTabRect(e.Index);
        bool selected = e.Index == tabs.SelectedIndex;

        using (var bg = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
            e.Graphics.FillRectangle(bg, r);
        if (selected)
        {
            using var accent = new SolidBrush(active ? ActiveAccent : SystemColors.ControlDark);
            e.Graphics.FillRectangle(accent, r.Left, r.Top, r.Width, S(3));
        }

        Rectangle close = CloseRect(r);
        var textRect = Rectangle.FromLTRB(r.Left + S(6), r.Top + (selected ? S(2) : 0), close.Left, r.Bottom);
        Color textColor = view is { IsWaiting: true } ? SystemColors.GrayText : SystemColors.ControlText;
        TextRenderer.DrawText(e.Graphics, page.Text, tabs.Font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(e.Graphics, "✕", closeFont, close, SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            glyphFont.Dispose();
            closeFont.Dispose();
            tabMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
