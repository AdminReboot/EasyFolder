using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyFolder;

/// <summary>
/// Tự cập nhật từ GitHub Releases. Bản phát hành là một file EasyFolder.exe duy nhất:
/// tải về cạnh file đang chạy, đổi tên file cũ thành .old (Windows cho đổi tên file đang
/// chạy) rồi đưa file mới vào chỗ. Lần chạy sau sẽ dọn file .old.
/// </summary>
internal static class Updater
{
    public const string AfterUpdateArg = "--after-update";
    private const string Repo = "AdminReboot/EasyFolder";
    private const string AssetName = "EasyFolder.exe";

    private static readonly HttpClient Http = CreateClient();

    public sealed record Release(Version Version, string Tag, string DownloadUrl, long Size, string? Sha256, string Notes);

    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version);

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EasyFolder", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version Normalize(Version? v) => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Trả về bản phát hành mới hơn bản đang chạy, hoặc null nếu đã là bản mới nhất.</summary>
    public static async Task<Release?> CheckAsync()
    {
        string json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? parsed)) return null;
        Version latest = Normalize(parsed);
        if (latest <= Current) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;

            string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new Release(
                latest,
                tag,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
        }
        return null;
    }

    /// <summary>Tải bản mới và thay file exe đang chạy. Ném lỗi nếu thất bại; file cũ khi đó vẫn nguyên.</summary>
    public static async Task ApplyAsync(Release release)
    {
        string exe = ExePath;
        string fresh = exe + ".new";
        string old = exe + ".old";

        byte[] bytes = await Http.GetByteArrayAsync(release.DownloadUrl);
        if (bytes.Length != release.Size || bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidDataException("File tải về không hợp lệ.");
        if (release.Sha256 != null &&
            !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File tải về sai mã kiểm tra SHA-256.");

        await File.WriteAllBytesAsync(fresh, bytes);
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(fresh, exe);
        }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    /// <summary>Mở bản mới; nó sẽ chờ bản này thoát hẳn rồi mới chạy.</summary>
    public static void StartNewVersion() =>
        Process.Start(new ProcessStartInfo(ExePath, AfterUpdateArg) { UseShellExecute = false });

    public static void CleanupOldFiles()
    {
        foreach (string suffix in new[] { ".old", ".new" })
        {
            try { File.Delete(ExePath + suffix); }
            catch { /* file cũ còn bị giữ, lần sau dọn tiếp */ }
        }
    }

    /// <summary>Chế độ dòng lệnh "--update": 0 = đã cập nhật, 1 = không có bản mới, 2 = lỗi.</summary>
    public static int RunHeadless()
    {
        try
        {
            Release? release = Task.Run(CheckAsync).GetAwaiter().GetResult();
            if (release == null) return 1;
            Task.Run(() => ApplyAsync(release)).GetAwaiter().GetResult();
            return 0;
        }
        catch
        {
            return 2;
        }
    }
}
