# BatteryCharge · 电池充电助手

BatteryCharge 是一款面向 Windows 11 的轻量级电池充电管理工具，使用 **C#、.NET 10 和 WinForms** 开发。

通过设备提供的 `EnergyDrv` 驱动接口控制充电模式，支持 Windows 11 风格界面、系统托盘、自动启动和中英文切换。

## 功能特性

| 功能 | 说明 |
|---|---|
| 普通充电 | 恢复设备的普通充电模式 |
| 电池养护 | 启用设备固件提供的充电限制 |
| 快速充电 | 启用设备支持的快速充电模式 |
| 夜间充电 | 独立检测和控制夜间充电功能 |
| 电池监控 | 显示电量、供电状态和当前充电模式 |
| 开机自动启动 | 通过 Windows 任务计划程序启动到系统托盘 |
| 多语言 | 简体中文 / English，即时切换 |
| 深浅色主题 | 跟随 Windows 系统外观，支持高对比度 |
| 清理卸载 | 清除应用配置和自动启动任务 |

支持窗口与托盘操作、单实例运行、多显示器和不同 DPI 缩放。

**说明：** 充电限制百分比和夜间充电策略由设备固件决定，应用不支持设置任意充电百分比。

## 使用方法

1. 下载 Windows x64 发布包并解压。
2. 运行 `BatteryCharge.exe`，接受管理员权限请求。
3. 选择普通、养护或快速充电模式。
4. 点击「应用充电模式」，等待设备状态验证。
5. 可在「应用设置」中启用自动启动、切换语言。

应用启动时仅读取设备状态，不会自动修改充电模式。只有用户主动操作时才会写入设备设置。

关闭或最小化窗口后，程序继续在系统托盘运行。

### 快捷键

| 快捷键 | 功能 |
|---|---|
| `F5` | 刷新状态 |
| `Alt + 1` | 打开概览 |
| `Alt + 2` | 打开应用设置 |

## 自动启动

自动启动默认关闭，使用 Windows 任务计划程序创建当前用户的登录任务：

- 登录约 10 秒后启动到系统托盘。
- 使用当前用户的最高可用权限，不保存登录密码。
- 不依赖注册表 `Run` 项。
- 自动检测程序路径变化，避免使用失效或不安全的启动路径。

**安全要求：** 自动启动仅接受位于本地固定磁盘、访问权限受到保护的程序目录。建议安装到 `Program Files`，不要直接从下载目录、桌面、共享目录或移动硬盘启用高权限自动启动。

移动程序后，需要重新关闭并开启自动启动以更新路径。

## 安全设计

- 使用独立的 Win32 P/Invoke 封装访问 `\\.\EnergyDrv`。
- 设备操作串行执行，防止并发写入交错。
- 写入后重新读取状态，确认操作是否生效。
- 驱动操作设置超时保护，异常时停止后续驱动读写。
- 只读刷新不会修改设备充电配置。
- 自动启动校验可执行文件路径、所有者和访问权限。
- 使用受保护的单实例机制，阻止普通权限进程抢占实例对象。
- 配置文件处理包含大小限制、路径验证及目录链接防护。

不包含第三方 NuGet 包，也不依赖 Lenovo Legion Toolkit 的程序集或源码。

**设备兼容性：** 需要目标 Windows 设备提供兼容的联想 `EnergyDrv` 驱动。应用不提供或安装该驱动，不保证所有电脑均支持相关充电功能。

## 系统要求

- Windows 11 x64
- .NET 10 Windows Desktop Runtime
- 兼容的设备驱动

查看已安装的运行时：

```powershell
dotnet --list-runtimes
```

普通发布版本需要 `Microsoft.WindowsDesktop.App 10.0.x`。

自包含发布版本包含所需的 .NET 运行时。

## 构建与发布

开发环境需要 **.NET 10 SDK**。

在仓库根目录执行：

```powershell
dotnet build BatteryCharge.slnx -c Release
```

### Windows 发布

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

自包含版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -SelfContained
```

### Linux 交叉构建

```bash
bash scripts/publish.sh
```

自包含版本：

```bash
bash scripts/publish.sh --self-contained
```

发布输出：

```text
artifacts/win-x64/
├── framework-dependent/
│   └── BatteryCharge.exe
└── self-contained/
    └── BatteryCharge.exe
```

发布采用单文件模式；如生成附属文件，应分发完整发布目录。

## 测试

运行独立行为检查：

```bash
dotnet run --project tests/BatteryCharge.Checks/BatteryCharge.Checks.csproj -c Release
```

检查覆盖充电模式、设备状态、异常处理、串行操作、自动启动、配置管理和语言切换等行为。

### 当前验证状态

- Debian 13 / .NET 10 SDK：**30/30 项行为检查通过**。
- Windows 应用及界面检查项目：Release 构建成功，0 警告、0 错误。
- Windows 实际界面、托盘、自动启动及设备驱动操作：**尚待实机验证**。

模拟检查通过不代表真实硬件功能已经验证。

详细说明见 [安全审查与修复记录](docs/security-review.zh-CN.md) 和 [桌面 UI 设计](docs/desktop-ui.md)。

## GitHub Actions

仓库支持自动构建和发布：

- 推送版本 Tag 时，自动构建并创建 GitHub Release。
- 支持通过 Actions 手动触发构建。
- 执行 Linux、Windows 行为检查和界面检查。
- 上传 Windows x64 ZIP 发布包及可用的界面截图。
- 基础行为检查或构建失败会阻止发布；UI 检查失败记录警告。
- 使用 Dependabot 定期检查依赖和 GitHub Actions 更新。

发布示例：

```bash
git tag v1.0.1
git push origin v1.0.1
```

## 清理与卸载

在应用设置或托盘菜单中选择「清理并退出」，程序会：

1. 删除当前用户由本应用创建的自动启动任务。
2. 删除 `settings.json` 和相关临时配置文件。
3. 退出程序。

之后可以手动删除发布文件夹。

清理操作不会删除其他用户的任务、无关文件，也不会恢复或修改设备固件中已经生效的充电设置。

## 项目结构

```text
BatteryCharge.slnx
src/
  BatteryCharge.Core/      核心状态与控制逻辑
  BatteryCharge.App/       WinForms 界面与驱动封装
tests/
  BatteryCharge.Checks/    行为检查
  BatteryCharge.UiChecks/  界面检查
scripts/                   构建与发布脚本
docs/                      设计与安全文档
.github/workflows/         CI/CD
```

## 参考项目

[Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) — 仅用于设备协议研究。

BatteryCharge 为独立实现，不直接引用其源码、程序集或运行组件。
