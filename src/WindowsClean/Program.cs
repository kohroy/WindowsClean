using System.Runtime.InteropServices;

Console.WriteLine("========================================");
Console.WriteLine("          Windows 垃圾清理工具");
Console.WriteLine("========================================");
Console.WriteLine();

var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
var windowsTempDir = Path.Combine(windowsDir, "Temp");
var prefetchDir = Path.Combine(windowsDir, "Prefetch");
var cookiesDir = Environment.GetFolderPath(Environment.SpecialFolder.Cookies);
var recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
var internetCacheDir = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
var userTempDir = Path.GetTempPath();

var exts = new[] { "*.tmp", "*._mp", "*.log", "*.gid", "*.chk", "*.old" };

// 清理项清单（回收站项由后续任务追加）
List<CleanItem> cleanItems =
[
    new("临时文件（按扩展名）", "删除 *.tmp/*._mp/*.log/*.gid/*.chk/*.old 垃圾文件，对应 clean.cmd L1-6，限定安全目录", $"{userTempDir}；{windowsTempDir}",
        stats =>
        {
            foreach (var root in new[] { userTempDir, windowsTempDir })
            {
                foreach (var pattern in exts)
                {
                    DeleteFiles(root, pattern, stats);
                }
            }
        }),
    new("Windows 备份文件", "删除 Windows 目录下备份文件，对应 clean.cmd L8", windowsDir,
        stats => DeleteFiles(windowsDir, "*.bak", stats)),
    new("Prefetch 预读取文件", "清空 Prefetch 预读取文件，对应 clean.cmd L9", prefetchDir,
        stats => ClearDirectoryContents(prefetchDir, stats)),
    new("Windows 临时目录", "清空系统临时目录，对应 clean.cmd L10，清空替代 rd+md 以保留 ACL", windowsTempDir,
        stats => ClearDirectoryContents(windowsTempDir, stats)),
    new("Cookies", "清空浏览器 Cookies，对应 clean.cmd L11", cookiesDir,
        stats => ClearDirectoryContents(cookiesDir, stats)),
    new("最近使用记录", "清空最近使用记录，对应 clean.cmd L12+L15，原脚本重复删除已去重", recentDir,
        stats => ClearDirectoryContents(recentDir, stats)),
    new("Internet 临时文件", "清空浏览器 Internet 缓存，对应 clean.cmd L13", internetCacheDir,
        stats => ClearDirectoryContents(internetCacheDir, stats)),
    new("用户临时目录", "清空当前用户临时目录，对应 clean.cmd L14", userTempDir,
        stats => ClearDirectoryContents(userTempDir, stats)),
    new("回收站", "清空所有驱动器的回收站，对应 clean.cmd L7，Shell API 适配原 %systemdrive%\\recycled 直删", "所有驱动器",
        stats =>
        {
            // 0x7 = SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND（不弹确认框、不显示进度、不播放声音）
            var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7u);
            if (hr == 0)
            {
                Console.WriteLine("  回收站已清空");
            }
            else
            {
                Console.WriteLine($"  回收站清空失败（HRESULT=0x{hr:X8}）");
            }
        }),
];

if (cleanItems.Count == 0)
{
    Console.WriteLine("（暂无清理项）");
}
else
{
    for (var i = 0; i < cleanItems.Count; i++)
    {
        var item = cleanItems[i];
        Console.WriteLine($"[{i + 1}] {item.Name} —— {item.Description}（目标：{item.Target}）");
    }
}
Console.WriteLine();

Console.Write("确认执行以上清理项？按 Y 继续，其他键取消：");
var key = Console.ReadKey();
Console.WriteLine();
Console.WriteLine();

var stats = new CleanStats();
if (key.KeyChar is 'Y' or 'y')
{
    foreach (var item in cleanItems)
    {
        Console.WriteLine($"=== 正在清理：{item.Name} ===");
        item.Execute(stats);
    }
}
else
{
    Console.WriteLine("已取消清理");
}
Console.WriteLine();

Console.WriteLine($"共删除 {stats.DeletedFiles} 个文件，跳过 {stats.SkippedFiles} 个，释放空间 {FormatBytes(stats.FreedBytes)}");
Console.WriteLine();

Console.Write("按任意键退出...");
Console.ReadKey();

static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
    if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
    return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
}

static void DeleteFiles(string root, string pattern, CleanStats stats)
{
    if (!Directory.Exists(root)) return;

    var options = new EnumerationOptions
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    foreach (var file in new DirectoryInfo(root).EnumerateFiles(pattern, options))
    {
        try
        {
            var size = file.Length;
            File.Delete(file.FullName);
            stats.DeletedFiles++;
            stats.FreedBytes += size;
            Console.WriteLine($"  删除文件：{file.FullName}");
        }
        catch (Exception e)
        {
            stats.SkippedFiles++;
            Console.WriteLine($"  跳过（{e.GetType().Name}）：{file.FullName}");
        }
    }
}

static void ClearDirectoryContents(string root, CleanStats stats)
{
    if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

    DeleteFiles(root, "*", stats);

    var options = new EnumerationOptions
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    foreach (var dir in new DirectoryInfo(root).EnumerateDirectories("*", options))
    {
        try
        {
            dir.Delete(recursive: true);
            Console.WriteLine($"  删除目录：{dir.FullName}");
        }
        catch
        {
            Console.WriteLine($"  跳过目录：{dir.FullName}");
        }
    }
}

internal sealed class CleanStats
{
    public int DeletedFiles { get; set; }

    public int SkippedFiles { get; set; }

    public long FreedBytes { get; set; }
}

internal sealed record CleanItem(string Name, string Description, string Target, Action<CleanStats> Execute);

internal static partial class NativeMethods
{
    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}