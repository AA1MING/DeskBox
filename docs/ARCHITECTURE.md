# DeskBox 架构说明

面向要读源码或改代码的人。先看 [README](../README.md) 了解它做什么，这里讲它是怎么搭的。

## 一、整体形态

DeskBox 是一个**常驻托盘的 WPF 应用**，没有主窗口生命周期 —— `ShutdownMode="OnExplicitShutdown"`，
主面板 `MainWindow` 只是"一个可以反复显示/隐藏的窗口"，不是应用主体。
应用主体是 `App` 持有的三样东西：托盘图标、全局热键、以及被收纳项的数据模型。

```
                    ┌─────────────────────────────┐
                    │            App              │
                    │  单实例 Mutex + 事件信号      │
                    └──┬────────┬────────┬────────┘
                       │        │        │
              ┌────────▼──┐ ┌───▼─────┐ ┌▼─────────────┐
              │TrayService│ │Hotkey   │ │  MainWindow  │
              │ (托盘菜单) │ │Service  │ │   (主面板)    │
              └───────────┘ └─────────┘ └──────┬───────┘
                       ▲                       │
                       │              ┌────────▼────────┐
                       │              │ SettingsWindow  │
                       │              └────────┬────────┘
                       │                       │
        ┌──────────────┴───────────────────────┴──────────────┐
        │                   Services / 静态门面                 │
        │  ConfigService · ItemStore · IconService · Theme...   │
        └──────────────────────┬───────────────────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │ Infrastructure      │
                    │ AppPaths(数据目录)   │
                    │ Interop/Native(P/Invoke) │
                    └─────────────────────┘
```

**分层约定**：`Interop` 只放 P/Invoke 声明与结构体，不含业务逻辑；
`Services` 是可以直接调用的静态门面（无 DI 容器）；UI 层（`MainWindow` / `SettingsWindow`）
通过服务读写状态。`App` 是唯一的装配点。

## 二、启动流程

`App.OnStartup` 是整个程序的编排中心，顺序大致如下：

1. **挂全局异常处理**：UI 线程异常记录后 `Handled = true`（不让程序因一次界面异常退出），
   后台线程异常只记日志。
2. **解析命令行**，判断是否处于"无人值守模式"（`--capture` / `--icons` / `--selftest` /
   `--autostart` / `--diag` / `--capture-menu`）。这些模式**不申请单实例锁**，因为它们要能
   和正在运行的实例共存。
3. **单实例**：非无人值守时申请 `Local\DeskBox_SingleInstance` 互斥体。抢不到锁就说明已有实例，
   于是给 `Local\DeskBox_ShowPanel` 事件发信号让老实例把面板呼出来，自己 `Shutdown()`。
4. **初始化数据目录**（`AppPaths.Init()`）→ **读配置**（`ConfigService.Load()`）→
   **探测系统主题**（`ThemeService.Refresh()`）→ **应用主题画刷**（`ThemeBrushes.Apply()`）。
5. **写启动自述**：把 exe 路径、数据目录、是否受限、父进程名、可疑环境变量记一行日志
   （同时写到数据目录和 exe 旁边的 `DeskBox-startup.log`），便于事后排查"为什么数据跑别处去了"。
6. **处理早退型命令行**（`--autostart` / `--diag` / `--selftest` / `--icons` / `--capture-menu`），
   执行完直接 `Shutdown()`。
7. **受限环境询问**：如果数据目录不可用或被降级，弹窗询问是否"用正常方式重新启动一次"；
   用户同意则通过 `explorer.exe` 启动一个临时 `.lnk`（这样新进程的父进程是 explorer，
   能拿到正常令牌），自己退出。`--relaunched` 参数用来避免这个提示无限循环。
