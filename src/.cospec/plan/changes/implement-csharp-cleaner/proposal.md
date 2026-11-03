# 变更：使用 C# 控制台程序现代化实现 clean.cmd 清理脚本

## 原因
`WindowsClean/clean.cmd` 是 XP 时代的批处理清理脚本，其路径（`%userprofile%\Local Settings\...`、`%userprofile%\cookies`、`recycled` 等）在 Windows 10/11 上已失效或重定向，且"全盘递归删除 *.log"在现代系统上会误删大量运行中应用的日志；当前 `Program.cs` 仅有演练雏形（核心删除逻辑被注释）。需要以 C# 控制台程序实现等效且安全的清理功能。

## 变更内容
- **重写 `WindowsClean/Program.cs`**：以顶级语句风格实现完整清理程序，采用"现代化安全适配"策略（映射到现代等价路径，扩展名清理限定在安全目录内），保留原脚本全部清理意图：
  - 清理项映射（9 项）：

    | clean.cmd 原始行 | 现代化适配实现 |
    | --- | --- |
    | L1-6 全盘递归删 `*.tmp /*._mp /*.log /*.gid /*.chk /*.old` | 限定在 `%TEMP%` 与 `C:\Windows\Temp` 内按扩展名递归删除（避免全盘误删应用日志） |
    | L7 清空 `%systemdrive%\recycled` | P/Invoke `SHEmptyRecycleBin` 清空所有驱动器回收站 |
    | L8 `%windir%\*.bak` 递归删除 | `C:\Windows` 递归删除 `*.bak` |
    | L9 清空 `%windir%\prefetch` | 清空 `C:\Windows\Prefetch` 内容 |
    | L10 rd+md `%windir%\temp` | 清空 `C:\Windows\Temp` 内容（保留目录与 ACL，比 rd+md 更安全） |
    | L11 `%userprofile%\cookies` | 清空 `SpecialFolder.Cookies`（INetCookies） |
    | L12+L15 `%userprofile%\recent`（重复两次，去重合并） | 清空 `SpecialFolder.Recent` |
    | L13 `Local Settings\Temporary Internet Files` | 清空 `SpecialFolder.InternetCache`（INetCache） |
    | L14 `Local Settings\Temp` | 清空 `Path.GetTempPath()` |

  - 执行流程：启动显示清理项清单 → 用户按 Y 确认（其他键取消）→ 逐项执行（逐文件中文输出，失败容错跳过）→ 显示统计报告（删除文件数 / 跳过数 / 释放空间，格式化 B/KB/MB/GB）→ 暂停退出（保留双击运行场景）
  - 技术要点：`EnumerateFiles` 惰性枚举 + `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }`（跳过 junction 防止重复扫描/死循环）；逐文件 try-catch 容错（沿用现有代码范式）；回收站 P/Invoke 使用 `[LibraryImport]` 源生成器（Native AOT 约束）
- **新增 `WindowsClean/app.manifest`**：`requestedExecutionLevel level="requireAdministrator"`，双击运行时自动请求 UAC 管理员授权，保障 `C:\Windows\Temp`、`Prefetch`、`C:\Windows\*.bak` 等清理项可执行
- **修改 `WindowsClean/WindowsClean.csproj`**：新增 `<ApplicationManifest>app.manifest</ApplicationManifest>` 属性声明
- **BREAKING**：`Program.cs` 现有演练代码（删除被注释的 `*.tmp` 打印逻辑与 L23-89 环境变量注释块）整体替换
- `clean.cmd` 保留不动，仅作为功能参照

## 影响
- 受影响的规范：无（项目尚无 specs 目录）
- 受影响的代码：
    - `WindowsClean/Program.cs`：整体重写为完整清理程序（项目唯一源码文件，保持顶级语句风格）
    - `WindowsClean/app.manifest`：新增 UAC 提权清单文件
    - `WindowsClean/WindowsClean.csproj`：新增 ApplicationManifest 属性（其余配置不动）
    - `WindowsClean/clean.cmd`：不修改、不删除（仅作参照）
- 技术约束：项目为 Native AOT 发布（`PublishAot=true`），P/Invoke 必须使用 `[LibraryImport]` 而非仅 `DllImport`；`InvariantGlobalization=true`；`ImplicitUsings=enable`（`System.IO` 已隐式引入）+ `Nullable=enable`；无任何 NuGet 业务包（纯 BCL 实现即可，不新增包）；运行需管理员权限（由 manifest 保障）
