# Lenovo Legion Toolkit 电池充电功能提取说明

参考仓库：<https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit>

整理日期：2026-10-04。已核对本地仓库 `LenovoLegionToolkit/`，提交为 `6c86eb8afb76a124be9339e01fb7900df354765d`。未在真实联想设备上验证。本文整理功能和协议，尚未生成可执行程序。

## 功能范围

| 功能 | 代码状态 | 独立工具中的用途 |
| --- | --- | --- |
| 普通充电 | `BatteryState.Normal` | 普通充电模式 |
| 电池养护 / 充电限制 | `BatteryState.Conservation` | 启用固件提供的养护模式 |
| 快速充电 | `BatteryState.RapidCharge` | 启用固件提供的快速充电模式 |
| 隔夜电池充电 | `BatteryNightChargeState.On / Off` | 按项目界面说明，过夜先充到 80%，早晨使用时充到 100%；需要单独检测是否支持 |

前三项是一个枚举中的互斥模式。夜间充电使用另一条 IOCTL；代码提供开关，没有提供时间表、充电电流或百分比设置接口，组合效果需要按机型验证。

来源：[Enums.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Enums.cs)、[BatteryFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/BatteryFeature.cs)、[BatteryNightChargeFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/BatteryNightChargeFeature.cs)。

养护上限由固件决定。项目 FAQ 描述为：2021 年及更早机型约 60%，2022 年及以后约 80%；具体以设备为准。这里不能直接实现“任意设置 70%、85%”这样的滑块。