8. **创建 `MainWindow`**，然后：
   - 启动后台线程监听 `Local\DeskBox_ShowPanel` 事件（第二个实例被启动时用来呼出面板）；
   - `EnsureHandle()` 提前建好窗口句柄（热键和 DWM 材质都需要 HWND）；
   - 装配 `HotkeyService`（订阅 `Pressed` → 面板开合，`SystemSettingsChanged` → 重新探测主题）；
   - 注册配置里的热键；
   - 装配 `TrayService`，把托盘菜单的各项请求接到对应动作上；
   - 根据状态弹气泡：受限环境提示 / 未设热键提示 / 已启动提示；
   - 如果收纳盒是空的，自动把面板显示出来。

## 三、数据模型

```
AppConfig                       ←→  %AppData%\DeskBox\config.json
├── ThemeMode / Backdrop / UseAccentColor
├── IconSize / ShowLabels / MonochromeIcons
├── Hotkey / AutoStart
├── SortMode / PinnedOpen / LastGroupId
├── WidthRatio / HeightRatio        # 面板尺寸占屏幕比例
├── RestrictedNoticeShown           # 受限环境弹窗只提示一次
└── Items : List<DeskItem>          # 树形：分组可以有 Children

DeskItem
├── Id            # GUID，拖放时用来在树里定位
├── Name          # 显示名（可重命名，不影响磁盘文件名）
├── Kind          # Shortcut | File | Folder | Group
├── StoredName    # 在 Items\ 目录下的真实文件名
├── Target        # 快捷方式解析出的目标程序（仅 Shortcut）
├── Arguments     # 快捷方式的目标参数
├── ClickCount    # 「按使用频率排序」用
├── AddedUtc
└── Children      # 仅 Group 使用
```

**关键设计：`Name` 与 `StoredName` 分离。**
重命名只改 `Name`，磁盘上的文件名保持不动，避免重命名引发路径失效、
以及同名冲突、扩展名被改坏等问题。`ItemVm.Name` 也是从这里读的。

**Kind 的实际含义**：`Folder` 是枚举里保留但当前不产出的值 —— 拖进来的文件夹一律变成 `Group`
（内容被递归收纳、空壳目录被删掉）。`Group` 不出现在磁盘上，只是配置里的一个容器节点。

## 四、服务层

| 服务 | 职责 | 关键点 |
| --- | --- | --- |
| `AppPaths` | 数据目录决策、路径、日志 | 三级降级；`Items\` 存放文件本体 |
| `ConfigService` | `config.json` 读写 | 原子写、损坏备份、宽松 JSON 编码（中文不转义） |
| `ItemStore` | 收纳项的增删改查与启动 | 移动→复制降级、`.lnk` 自解析启动 |
| `ShortcutService` | `.lnk` 创建与解析 | COM `IShellLinkW` + `IPersistFile` |
| `IconService` | 图标提取与线条化 | 内存缓存、可选单色化 |
| `HotkeyService` | 全局热键 | 消息窗口 + `RegisterHotKey`、字符串规格编解码 |
| `TrayService` | 托盘图标与菜单 | `NotifyIcon` + WPF `ContextMenu` 混用 |
| `AutoStartService` | 开机自启 | 注册表 Run 优先，启动文件夹兜底 |
| `ThemeService` | 读系统主题/强调色 | 注册表 + 监听系统广播消息 |
| `ThemeBrushes` | 计算并写入动态画刷 | 全部 UI 颜色都走 `DynamicResource` |
| `BackdropService` | Mica / Acrylic / 纯色 | Win11 走 DWM，低版本走 SetWindowCompositionAttribute |
| `RecycleBin` | 删除到回收站 | `SHFileOperation` + `FOF_ALLOWUNDO` |

### ItemStore：收纳与启动

**收纳（`ImportAll`）**
- 文件：`File.Move` 到 `Items\`，文件名冲突时 `UniqueStoredName` 自动加 `(2)`、`(3)`。
  移动抛异常（跨盘 / 占用 / 权限）时降级为 `File.Copy` 并累加 `CopiedInsteadOfMoved`，
  UI 据此在状态栏告诉用户"原文件仍在原处"。
- 已经在 `Items\` 里的文件不会二次搬运，只登记。
- 文件夹：递归 `ImportDirectory` 成 `Group`，逐个子项走上面的文件流程，
  最后尝试删掉已经空了的原目录。
- 导入 `.lnk` 时立刻 `Resolve` 一次，把 `Target` / `Arguments` 落库，
  这样后面启动不必再解析。

**启动（`Open`）**
```
LaunchPath(item)                       # 优先 Items\ 里的实体，回退到 Target
   └─ TryLaunchShortcut(path)          # .lnk：自己解析后直接启动目标
        ├─ ShortcutService.Resolve()
        └─ Process.Start(target, args, workingDir)
   └─ Process.Start(ShellExecute)      # 非 .lnk，或解析失败时的回退路径
