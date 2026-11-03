## 实施

- [x] 1.1 重写 Program.cs：建立主流程框架与统计结构
     【目标对象】`WindowsClean/Program.cs`
     【修改目的】替换现有演练代码（删除逻辑被注释的 `*.tmp` 打印逻辑、L23-89 环境变量注释块），建立"清理项清单展示 → 用户确认 → 逐项执行 → 统计报告"的可编译运行骨架
     【修改方式】整体重写为顶级语句风格（与现有风格保持一致），文件末尾（顶级语句之后）声明类型
     【相关依赖】无（纯 BCL，`ImplicitUsings=enable` 已隐式引入 `System.IO`）
     【修改内容】
        - 删除现有全部内容
        - 文件末尾定义统计类型 `CleanStats`：`int DeletedFiles`（已删除文件数）、`int SkippedFiles`（跳过/失败文件数）、`long FreedBytes`（累计释放字节数）
        - 定义清理项结构（可用简单元组或记录类型）：名称、描述、目标路径展示文本、执行委托 `Action<CleanStats>`
        - 主流程：输出横幅（中文，如"Windows 垃圾清理工具"）→ 遍历清理项列表打印"[编号] 名称 —— 描述（目标：路径）"（列表暂为空时输出"（暂无清理项）"，保证骨架独立可编译运行）→ 提示"确认执行以上清理项？按 Y 继续，其他键取消："并 `Console.ReadKey()` 读取按键 → 按 Y/y 时逐项输出"=== 正在清理：{名称} ==="并调用执行器，否则输出"已取消清理" → 输出统计报告："共删除 {DeletedFiles} 个文件，跳过 {SkippedFiles} 个，释放空间 {格式化}" → "按任意键退出..."后 `Console.ReadKey()` 暂停
        - 实现释放空间格式化函数：按 B/KB/MB/GB 自适应单位（1024 进制），保留 1 位小数（如 "0 B"、"1.5 KB"、"20.3 MB"）
- [x] 1.2 实现通用删除核心方法并注册全部目录类清理项（8 项）
     【目标对象】`WindowsClean/Program.cs`
     【修改目的】实现安全的文件/目录删除核心方法，并注册 clean.cmd 中 8 个目录类清理项（对应 L1-6、L8-L15 去重后的清理意图）
     【修改方式】在 1.1 骨架基础上添加核心方法（顶级语句中的本地函数）与清理项注册代码
     【相关依赖】任务 1.1 的 `CleanStats` 结构与清理项列表
     【修改内容】
        - 本地函数 `void DeleteFiles(string root, string pattern, CleanStats stats)`（递归删除匹配文件，不动目录）：
          - `root` 目录不存在时直接 return
          - 枚举选项：`new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }`（跳过 junction/reparse point，防止 `Documents and Settings` 等目录重复扫描或死循环）
          - 使用 `new DirectoryInfo(root).EnumerateFiles(pattern, options)` 惰性枚举（不用 `GetFiles`，避免全量加载）
          - 逐文件 `try { var size = file.Length; File.Delete(file.FullName); stats.DeletedFiles++; stats.FreedBytes += size; Console.WriteLine($"  删除文件：{file.FullName}"); } catch (Exception e) { stats.SkippedFiles++; Console.WriteLine($"  跳过（{e.GetType().Name}）：{file.FullName}"); }`（先读取 `file.Length` 存入局部变量、删除成功后再累加——删除后再访问 `FileInfo` 属性依赖枚举缓存的实现细节，先读后删保证统计稳健）
        - 本地函数 `void ClearDirectoryContents(string root, CleanStats stats)`（清空目录全部内容，保留目录本身与 ACL）：
          - 前置检查：`string.IsNullOrEmpty(root)`（`GetFolderPath` 找不到特殊目录时会返回空字符串）或 `!Directory.Exists(root)` 时直接 return——否则 `EnumerateDirectories` 会抛 `ArgumentException`/`DirectoryNotFoundException`，中断整个清理流程
          - 先调用 `DeleteFiles(root, "*", stats)` 删除全部文件（含子目录内文件）
          - 再遍历 `new DirectoryInfo(root).EnumerateDirectories(new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })`（一级枚举即可，文件已由 `DeleteFiles` 递归删除；显式传入枚举选项：无参重载 `EnumerateDirectories()` 为旧式兼容语义，不跳过 reparse point，TEMP 等目录下应用创建的 junction 被 `Delete(recursive: true)` 处理时行为不可预期，须跳过）
          - 逐个 `try { dir.Delete(recursive: true); Console.WriteLine($"  删除目录：{dir.FullName}"); } catch { Console.WriteLine($"  跳过目录：{dir.FullName}"); }`（目录非空或被占用则保留；不计入 `SkippedFiles`，该计数仅统计文件）
        - 注册 8 个清理项（名称 → 实现，覆盖 clean.cmd 对应行；各项"描述"字段填写用途及对应 clean.cmd 行号，如"对应 clean.cmd L1-6，限定安全目录"，供启动清单展示）：
          1. "临时文件（按扩展名）" → 对 `Path.GetTempPath()` 与 `Windows目录\Temp` 两个目录，各调用 `DeleteFiles(dir, ext, stats)`，ext 依次为 `*.tmp`、`*._mp`、`*.log`、`*.gid`、`*.chk`、`*.old`（对应 L1-6 的现代化安全适配：限定安全目录，避免原全盘递归误删应用日志）
          2. "Windows 备份文件" → `DeleteFiles(Windows目录, "*.bak", stats)`（对应 L8）
          3. "Prefetch 预读取文件" → `ClearDirectoryContents(Path.Combine(Windows目录, "Prefetch"), stats)`（对应 L9）
          4. "Windows 临时目录" → `ClearDirectoryContents(Path.Combine(Windows目录, "Temp"), stats)`（对应 L10，清空替代 rd+md 以保留 ACL）
          5. "Cookies" → `ClearDirectoryContents(Environment.GetFolderPath(Environment.SpecialFolder.Cookies), stats)`（对应 L11）
          6. "最近使用记录" → `ClearDirectoryContents(Environment.GetFolderPath(Environment.SpecialFolder.Recent), stats)`（对应 L12+L15，原脚本重复删除已去重）
          7. "Internet 临时文件" → `ClearDirectoryContents(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache), stats)`（对应 L13）
          8. "用户临时目录" → `ClearDirectoryContents(Path.GetTempPath(), stats)`（对应 L14）
        - 路径获取规范：Windows 目录用 `Environment.GetFolderPath(Environment.SpecialFolder.Windows)`，禁止写死 `C:\Windows`；各清理项的"目标路径展示文本"填写实际解析路径
        - 说明：清理项 1 与清理项 3/4/8 在相同目录先后执行（先按扩展名删、清空时删除其余全部），顺序执行无冲突，属于忠实原脚本结构
