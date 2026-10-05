# 电池充电助手 · BatteryCharge

独立的 Windows 电池充电小应用，使用 C#、.NET 10 和 WinForms。包含设置窗口和托盘菜单。

## 功能

| 功能 | 操作 |
| --- | --- |
| 普通充电 | 切换到普通模式 |
| 电池养护 | 启用设备固件提供的充电限制 |
| 快速充电 | 启用设备固件提供的快充模式 |
| 夜间充电 | 单独检测支持性，开启或关闭 |

打开应用后只读取设备状态。选择模式后点击“应用充电模式”，或在托盘菜单选择模式，才会写入设置。夜间充电开关也只响应用户点击；读取状态和更新界面不会触发写入。

应用通过自己的 Win32 P/Invoke 封装访问 `\\.\EnergyDrv`。写入整个命令序列时串行执行，写完后重新查询，最多验证 12 次；没有生效则提示失败。启动时申请管理员权限，并防止应用多开。关闭窗口或最小化会收起到托盘，点击“退出”结束程序。

养护百分比和夜间策略由设备实现，应用不提供任意百分比或具体钟点设置。某项功能读取失败时，只禁用对应控件，并显示设备、权限或协议错误原因。

## 独立性

`LenovoLegionToolkit` 仅作为协议研究参考。仓库根目录就是独立应用项目，无需保留参考仓库即可构建：

- 没有引用、加载或调用 LLT 的项目、程序集、命令行程序或源码。
- 没有链接 LLT 的源码文件，也不使用 LLT 的注册表设置。
- 控制层、Windows 驱动封装、界面、错误处理和验证逻辑均在这个项目内实现。
- 无第三方 NuGet 包；构建时需要 .NET 框架引用包。

底层仍依赖目标机器提供的联想 `EnergyDrv` 设备。应用本身不包含、安装或编写驱动。仅在可读取该设备对应功能的 Windows 机型上工作。

## 运行环境

默认发布目标为 Windows x64，使用 .NET 10 桌面运行时。运行时检查：

```powershell
dotnet --list-runtimes
```

需要看到 `Microsoft.WindowsDesktop.App 10.0.x`。已有这个运行时的电脑可以用体积更小的默认发布版本。自包含版本会打包运行时，可用于没有安装 .NET 10 的电脑。

## 构建

开发机需要 **.NET 10 SDK**，只有 Runtime 不能编译。可在 Windows 或 Linux 构建；Linux 构建 GUI 项目时需要能下载 Windows 框架引用包。下载地址：[.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)。

在仓库根目录（包含 `BatteryCharge.slnx` 的目录）执行以下命令，无需进入额外的 `BatteryCharge` 子目录。每个发布脚本都会先运行独立控制层的行为检查，失败时停止发布。

Windows PowerShell：

```powershell
dotnet --list-sdks
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Linux：

```bash
bash scripts/publish.sh
```

默认生成：

```text
artifacts/win-x64/framework-dependent/BatteryCharge.exe
```

将生成的发布文件夹复制到 Windows，运行 `BatteryCharge.exe`。

要打包运行时，使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -SelfContained
```

```bash
bash scripts/publish.sh --self-contained
```

自包含版本输出到 `artifacts/win-x64/self-contained/`。两个版本都按单文件方式发布；若出现附属文件，分发完整发布目录即可。

也可以打开 `BatteryCharge.slnx`，或直接执行：

```bash
dotnet build BatteryCharge.slnx -c Release
```

## 行为检查

```bash
dotnet run --project tests/BatteryCharge.Checks/BatteryCharge.Checks.csproj -c Release
```

检查使用模拟设备，不访问真实硬件，不依赖测试框架或第三方包。覆盖：只读探测、独立功能支持性、设备缺失、三种模式命令顺序、夜间开关、禁止写入不支持的夜间功能、写入无效、延迟生效、驱动失败、非法枚举，以及并发设置时完整命令序列不能交错。

根目录的 `.github/workflows/build.yml` 会执行 Linux 和 Windows 行为检查，并生成两个 Windows 发布包。工作流只构建和上传产物，不发布 Release。

## Windows 实机验证

1. 启动程序并接受管理员权限请求；确认初始状态或具体失败原因。
2. 依次应用普通、养护、快速模式；确认成功提示、当前模式及托盘勾选同步变化。
3. 刷新，确认显示的是设备读回值；重新打开程序再次核对。
4. 若夜间充电可用，开启和关闭后刷新；若不可用，确认只禁用该开关。
5. 关闭窗口后使用托盘菜单切换，最后点击“退出”。

如果另一个联想管理程序同时更改同一设置，设备状态可能变化；点击“刷新状态”读取实际值。本应用不自动恢复历史设置，不写开机启动项，也不修改其他联想软件。

## 当前验证情况

源码已实现。创建该项目的 Linux 环境没有 .NET SDK，且 SDK 下载连接不可用，因此尚未完成 C# 编译、行为检查执行、Windows 界面运行和实机驱动验证；需要在具备 SDK 的环境运行以上检查和发布命令。静态文件与构建配置检查不代表程序已通过编译或硬件测试。

## 文件结构

```text
BatteryCharge.slnx            解决方案入口
Directory.Build.props        通用构建配置
global.json                  .NET SDK 版本选择
src/BatteryCharge.Core/       状态、协议及串行控制层
src/BatteryCharge.App/        独立驱动封装、WinForms 窗口及托盘
tests/BatteryCharge.Checks/   无硬件依赖的行为检查
scripts/                     Windows / Linux 发布脚本
.github/workflows/           自动构建配置
LenovoLegionToolkit/          本地参考仓库，已被 Git 忽略
```

协议研究参考：[Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit)。