```
自己解析 `.lnk` 而不把 `.lnk` 交给 `ShellExecute`，是为了绕开系统文件关联被破坏的场景
（详见 CHANGELOG 1.0.1）。

### AppPaths：数据目录三级降级

```
DESKBOX_HOME 环境变量          ─┐
exe 旁存在 portable.txt        ─┼─→ 便携模式：BaseDir = 指定目录 / exe 目录
否则 %AppData%\DeskBox         ─┘

BaseDir 写不进去？
   ├─ 降级 1：exe 旁的 DeskBoxData\      （Degraded = true，托盘气泡 + 首次弹窗说明）
   └─ 降级 2：%TEMP%\DeskBox\            （再不行就只能记日志了）
```

降级后只要正式目录恢复可写，`TryMigrateFromExeSide` 会把 `config.json` 和 `Items\` 搬回去。

`Log()` 也做同样的多路尝试：数据目录 → exe 目录 → 临时目录，写成功一处就停。

## 五、主面板

### 显示 / 隐藏

面板是一个 `WindowStyle=None` + `Topmost` 的无边框窗口，位置按屏幕工作区的
`WidthRatio` / `HeightRatio` 居中偏上计算（`PositionPanel`），并保证不出屏。

- `ShowPanel` / `HidePanel` / `TogglePanel` 三个入口，带缩放+位移动画。
- 动画是异步的，而用户可能在动画没播完时又按了一次热键 —— 所以有个
  `_visibilityToken` 计数器，动画回调里先比对 token，过期就丢弃，避免"快速连按后状态错乱"。
- 点击面板外部自动隐藏：`OnDeactivated` 里不是立刻隐藏，而是启动 `_hideWatch` 定时器，
  并记下 `_releasedAt`。这是为了处理"右键菜单 / 输入框弹出时窗口失活"的情况 ——
  定时器 tick 时检查是否真的没有子窗口在开着（`_dialogOpen` / `_menuOpen`），
  否则会把正在用的右键菜单连窗口一起关掉。
- 图钉按钮（`PinnedOpen`）可以取消自动隐藏。

### 拖放

涉及两套拖放，用自定义剪贴板格式 `DeskBox.ItemId` 区分：

| 来源 | 数据格式 | 处理 |
| --- | --- | --- |
| 资源管理器（外部文件） | `FileDrop` | `Area_Drop` → `ItemStore.ImportAll` → 插入到当前分组 |
| 面板内部图标 | `DeskBox.ItemId` | `Item_Drop` → 在树里移动节点（含拖到分组上归档） |

内部拖动时，`Item_DragOver` 会拒绝把分组拖进自己的子孙节点（`ContainsItem` 判定），
防止构造出环。`ClearDropTargets` / `SetDropTarget` 负责高亮反馈。

### 导航与渲染

`_currentGroupId` 表示当前所在分组（null 为根）。`Navigate` 切换分组并触发内容切换动画，
同时把 `LastGroupId` 持久化，下次打开还停在原来的位置。

`Refresh()` 把当前分组的 `DeskItem` 列表投影成 `ItemVm` 集合绑给 `ItemsControl`：
每个条目现取图标（`IconService`，带缓存）、按配置算出格子尺寸与标签可见性。
布局是 `WrapPanel`，条目大小由 `ItemVm.CellWidth/CellHeight` 决定。

### 排序

`ApplySort(mode)` 直接对当前分组的列表重排后整体 `Refresh`：

| mode | 规则 |
| --- | --- |
| `Manual` | 保持用户拖拽出来的顺序（默认） |
| `Name` | 按 `Name` 当前区域文化忽略大小写 |
| `Type` | 先按 `Kind` 再按 `Name` |
| `Frequent` | 按 `ClickCount` 倒序，再按 `Name` |

`ActivateItem` 打开条目时会 `ClickCount++`，这就是"使用频率"的数据来源。

## 六、设置窗口

`SettingsWindow` 是单例（静态 `Current`），构造时 `LoadFromConfig()` 把配置灌进控件，
每个控件的 `Checked` / `ValueChanged` 事件再写回 `ConfigService` 并 `Save()` —— 即**改即存**，
没有"确定/取消"按钮。

**热键录入**（`HotkeyBox_PreviewKeyDown`）是这里最需要小心的部分：

```csharp
Key key = e.Key == Key.System ? e.SystemKey : e.Key;   // 按 Alt 组合键时真键在 SystemKey