- [x] 1.3 实现回收站清空清理项（P/Invoke）
     【目标对象】`WindowsClean/Program.cs`
     【修改目的】实现 clean.cmd L7（清空 `%systemdrive%\recycled`）的现代等价功能：通过 Shell API 清空所有驱动器回收站
     【修改方式】在顶级语句的清理项注册列表（任务 1.2 已注册 8 项）末尾追加第 9 项；文件末尾（顶级语句之后，与 `CleanStats` 等类型并列）添加 `NativeMethods` 静态类
     【相关依赖】`shell32.dll` 的 `SHEmptyRecycleBin` API；项目 `PublishAot=true`，P/Invoke 必须使用 `[LibraryImport]` 源生成器（不可仅用 `DllImport`）
     【修改内容】
        - 文件末尾添加：`internal static partial class NativeMethods { [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)] internal static partial uint SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags); }`
        - 【关键】必须显式指定 `EntryPoint = "SHEmptyRecycleBinW"`：`[LibraryImport]` 源生成器生成的 P/Invoke 固定按精确入口名查找（等效 `ExactSpelling=true`，不像 `DllImport` 默认会自动尝试 A/W 后缀），而 shell32.dll 仅导出 `SHEmptyRecycleBinA`/`SHEmptyRecycleBinW`，不存在无后缀入口——缺省 `EntryPoint` 会在运行时抛 `EntryPointNotFoundException`
        - 清理项执行器：调用 `NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, 0x7u)`；`0x7 = SHERB_NOCONFIRMATION(0x1) | SHERB_NOPROGRESSUI(0x2) | SHERB_NOSOUND(0x4)`（不弹确认框、不显示进度、不播放声音）
        - 返回值 `0`（S_OK）时输出"回收站已清空"；非 0 时输出"回收站清空失败（HRESULT=0x{返回值:X8}）"，不抛异常
        - 注册清理项"回收站"：描述为"清空所有驱动器的回收站"，目标展示"所有驱动器"（Shell API 不返回文件数与空间，不计入 CleanStats 文件统计）
- [x] 2.1 新增 app.manifest 并在 csproj 中声明
     【目标对象】`WindowsClean/app.manifest`（新增）、`WindowsClean/WindowsClean.csproj`（修改）
     【修改目的】使程序双击运行时自动请求 UAC 管理员授权，保障 `C:\Windows\Temp`、`C:\Windows\Prefetch`、`C:\Windows\*.bak` 等需提权清理项可执行
     【修改方式】新增 UTF-8 编码的 manifest 文件，csproj 显式声明引用（.NET SDK 会将其嵌入输出 exe，Native AOT 发布同样支持）
     【相关依赖】无（.NET SDK 内建 manifest 嵌入机制）
     【修改内容】
        - 新增 `WindowsClean/app.manifest`：采用标准 UAC 清单 XML 结构（根元素 `assembly`，命名空间 `xmlns="urn:schemas-microsoft-com:asm.v1"`，缺少命名空间的清单无效），包含 `assemblyIdentity`（name="WindowsClean.app" version="1.0.0.0"）与 `trustInfo/security/requestedPrivileges/requestedExecutionLevel level="requireAdministrator" uiAccess="false"`
        - `WindowsClean/WindowsClean.csproj` 第一个 `PropertyGroup` 内新增一行：`<ApplicationManifest>app.manifest</ApplicationManifest>`
        - 不修改 csproj 其他任何配置
- [x] 3.1 构建验证
     【目标对象】整个解决方案（`WindowsClean.sln`）
     【修改目的】验证所有代码变更可正常编译
     【修改方式】在解决方案根目录执行 `dotnet build WindowsClean.sln -c Debug`
     【相关依赖】.NET 8 SDK（本机已具备构建条件）
     【修改内容】
        - 执行构建，确认零错误；如出现警告（如未使用变量）则修复后重新构建
        - 【重要】仅做编译验证，严禁运行 `WindowsClean.exe`——程序会真实删除系统文件，不得在验证阶段执行
