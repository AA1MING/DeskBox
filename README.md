<img src="src/assets/app.preview.png" alt="DeskBox" width="420">

# DeskBox · 桌面收纳盒

把桌面上的快捷方式、文件和文件夹**拖进面板集中管理**，桌面立刻变干净。

DeskBox 是一个常驻托盘的 Windows 桌面整理工具：一个全局热键呼出的半透明面板，
把散落一地的图标收纳进统一的仓库里，用分组、排序、右键菜单继续正常使用它们。
原生 WPF 实现，无第三方依赖，单个 exe 即可运行。

```
拖进来  →  原文件被移动到收纳目录  →  桌面干净了  →  热键呼出面板照常打开
```

---

## 功能

### 收纳

- **拖放即收纳**：把桌面上的快捷方式 / 文件 / 整个文件夹拖进面板即可。
  文件会被**移动**到收纳目录（不是复制），原位置立刻清空 —— 这才是"桌面变干净"的关键。
  移动失败时（跨盘、被占用、权限不足）自动降级为复制，并在状态栏说明"原文件仍在原处"。
- **文件夹自动变成分组**：拖进来的文件夹会连同内部内容一起收纳成一个分组，
  可以点进去继续管理，形成多级结构。
- **重复文件名自动编号**：`报告.docx` 已存在时自动存为 `报告 (2).docx`，不会覆盖。
- **导出回桌面**：右键任意条目「导出到桌面」，把文件放回去（保留收纳目录里的原件）。

### 面板

- **全局热键呼出**：默认不占用热键，在设置里录一个（如 `Ctrl+Alt+空格`）即可随时呼出/收起。
- **托盘常驻**：左键单击托盘图标开合面板，右键菜单可打开设置、数据目录、使用说明、退出。
- **单实例**：重复启动只会把已有窗口呼出，不会开出第二个进程。
- **点击面板外部自动收起**（可在面板标题栏「图钉」按钮固定住不自动隐藏）。
- **拖拽排序与归档**：按住图标拖动调整顺序，拖到分组图标上即归档进该分组。
- **快捷方式拖回桌面**：把快捷方式拖到桌面或资源管理器即可移出收纳盒；按住 `Ctrl` 则复制，保留收纳盒中的原件。
- **一键排序**：手动顺序 / 名称 / 类型 / 使用频率（按点击次数）四种模式。
- **拼音排序**：按名称排序时文件夹置顶，英文按 A–Z 在前，中文按完整拼音在后；数字按数值排列。
- **全局搜索**：顶部搜索框可搜索所有分组中的应用、快捷方式、文件和文件夹，支持名称、完整拼音和首字母（例如“微信 / weixin / wx”）。清空后返回原分组；`Ctrl+F` 聚焦搜索，`Enter` 打开首项，`Esc` 清空搜索。
- **柔和过渡**：面板、分组、设置、菜单、控件反馈、滚动与图标重排统一使用约 300 ms 的过渡。
- **右键菜单**：打开、以管理员身份运行、打开所在位置、重命名、删除（进回收站）、导出到桌面、属性。

### 外观

- **主题**：跟随系统 / 浅色 / 深色，实时响应 Windows 主题切换。
- **背景材质**：Windows 11 上使用 Mica（云母）/ Acrylic（亚克力）系统材质；
  低版本或材质不可用时自动降级为纯色 / 模糊，不会出现黑块。
- **强调色**：可跟随 Windows 系统强调色，也可固定为默认蓝。
- **图标**：32–96 px 可调（默认 48），可隐藏名称标签，可切换「彩色原图 / 单色统一」
  （单色模式会对图标做线条化处理，与背景融合得更好）。
- **高对比度**：自动识别系统高对比度模式并强制不透明，保证可读性。
- **DPI**：Per-Monitor V2，多屏不同缩放下不模糊。

### 系统集成

- **开机自启**：优先写 `HKCU\...\CurrentVersion\Run`，失败时回退到启动文件夹快捷方式；
  同时识别"任务管理器 → 启动应用"里被用户手动禁用的情况。
- **不依赖 `.lnk` 文件关联**：自己用 `IShellLinkW` 解析快捷方式再启动目标程序。
  即使系统的 `.lnk` 关联被"去掉快捷方式小箭头"之类的工具破坏，DeskBox 里的快捷方式依然能打开。
- **删除进回收站**：调用 `SHFileOperation`，误删可恢复，不是永久删除。

### 数据与可靠性

- **数据目录**：默认 `%AppData%\DeskBox`；便携模式可用环境变量 `DESKBOX_HOME`
  或程序目录下的 `portable.txt` 指定。
- **受限环境降级**：被沙箱 / 受限令牌启动导致写不进正常目录时，自动降级到
  程序目录旁的 `DeskBoxData`，甚至临时目录，并在托盘气泡 + 弹窗里说明原因，
  还会提供"用正常方式重新启动一次"的选项；恢复正常后数据自动迁回正式目录。
- **配置容错**：`config.json` 解析失败时备份为 `config.json.broken` 并使用默认配置，不会崩溃。
- **原子写入**：配置先写 `.tmp` 再 `Move` 覆盖，避免断电/崩溃留下半个文件。
- **日志**：`deskbox.log` 依次尝试写入数据目录、程序目录、临时目录，至少能留下一份。

---