if ((uint)(key - Key.LWin) <= 1 || (uint)(key - Key.LeftShift) <= 5)
    // 只按下了修饰键（LWin/RWin=70,71；Shift/Ctrl/Alt=116..121），等下一个键
else if (key == Key.Back || key == Key.Delete)
    SetHotkey(string.Empty);                            // 清除
else
    SetHotkey(HotkeyService.BuildSpec(modifiers, key)); // 记录，至少要求一个修饰键
```

**必须先转 `uint` 再比较**：`Key` 底层是 `int`，若用有符号比较，`A`(44) 减 70 得负数
依然满足 `<= 1`，所有普通键都会被误判成修饰键，热键永远录不进去。
（这就是 1.0.0 的严重 bug，见 CHANGELOG。）

改完热键调 `App.RestartHotkey()` 立即重新注册；打开输入框 / 菜单时
`SuspendHotkey` / `ResumeHotkey` 临时让出热键，避免和录入冲突。

## 七、主题与视觉

所有界面颜色都定义为 `DynamicResource` 画刷，`ThemeBrushes.Apply()` 在运行时替换它们的值，
所以切主题不需要重建窗口。

```
ThemeService.Refresh()          读注册表：AppsUseLightTheme、AccentColor、系统高对比度
        │                       并监听 WM_SETTINGCHANGE / WM_THEMECHANGED /
        │                       WM_DWMCOLORIZATIONCOLORCHANGED 广播
        ▼
ThemeBrushes.Apply()            算出 IsDarkNow / CurrentAccent，写入 Application.Resources
        │                       并写入每个已打开窗口的资源
        ▼