来源：[README 的养护阈值说明](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit#can-i-customize-conservation-mode-threshold)。

## 底层调用

调用链：应用 → `CreateFileW` 打开设备 → `DeviceIoControl` → 联想驱动 → 固件执行。

设备路径为 `\\.\EnergyDrv`。打开时使用读写访问、读写共享、`OPEN_EXISTING`，不是创建新设备。驱动是否存在、功能是否支持，都要在目标机器上检测。

来源：[Drivers.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/System/Drivers.cs)。

### 充电模式协议

IOCTL：`0x831020F8`。输入、输出均为一个 32 位无符号整数，缓冲区各 4 字节。

| 操作 | 输入值 / 顺序 |
| --- | --- |
| 查询当前模式 | `0xFF` |
| 设置养护模式 | 先发送 `0x08`，再发送 `0x03` |
| 设置普通模式 | 先发送 `0x05`，再发送 `0x08` |
| 设置快速充电 | 先发送 `0x05`，再发送 `0x07` |

设置时每个值是一次单独调用，不能把两个值合成一个数组缓冲区发送。

查询结果解析顺序：

1. `(result & 0x20) != 0`：养护模式。
2. 否则 `(result & 0x04) != 0`：快速充电。
3. 否则：普通模式。

来源：[BatteryFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/BatteryFeature.cs)、[PInvokeExtensions.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Extensions/PInvokeExtensions.cs)。

### 夜间充电协议

IOCTL：`0x83102150`。输入、输出同样为一个 32 位无符号整数。

| 操作 | 输入值 |
| --- | --- |
| 查询 | `0x11` |
| 开启 | `0x80000012` |
| 关闭 | `0x12` |

查询结果的 bit 0 必须为 1，否则原项目判为未知状态；bit 4 为 1 表示开启，为 0 表示关闭。本地界面说明该功能过夜先充到 80%，早晨使用时充到 100%；开关代码没有设置具体钟点的接口，实际策略由设备实现。

来源：[BatteryNightChargeFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/BatteryNightChargeFeature.cs)。

界面行为说明：[本地中文资源](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.WPF/Resources/Resource.zh-hans.resx:609)。

## 独立实现应保留的行为

先读取状态来探测支持性，两个功能分别检测。写入后重新读取实际状态；原项目最多验证 10 次，失败间隔 50 ms，最终不一致仅记录日志。独立工具建议把验证失败明确返回给界面。

连续切换模式时，建议串行执行整个设置序列，避免两组命令交错；这是独立实现建议。原项目提供取消机制，通用驱动队列则是可选项，电池类没有显式开启它。

来源：[AbstractDriverFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/AbstractDriverFeature.cs)。

原项目还将实际充电模式保存到以下注册表位置，并提供重新应用保存值的方法：

```text
HKEY_CURRENT_USER\Software\Lenovo\VantageService\AddinData\IdeaNotebookAddin
值名：BatteryChargeMode
普通：Normal
快充：Quick
养护：Storage
```

这是保存和恢复逻辑；改变硬件模式仍需上述驱动调用。独立工具可使用自己的配置文件保存偏好。本地代码确认，程序初始化和睡眠恢复时都会检查已保存的充电模式，发现不一致则重新应用。

来源：[BatteryFeature.cs](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/LenovoLegionToolkit.Lib/Features/BatteryFeature.cs)。

恢复入口：[App.xaml.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.WPF/App.xaml.cs:832)、[PowerStateListener.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Listeners/PowerStateListener.cs:225)。

## 本地源码位置与提取边界

| 文件 | 用途 | 独立实现建议 |
| --- | --- | --- |
| [BatteryFeature.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Features/BatteryFeature.cs:7) | 三种充电模式、状态解析、注册表保存 | 保留模式协议，按需改为自己的配置保存 |
| [BatteryNightChargeFeature.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Features/BatteryNightChargeFeature.cs:8) | 隔夜充电开关 | 可选功能，独立探测支持性 |
| [AbstractDriverFeature.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Features/AbstractDriverFeature.cs:16) | 支持性探测、驱动调用、写后验证 | 抽出必要逻辑，避免带入日志和全局标志依赖 |
| [Drivers.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/System/Drivers.cs:8) | 设备句柄及 IOCTL 常量 | 只保留电池所需常量和句柄打开逻辑 |
| [PInvokeExtensions.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Extensions/PInvokeExtensions.cs:35) | Win32 缓冲区封装 | 可改为针对 `uint` 的简化封装 |
| [Enums.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/Enums.cs:42) | 两组电池状态枚举 | 去掉资源显示属性即可独立使用 |
| [BatteryModeControl.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.WPF/Controls/Dashboard/BatteryModeControl.cs:7) | 界面下拉框 | 用自己的界面绑定同一组状态 |
| [BatteryNightChargeModeControl.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.WPF/Controls/Dashboard/BatteryNightChargeModeControl.cs:7) | 界面开关 | 用自己的界面调用开关接口 |

本地自动化步骤同样调用通用功能层，未增加新的充电模式。电量自动监听器每 30 秒检查一次电量变化，属于自动化触发支持，不是切换充电模式的必要依赖。

来源：[BatteryAutomationStep.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib.Automation/Steps/BatteryAutomationStep.cs:6)、[BatteryNightChargeAutomationStep.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib.Automation/Steps/BatteryNightChargeAutomationStep.cs:6)、[BatteryAutoListener.cs](/home/snail/code/lenovo/LenovoLegionToolkit/LenovoLegionToolkit.Lib/AutoListeners/BatteryAutoListener.cs:24)。

## 语言和程序结构建议

如果目标是 Windows，首选 **C# + .NET**。原项目使用 C#，界面为 WPF，读取到的构建配置目标为 .NET 9 Windows。C# 可以通过 P/Invoke 调用所需的 Win32 接口，最容易保留原有逻辑。

来源：[Directory.Build.props](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit/blob/master/Directory.Build.props)、[项目目录](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit)。

建议第一版先做 C# 命令行工具，支持查询、三个模式切换和夜间充电开关。硬件验证通过后，再加 WinForms 托盘菜单或 WPF 窗口。最小核心只需要枚举、设备句柄、IOCTL 调用、状态解析、写后验证和错误处理，无需把整个 LLT 库及其依赖带入。

建议对外接口：

```text
ProbeSupport()
GetChargeMode()
SetChargeMode(mode)
GetNightCharge()
SetNightCharge(enabled)
```

上述为建议接口名，不是原项目命令行参数。

C++ 或 Rust 也能实现相同 Win32 调用，但需要重写封装；如果只是实现这几个功能，没有必要因此增加迁移工作量。

如果目标是 Linux，不能直接沿用 Windows 的 `EnergyDrv` 调用，需要先确认内核驱动或 sysfs 暴露的功能，再选择语言。硬件接口可用性比语言选择更关键。

原项目说明 Windows 使用需要管理员账户，并提示部分机型依赖联想驱动；独立工具应明确区分权限不足、设备不存在、功能不支持和写入未生效。文档里的 IOCTL 适用性仍须在具体机器上确认。

来源：[README 的兼容性与驱动说明](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit#compatibility)。
