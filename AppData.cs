using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyFolder;

internal sealed class TabState
{
    public string Path { get; set; } = "";
    public string? Title { get; set; }
}

internal sealed class PaneState
{
    public List<TabState> Tabs { get; set; } = new();
    public int Active { get; set; }
}

internal sealed class LayoutState
{
    public int Rows { get; set; } = 2;
    public int Cols { get; set; } = 2;
    public List<PaneState> Panes { get; set; } = new();
    public List<double> Splits { get; set; } = new();

    public LayoutState Clone() =>
        JsonSerializer.Deserialize<LayoutState>(JsonSerializer.Serialize(this, Store.Json), Store.Json)!;
}

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

internal sealed class Favorite
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

internal sealed class AppData
{
    public int Version { get; set; } = 1;
    public WindowPlacement? Window { get; set; }
    public LayoutState Current { get; set; } = new();
    public string? CurrentSession { get; set; }
    public Dictionary<string, LayoutState> Sessions { get; set; } = new();
    public List<Favorite> Favorites { get; set; } = new();
    public bool CheckUpdates { get; set; } = true;
    public string? SkippedVersion { get; set; }
}

/// <summary>
/// Đọc/ghi dữ liệu. Ghi kiểu nguyên tử (file tạm rồi thay thế) kèm bản .bak và bản sao lưu
/// theo ngày, để một lần ghi hỏng hay mất điện không làm mất danh sách thư mục.
/// </summary>
internal static class Store
{
    public const string ThisPC = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const int MaxPanes = 6;
    private const int KeepBackups = 14;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Chế độ portable: có data.json nằm cạnh file exe thì dùng luôn thư mục đó.
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "data.json"));

    public static string Dir { get; } = IsPortable
        ? AppContext.BaseDirectory
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyFolder");
    public static string DataFile => Path.Combine(Dir, "data.json");
    private static string BackupFile => DataFile + ".bak";
    private static string BackupDir => Path.Combine(Dir, "backups");

    public static AppData Load(out string? warning)
    {
        warning = null;
        Directory.CreateDirectory(Dir);

        var candidates = new List<string> { DataFile, BackupFile };
        if (Directory.Exists(BackupDir))
            candidates.AddRange(Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f));

        bool mainBroken = false;
        foreach (string file in candidates)
        {
            if (!File.Exists(file)) continue;
            AppData? loaded = TryRead(file);
            if (loaded != null)
            {
                if (mainBroken)
                    warning = $"File dữ liệu chính bị lỗi, đã khôi phục từ bản sao lưu:\n{file}";
                return Sanitize(loaded);
            }
            if (file == DataFile)
            {
                mainBroken = true;
                try { File.Copy(file, Path.Combine(Dir, $"data.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); }
                catch { /* giữ được bản lỗi thì tốt, không thì thôi */ }
            }
        }

        if (mainBroken)
            warning = "File dữ liệu bị lỗi và không có bản sao lưu nào đọc được. Bắt đầu với bố cục mặc định.";
        return Sanitize(new AppData());
    }

    private static AppData? TryRead(string file)
    {
        try { return JsonSerializer.Deserialize<AppData>(File.ReadAllText(file), Json); }
        catch { return null; }
    }

    public static void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = DataFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));

            if (File.Exists(DataFile))
            {
                DailyBackup();
                File.Replace(tmp, DataFile, BackupFile);
            }
            else
            {
                File.Move(tmp, DataFile);
            }
        }
        catch
        {
            // File đang bị khoá (OneDrive, antivirus...): bỏ qua, lần lưu sau sẽ ghi lại.
        }
    }

    private static void DailyBackup()
    {
        Directory.CreateDirectory(BackupDir);
        string today = Path.Combine(BackupDir, $"data-{DateTime.Now:yyyyMMdd}.json");
        if (File.Exists(today)) return;
        File.Copy(DataFile, today);
        foreach (string old in Directory.GetFiles(BackupDir, "data-*.json").OrderByDescending(f => f).Skip(KeepBackups))
            File.Delete(old);
    }

    public static AppData Sanitize(AppData data)
    {
        data.Sessions ??= new();
        data.Favorites ??= new();
        data.Current ??= new();
        bool firstRun = data.Current.Panes == null || data.Current.Panes.Count == 0;
        SanitizeLayout(data.Current, firstRun);
        foreach (var s in data.Sessions.Values) SanitizeLayout(s, false);
        return data;
    }

    public static void SanitizeLayout(LayoutState st, bool useDefaults)
    {
        st.Rows = Math.Clamp(st.Rows, 1, 2);
        st.Cols = Math.Clamp(st.Cols, 1, 3);
        st.Panes ??= new();
        st.Splits ??= new();

        string[] defaults = useDefaults
            ? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                ThisPC,
            }
            : Array.Empty<string>();

        while (st.Panes.Count < MaxPanes)
        {
            string path = st.Panes.Count < defaults.Length ? defaults[st.Panes.Count] : ThisPC;
            st.Panes.Add(new PaneState { Tabs = { new TabState { Path = path } } });
        }

        foreach (var pane in st.Panes)
        {
            pane.Tabs ??= new();
            pane.Tabs.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Path));
            if (pane.Tabs.Count == 0) pane.Tabs.Add(new TabState { Path = ThisPC });
            pane.Active = Math.Clamp(pane.Active, 0, pane.Tabs.Count - 1);
        }
    }
}
