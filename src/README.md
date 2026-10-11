# DeskBox / src

桌面收纳盒 —— 把桌面上的快捷方式和文件拖进面板集中管理，桌面立刻变干净。

本目录是根据发布的 `DeskBox.exe`（.NET 单文件打包）**反编译还原**出来的源码，
并已修复编译/运行上的问题，可以直接用 .NET 8 SDK 编译。

> 完整的项目介绍与使用说明见仓库根目录的 [README.md](../README.md)，
> 模块职责、数据流与设计取舍见 [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)。

## 编译

```powershell
dotnet build .\DeskBox.csproj -c Release
```

发布成单文件（与官方发布形态一致）：

```powershell
dotnet publish .\DeskBox.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 目录结构

| 路径 | 说明 |
| --- | --- |
| `App.xaml(.cs)` | 应用入口、单实例、托盘、命令行（`--capture` / `--icons` / `--diag` / `--autostart` / `--selftest`） |
| `MainWindow.xaml(.cs)` | 主面板：收纳区、拖放、分组、右键菜单、排序 |
| `SettingsWindow.xaml(.cs)` | 设置窗口：主题、背景材质、图标、**热键**、开机自启 |
| `InputDialog.xaml(.cs)` | 通用输入对话框 |
| `Diagnostics.cs` | 图标导出、自检 |
| `Infrastructure/AppPaths.cs` | 数据目录、日志、路径与降级处理 |
| `Interop/Native.cs` | Win32 P/Invoke 封装 |
| `Models/` | 配置与收纳项数据模型 |
| `Services/` | 各项服务（见下） |
| `Theme/` | 样式与主题画刷 |
| `ViewModels/ItemVm.cs` | 收纳项视图模型 |
| `Properties/AssemblyInfo.cs` | 程序集元数据 |

### Services

| 服务 | 职责 |
| --- | --- |
| `HotkeyService` | 全局热键注册 / 注销 / 解析 / 显示名 |
| `ConfigService` | `config.json` 读写 |
| `ItemStore` | 收纳项增删、打开、导出、排序 |
| `ShortcutService` | `.lnk` 创建与解析 |
| `IconService` | 图标提取与线条化 |
| `TrayService` | 托盘图标与气泡通知 |
| `AutoStartService` | 开机自启（注册表 Run 项 / 启动文件夹） |
| `ThemeService` / `ThemeBrushes` / `BackdropService` | 主题、Mica/Acrylic 背景 |
| `RecycleBin` | 删除到回收站 |

## 相比原始发布版所做的修复

### 1. 热键无法录入（严重）

`SettingsWindow.HotkeyBox_PreviewKeyDown` 里判断"是否只按下了修饰键"时用了
**有符号**比较：

```csharp
// 原始（有 bug）
if (val - 70 <= 1 || val - 116 <= 5)
```

`Key` 底层是 `int`。按 `A`(44) 时 `44 - 70 = -26`，负数 ≤ 1 成立，
于是所有 Key 值 ≤ 121 的按键（字母、数字、空格、`F1`~`F12`、小键盘、
方向键……）都被误判成"修饰键"，函数直接返回，热键永远录不进去。

修复为无符号比较：

```csharp
// 修复后
if ((uint)(key - Key.LWin) <= 1 || (uint)(key - Key.LeftShift) <= 5)
```

这样只有 `LWin/RWin/Shift/Ctrl/Alt`（Key 值 70、71、116~121）会被
拦下等待下一个键，其余按键正常记录。

### 2. 快捷方式打不开（严重）

`ItemStore.Open` 原本直接把 `.lnk` 交给 `Process.Start`，依赖 Windows 的
`.lnk` 文件关联。一旦 `HKEY_CLASSES_ROOT\lnkfile` 下的 `IsShortcut` 被
"去掉快捷方式小箭头"类工具删掉，系统就报

> 没有应用程序与此操作的指定文件有关联。

所有快捷方式立刻全部打不开。现在改为**先自己用 `IShellLinkW` 解析
`.lnk`，拿到目标程序、参数和工作目录后直接启动目标**，不再依赖文件关联；
只有解析失败时才回退到原来的 ShellExecute 路径。

```csharp
public static void Open(DeskItem item)
{
    string path = LaunchPath(item);
    if (string.IsNullOrEmpty(path)) return;

    try
    {
        // 自己解析 .lnk，不依赖系统关联
        if (TryLaunchShortcut(path)) return;

        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true, ... });
    }
    catch (Exception ex) { ... }
}
```

实测：把 `IsShortcut` 删掉、确认系统已无法打开任何 `.lnk` 之后，
新版本仍能正常启动快捷方式指向的程序。

### 3. 魔法数字改为命名常量

`HotkeyService` 增加 `ModAlt/ModControl/ModShift/ModWin` 常量与
`ToNativeModifiers(ModifierKeys)` 辅助方法，`BuildSpec`、`TryParse`
不再直接用 `1/2/4/8`。

### 4. 按键显示名整理

`KeyDisplayName` 由反编译的 `switch (key - N)` 数值分支改写为可读的
`switch (key)` 枚举分支，并补上 `PageUp/PageDown/Home/End` 等。

## 已知的运行环境问题（非本程序缺陷）

若**所有**快捷方式都打不开，并报

> 没有应用程序与此操作的指定文件有关联。

那是 Windows 的 `.lnk` 文件关联被破坏，而非 DeskBox 的问题：
`HKEY_CLASSES_ROOT\lnkfile` 下缺少 `IsShortcut` 值。
"去掉快捷方式小箭头"类工具（例如写 `Shell Icons\29` 指向空白图标的做法）
可能顺手删掉它。

恢复方式（需管理员）：

```powershell
reg add "HKLM\SOFTWARE\Classes\lnkfile" /v IsShortcut /t REG_SZ /d "" /f
reg add "HKCU\Software\Classes\lnkfile" /v IsShortcut /t REG_SZ /d "" /f
```

仓库根目录提供了现成的 `fix-shortcut-association.reg`。

## 说明

源码由反编译还原，变量名/结构已尽量整理（去掉编译器生成的闭包类、
反编译残留注释等），但**不是**原始工程，注释与部分命名系整理时补充。