## 运行要求

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10 1809+ / Windows 11（原生云母与亚克力需要 Windows 11 22621+，旧版本使用模糊回退） |
| 运行时 | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（x64） |
| 权限 | 普通用户即可，**不需要管理员** |

## 快速开始

从 [Releases](../../releases) 下载 `DeskBox.exe` 双击运行，或自行编译（见下）。

首次使用时面板会自动弹出；之后启动驻留托盘，通过托盘或热键呼出。
之后程序常驻托盘，建议到「设置」里录一个呼出热键。

## 编译

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
cd src
dotnet build .\DeskBox.csproj -c Release
```

发布为单文件（与官方发布形态一致）：

```powershell
cd src
dotnet publish .\DeskBox.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

产物在 `src\bin\Release\net8.0-windows\win-x64\publish\DeskBox.exe`。

回归检查（使用隔离数据目录，不修改现有收纳内容）：

```powershell
dotnet run --project .\tests\DeskBox.Tests.csproj -c Release
```

云母与亚克力需要在 Windows 设置中开启“透明效果”；关闭透明效果或启用高对比度时使用纯色。拼音使用内嵌的 Unicode Unihan 数据，无需联网；多音字采用默认读音并补充常见词组，数据许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 命令行参数

调试与自动化用的隐藏开关：

| 参数 | 说明 |
| --- | --- |
| `--tray` | 静默启动，只驻留托盘，不弹面板（开机自启用的就是这个） |
| `--relaunched` | 标记"这是重新启动的进程"，避免受限环境的提示循环 |
| `--diag [路径]` | 输出环境诊断：数据目录可写性、进程令牌、完整性级别、父进程等 |
| `--autostart [on\|off\|status]` | 查看 / 切换开机自启，结果写入 `autostart.txt` |
| `--selftest [路径]` | 自检：快捷方式创建与解析、导入、排序、重命名、持久化等 |
| `--icons [路径]` | 导出图标提取结果对照图 |
| `--capture [路径]` `--demo` `--settings` | 截图面板 / 设置窗口（`--demo` 用演示数据填充） |
| `--capture-menu [路径]` | 截图托盘右键菜单 |

## 项目结构

```
DeskBox/
├── src/                        # 全部源码
│   ├── DeskBox.csproj          # .NET 8 / WPF / x64
│   ├── app.manifest            # DPI 感知、UTF-8 代码页、支持的系统版本
│   ├── App.xaml(.cs)           # 入口：单实例、命令行分支、托盘、热键装配
│   ├── MainWindow.xaml(.cs)    # 主面板：收纳区、拖放、分组、排序、右键菜单
│   ├── SettingsWindow.xaml(.cs)# 设置：主题 / 材质 / 图标 / 热键 / 开机自启
│   ├── InputDialog.xaml(.cs)   # 通用输入框（新建文件夹、重命名）
│   ├── Diagnostics.cs          # 自检、图标导出、窗口截图
│   ├── Infrastructure/         # AppPaths：数据目录、日志、路径降级
│   ├── Interop/                # Native：Win32 / Shell / DWM P/Invoke 封装
│   ├── Models/                 # AppConfig、DeskItem、ItemKind
│   ├── Services/               # 服务，见 docs/ARCHITECTURE.md
│   ├── Theme/                  # ThemeBrushes + styles.xaml 控件样式
│   ├── ViewModels/             # ItemVm
│   └── assets/                 # 图标与 Logo
└── docs/ARCHITECTURE.md        # 架构说明
```

详细的模块职责、数据流与关键设计取舍见 **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**。

## 数据存放位置

| 路径 | 内容 |
| --- | --- |
| `%AppData%\DeskBox\config.json` | 全部设置与收纳项索引 |
| `%AppData%\DeskBox\Items\` | **被收纳的原文件本体** |
| `%AppData%\DeskBox\deskbox.log` | 运行日志 |

> ⚠️ `Items\` 里放的是真实文件，不要手动删除或改名；
> 想拿回某个文件请用右键「导出到桌面」。

## 已知问题（环境相关，非本程序缺陷）

如果**系统里所有**快捷方式都打不开并提示"没有应用程序与此操作的指定文件有关联"，
那是 Windows 的 `.lnk` 文件关联被破坏了（`HKEY_CLASSES_ROOT\lnkfile` 下缺少 `IsShortcut`），
常见于使用过"去掉快捷方式小箭头"类工具之后。

DeskBox 自身不受影响（它自己解析 `.lnk`），但其他程序会受影响。恢复方式（需管理员）：

```powershell
reg add "HKLM\SOFTWARE\Classes\lnkfile" /v IsShortcut /t REG_SZ /d "" /f
reg add "HKCU\Software\Classes\lnkfile" /v IsShortcut /t REG_SZ /d "" /f
```

仓库根目录提供了现成的 `fix-shortcut-association.reg`，双击导入亦可。

## 版本历史

见 [CHANGELOG.md](CHANGELOG.md)。

## 关于源码

本工程的源码由发布的 `DeskBox.exe`（.NET 单文件打包）**反编译还原**而来，
并修复了编译与运行上的问题、整理了命名与结构（去掉了编译器生成的闭包类与反编译残留注释）。
它**不是**原始工程，注释与部分命名是整理时补充的。

## 许可

尚未指定开源许可证。在补充 LICENSE 之前，保留所有权利。