App.ApplyThemeEverywhere()      主面板 + 设置窗口 + 托盘图标一起刷新
```

`ThemeBrushes.IsDarkNow` 的决定顺序：配置显式指定浅色/深色 → 否则跟随系统。
`CurrentAccent` 在高对比度下强制用系统高亮色，否则按 `UseAccentColor` 取系统强调色或默认蓝。

`BackdropService` 分两条路：
- Windows 11（build ≥ 22000）：`DwmSetWindowAttribute` 设 `DWMWA_SYSTEMBACKDROP_TYPE`
  为 Mica(2) / Acrylic(3)，并用 `DwmExtendFrameIntoClientArea` 全窗延伸；返回码非 0 说明不支持，走降级。
- 其他版本：`SetWindowCompositionAttribute` + `AccentPolicy` 做模糊或半透明，
  高对比度或选纯色时直接不透明。

## 八、托盘

`TrayService` 用 WinForms 的 `NotifyIcon`（WPF 没有托盘 API），
但右键菜单是 WPF 的 `ContextMenu`，这样能复用 `Theme/Styles.xaml` 里的
`DeskMenuStyle` / `DeskMenuItemStyle`，视觉和面板一致。

对外只暴露事件（`ToggleRequested` / `SettingsRequested` / `OpenDataFolderRequested` /
`HelpRequested` / `ExitRequested`）和两个回调（`AutoStartStateProvider` / `AutoStartSetter`），
`App` 负责接线 —— 托盘自己不直接调业务代码。

托盘图标从嵌入资源 `assets/app.ico` 读取，主题切换时重新着色（`ApplyTheme`）。

## 九、互操作层

`Interop/Native.cs` 集中所有 P/Invoke，涉及四类 API：

| 类别 | 用到的 API |
| --- | --- |
| 窗口 / 热键 | `RegisterHotKey` `UnregisterHotKey`、`SetWindowPos`、`GetCursorPos`、`MonitorFromPoint` |
| DWM / 材质 | `DwmSetWindowAttribute`、`DwmExtendFrameIntoClientArea`、`SetWindowCompositionAttribute` |
| Shell | `SHFileOperation`、`ShellExecuteEx`、`SHGetStockIconInfo`、`SHGetFileInfo`、`ExtractIconEx` |
| 进程 | `CreateToolhelp32Snapshot` / `Process32First/Next`（取父进程名，用于诊断） |

`Interop/IPersistFileGuid.cs` 单独放 `IPersistFile` 的 GUID 常量（`ShortcutService` 里的
`[ComImport]` 接口需要它）。

> 注意：`Native.cs` 里 `GetParentProcessName` 等诊断用途的函数是"尽力而为"的，
> 失败一律返回占位字符串，不抛异常 —— 诊断代码本身不应该成为崩溃源。

## 十、诊断与自检

`Diagnostics` 提供三件事，都是给开发/排障用的：

- **`SelfTest(path)`**：在临时目录里造出 `.txt` / `.docx` / 子文件夹 / `.lnk`，
  跑一遍导入 → 检查原文件是否被移走、Kind 是否识别正确、快捷方式目标是否解析出来、
  分组子项数量、排序与重命名是否丢数据、`ClickCount` 能否持久化，逐条记 PASS/FAIL 写入报告。
  这是改完 `ItemStore` / `ConfigService` 后最快的回归验证手段：
  `DeskBox.exe --selftest`。
- **`IconSheet(path)`**：把图标提取/线条化结果排成对照图，用来核对 `IconService` 的效果。
- **`CaptureWindow` / `CaptureRegion`**：截窗口 / 截区域，配合 `--capture` 系列参数生成文档截图。

## 十一、改代码时的注意点

1. **`Items\` 里是用户的真实文件**。任何"清理"逻辑都必须先确认路径在 `ItemsDir` 之下，
   别把用户桌面的原文件删了。`ItemStore.DeleteFilesRecursive` 走的是回收站，保持这个习惯。
2. **拖放的两个数据格式不要混**。`DeskBox.ItemId` 是内部标识，
   `FileDrop` 才是外部文件；`IsAcceptable` 里两者都要放行，但处理分支完全不同。
3. **改热键相关代码先看 `uint` 转换那一段**，这是历史踩过的坑。
4. **新增 UI 颜色请用 `DynamicResource`**，否则主题切换时不会跟着变。
5. **改数据目录逻辑要同时考虑降级与迁移两条路径**，并且保证 `Log()` 至少能写出去一处。
6. **新增设置项**：`Models/AppConfig.cs` 加属性（给默认值）→ `SettingsWindow` 加控件与事件 →
   如果需要影响渲染，在 `MainWindow.Refresh()` / `ApplyTheme()` 里消费。
