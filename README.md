# 电池充电助手 · BatteryCharge

独立的 Windows 电池充电小应用，使用 C#、.NET 10 和 WinForms。采用 Windows 11 风格的桌面界面，包含电池概览、应用设置和托盘菜单。

## 功能

| 功能 | 操作 |
| --- | --- |
| 普通充电 | 切换到普通模式 |
| 电池养护 | 启用设备固件提供的充电限制 |
| 快速充电 | 启用设备固件提供的快充模式 |
| 夜间充电 | 单独检测支持性，开启或关闭 |
| 开机自动启动 | 在窗口或托盘菜单中开启、关闭；登录后进入托盘 |
| 模式图标 | 普通为白色电池配深色插头、养护为蓝色叶片图标、快充为橙色闪电；状态未知为灰色感叹号 |
| 中英文切换 | 在“应用设置”或托盘的语言菜单选择简体中文、English，即时生效并保存 |
| 清理并退出 | 删除本应用的自动启动任务和配置，退出后可手动删除程序文件夹 |

打开应用后只读取设备状态。选择模式卡片后点击“应用充电模式”，或在托盘菜单选择模式，才会写入设置。夜间充电按钮也只响应用户点击；读取状态和更新界面不会触发写入。

应用通过自己的 Win32 P/Invoke 封装访问 `\\.\EnergyDrv`。写入整个命令序列时串行执行，写完后重新查询，最多验证 12 次；没有生效则提示失败。每项驱动操作最多等待 15 秒；超时后禁用本次运行中的后续驱动读写并允许退出，未返回的原生调用仍持有操作锁，不会与新命令交错。已发送的命令无法撤回，驱动恢复后需重新启动程序。启动时申请管理员权限，并防止应用多开。关闭窗口或最小化会收起到托盘，点击“退出”结束程序。

窗口初始采用 1020 × 760 的布局，会按当前显示器的可用区域限制尺寸，考虑 Windows 显示缩放及任务栏。缩窄窗口后侧栏改为顶部导航，模式卡片改为纵向排列；空间不足时可滚动查看。移动到不同缩放的显示器或从托盘恢复时，会重新检查窗口是否超出屏幕。

养护百分比和夜间策略由设备实现，应用不提供任意百分比或具体钟点设置。某项功能读取失败时，只禁用对应控件，并显示设备、权限或协议错误原因。

## Windows 11 风格界面

“概览”显示真实电量、供电状态、当前充电模式和最后读取时间。环形电量指示与百分比每 30 秒更新一次，只读取 Windows 的电池状态；没有电池或状态未知时显示“—”。设备充电模式通过“刷新状态”读取。

三个模式使用独立卡片。边框和单选圆点表示待应用的选择，“当前使用”标记表示设备读回的实际模式；应用成功并验证后才更新该标记。选择已经生效的模式时，应用按钮禁用。夜间充电提供独立的“开启夜间充电／关闭夜间充电”按钮，操作完成后显示读回结果；读取失败时按钮保持显示并标明不可用，旁边显示具体原因。自动启动使用独立开关。

“应用设置”集中管理语言、自动启动、托盘行为说明和应用数据清理。详细错误可展开查看，设备功能不可用时自动展开说明。界面跟随 Windows 的浅色、深色和高对比度设置，保留原生标题栏、系统窗口操作和 Windows 11 圆角；没有引入第三方 UI 包。

支持 Tab、空格和方向键操作；F5 刷新，Alt+1 打开概览，Alt+2 打开应用设置。实现与验证说明见 [桌面 UI 设计](docs/desktop-ui.md)。

## 自动启动与托盘图标

自动启动默认关闭。在“应用设置”打开“开机自动启动”开关，或点击托盘菜单中的同名选项，即可开启；关闭会删除本应用创建的登录任务。该设置独立于充电功能，设备不可用时仍可操作。

首次运行或关闭自动启动后，登录任务不存在是正常情况，会显示“已关闭”。查询任务返回 `0x80070002` 时兼容 .NET 映射的 `FileNotFoundException`；权限不足、任务计划服务异常等其他错误仍会显示具体原因。

计划任务中的账户标识支持 SID 和用户名（如 `电脑名\用户名`）。读回任务时会把用户名解析为 SID，再核对运行账户和登录触发账户，避免把同一账户的不同表示误判为“自动启动设置未生效”。其他账户、无法解析的账户或缺少运行账户的任务仍不会被修改。

应用需要管理员权限，因此通过 Windows 任务计划程序为当前运行账户创建 `BatteryCharge.Startup.<用户 SID>` 任务，使用最高权限和已登录用户的交互会话，不保存密码，不直接写注册表 Run 项。任务在该账户登录约 10 秒后执行 `BatteryCharge.exe --startup`，直接显示托盘图标。电池供电时也会启动，拔掉电源不会结束任务，任务没有运行时长限制。启用后不会立即再启动一个实例。

最高权限自动启动只接受本地固定磁盘中权限受保护的 EXE 和目录：文件及目录所有者必须是管理员组、SYSTEM 或 TrustedInstaller，普通权限账户不得有修改程序、在程序目录创建文件或替换路径的权限。建议使用 Program Files 下的专用目录；仅移动文件不一定会修正原有 ACL。用户可写的下载目录、桌面、共享目录、移动盘和目录链接不能注册为最高权限启动目标。读取已有任务时也会校验注册路径；不安全或不可访问的旧目标会自动停用，并显示红色提示。关闭、清理任务不受此路径限制。

请使用发布目录中的 `BatteryCharge.exe` 开启自动启动。启动或点击“刷新状态”时，会比较当前 EXE 路径与计划任务里的路径。自动启动已开启且路径不一致时，会显示：

> 已开启，但程序位置已变化；请关闭后重新开启以更新路径。

路径不一致时，概览页和设置页顶部都会固定显示带红色背景、红色边框和加粗标题的“自动启动需要处理”警告，滚动或刷新不会隐藏它。“查看启动设置”按钮可打开设置页，核对当前程序路径与计划任务路径。任务已禁用但仍指向旧路径时，也会提示路径问题；移除任务或重新配置到当前路径后，警告消失。颜色随浅色、深色和高对比度主题适配。

将“开机自动启动”关闭再开启，即可更新为当前 EXE 的路径。移动程序后，需要从新位置手动启动程序，才能检测到路径变化；旧路径缺失或不安全时，旧任务会先被停用。正常双击 EXE 仍会打开窗口。自动启动只读取设备状态，不会重新应用充电模式。

程序已经运行时，从相同位置、相同会话再次双击 EXE 会打开已有窗口，不启动第二个实例。从其他位置或会话启动时，会提示先退出已有实例，避免误以为正在运行新位置的程序。单实例对象使用管理员和高完整性级别边界的 Windows 私有命名空间，普通权限程序不能提前占用这些对象。重复执行 `--startup` 保持静默。升级此安全修复时，请先退出旧版程序；旧版与新版使用的实例机制不同。

托盘和窗口图标根据读回的实际模式更新；点击模式卡片尚未应用时不会改变图标。刷新、应用模式和操作失败后的状态重读都会更新图标，模式不可用时显示灰色。外部程序改变模式后，可点击“刷新状态”同步图标。所有图标使用透明背景，包含 16、20、24、32、40、48、64、128、256 像素尺寸。

模式图标的可编辑 SVG 与 ICO 位于 `src/BatteryCharge.App/Assets/`。可用 Python 3 标准库重新生成，无需安装额外依赖：

```bash
python3 scripts/generate-icons.py
```

## 界面语言

“应用设置”中的语言下拉框和托盘菜单中的语言选项均可切换 **简体中文 / English**。切换后窗口标题、按钮、模式名称、托盘菜单与提示、状态文字以及应用生成的错误说明同步更新，无需重启。Windows 自身提供的底层异常文字可能仍使用系统语言。

语言选择保存到 **`BatteryCharge.exe` 所在目录的 `settings.json`**，与启动命令的当前工作目录无关。复制或移动程序时可一起携带配置；下次手动启动或自动启动都会使用该语言，不写注册表。设置文件最大为 64 KiB，超过上限直接使用系统默认语言，不完整读取大文件。首次启动或设置文件缺失、损坏时，中文系统默认简体中文，其他系统默认英文。保存失败会提示原因，并保留原语言选择。

启动时只读取程序目录中的配置；该文件不存在时使用系统默认语言。

切换语言只保存语言偏好并重新读取状态，不应用充电模式、不修改夜间充电，也不创建自动启动任务；状态重读时可能停用不安全的旧任务。尚未应用的模式选择会保留。翻译资源位于 `src/BatteryCharge.Core/Resources/Strings.resx` 和 `Strings.en.resx`。

## 清理与卸载

在“应用设置”中点击 **“清理并退出”**，或使用托盘菜单的同名选项。确认后会：

1. 删除并确认移除当前运行账户的本应用自动启动任务；同名外部任务不会被修改。
2. 删除程序目录中的 `settings.json` 和本应用写配置时生成的 `.settings-<GUID>.tmp` 临时文件。
3. 提示完成并退出，随后可手动删除发布文件夹。

配置保存和清理会拒绝路径中的目录链接；Windows 上操作期间会锁住各级目录，防止检查后被重命名或替换。发现链接或目录被占用时会报错，不会沿链接清理其他目录。清理不递归删除其他文件，也不会删除正在运行的 EXE。当前充电模式和夜间充电设置由设备保留，不会因清理而改写。删除自动启动任务失败时保留配置；配置删除失败时会提示具体原因并保持程序运行，解决问题后可重试。卸载只清理当前运行账户创建的登录任务，其他账户需要分别清理。

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

检查使用模拟设备，不访问真实硬件，不依赖测试框架或第三方包。覆盖：只读探测、独立功能支持性、设备缺失、三种模式命令顺序、夜间开关、禁止写入不支持的夜间功能、写入无效、延迟生效、驱动失败、非法枚举，以及并发设置时完整命令序列不能交错。另检查自动启动任务的路径转义、电池供电设置、无限运行时长、禁用状态、移动后的旧路径、同名外部任务保护，以及语言偏好的保存、损坏文件回退和双语错误格式。清理检查使用临时目录和模拟任务删除，覆盖重复清理、保留无关文件及部分失败；这些检查不会注册或删除真实计划任务。

窗口边界检查覆盖小屏幕、显示缩放、任务栏偏移、多显示器负坐标和显示器断开后的窗口恢复；这部分验证尺寸与位置计算，不运行 Windows 界面。

自动启动查询回归检查覆盖任务不存在时的 COM 异常和 .NET 实际错误映射，并确认已有任务正常返回、权限和服务错误不会被当作“已关闭”。账户兼容性检查模拟 SID 与用户名的等价映射，确认运行账户和触发账户都可以使用用户名，并继续拒绝其他账户、无法解析的账户及缺失的账户标识。检查使用模拟查询与账户解析，不连接真实任务计划服务。

Windows 界面检查使用模拟设备与模拟启动注册，运行实际窗口并生成中英文、明暗主题、窄窗口和错误状态的截图，执行命令与检查范围见 [桌面 UI 设计](docs/desktop-ui.md)。

根目录的 `.github/workflows/build.yml` 支持推送 tag 和手动触发；普通分支推送与 PR 不触发构建。两种模式都执行 Linux 和 Windows 行为检查、Windows 界面检查，并上传界面截图（`BatteryCharge-ui`）和一个 Windows x64 发布包。发布包按“名字-Windows-架构-版本”命名，例如 tag `v1.0.1` 对应 `BatteryCharge-Windows-x64-1.0.1.zip`；程序内的版本号也采用该版本。版本支持 `v1.0.1`、`1.0.1` 和 `v1.0.1-rc.1` 等格式，前导 `v` 不计入版本号。CI 只生成不包含 .NET 运行时的版本，运行电脑需安装 .NET 10 桌面运行时。

手动构建：在 GitHub 的 **Actions → Build Windows application → Run workflow** 中选择分支，选填 `version`，然后运行。版本留空时使用 `1.0.0-manual.<运行编号>`。完成后可从该次运行的 Artifacts 下载 ZIP 和界面截图；手动模式只上传构建产物。工作流文件进入默认分支后才可手动触发，参见 [GitHub 手动运行工作流](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)。

Windows 界面检查及其截图上传是非阻断步骤：失败时记录警告和运行摘要，保留已有日志与可用截图，并继续构建、打包和发布。Linux 和 Windows 基础行为检查仍须通过；编译或打包失败也会停止发布。

Action 已固定到完整提交 SHA，checkout 不保留 Git 凭据。构建任务只持有仓库读权限；独立发布任务仅下载本次构建的制品并使用必要的仓库写权限。UI 诊断仍允许失败后继续发布。

推送 tag 时，构建和基础行为检查通过后，工作流自动创建对应 tag 的 GitHub Release 并附上 ZIP 文件；预发布版本标记为 Pre-release，重复运行时更新同名附件并保留现有说明。例如：

```bash
git tag v1.0.1
git push origin v1.0.1
```

Release Notes 采用组合生成：`scripts/release-notes.py` 用 `git log` 列出上一个主线祖先 tag 到当前 tag 之间的主线非合并提交，包含提交标题、作者和提交链接，覆盖个人直推变更；首次发布列出当前版本的全部主线非合并提交。再通过 `--notes-file` 配合 GitHub 的 `--generate-notes` 补充 Pull Request、贡献者和 Full Changelog 链接，两部分采用相同的起始 tag。工作流拉取完整历史与 tags，使用内置 `GITHUB_TOKEN`，仅 Windows 发布作业申请 `contents: write`，无需配置个人令牌。说明见 [GitHub 自动生成 Release Notes](https://docs.github.com/en/repositories/releasing-projects-on-github/automatically-generated-release-notes) 和 [GitHub CLI 发布命令](https://cli.github.com/manual/gh_release_create)。

`.github/dependabot.yml` 配置 Dependabot 每周一检查 GitHub Actions、`global.json` 中的 .NET SDK 和四个项目的 NuGet 依赖，发现更新时自动创建 PR。Actions 和 NuGet 更新分别合并为一组，SDK 不跨主版本升级，保持 .NET 10。当前没有第三方 NuGet 包，该配置也覆盖后续加入的包。Dependabot PR 不触发上述构建，也不会自动合并；配置进入 GitHub 默认分支后生效。配置选项见 [GitHub Dependabot 文档](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)。

如果 Windows 检查出现 `FileLoadException`、`0x800711C7` 和 `An Application Control policy has blocked this file`，表示系统策略拦截了程序集加载，检查无法完成；发布会停止。检查程序会在首次遇到这个错误时报告 `BLOCKED` 并退出，不继续重复报告功能失败。`-ExecutionPolicy Bypass` 只影响 PowerShell 脚本执行策略，不能解除 DLL 的应用控制拦截。使用下面的只读命令查看近期拦截事件，找到包含 `BatteryCharge.Core.dll` 的记录及其策略名称或 ID：

```powershell
Get-WinEvent -FilterHashtable @{
    LogName = 'Microsoft-Windows-CodeIntegrity/Operational'
    Id = 3077
} -MaxEvents 10 | Format-List TimeCreated, Message
```

具体来源可能是 Smart App Control 或其他 App Control 策略，需要根据事件确认。受管理设备应由策略管理员批准开发构建或签名；也可在允许开发构建的环境中运行检查。生成或下载的发布程序仍受运行机器的应用控制策略约束。参见 [微软 App Control 事件说明](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/operations/event-id-explanations) 和 [PowerShell 执行策略](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies)。

## Windows 实机验证

1. 启动程序并接受管理员权限请求；确认初始状态或具体失败原因。
2. 依次应用普通、养护、快速模式；确认成功提示、当前模式及托盘勾选同步变化。
3. 刷新，确认显示的是设备读回值；重新打开程序再次核对。
4. 在概览页向下滚动找到夜间充电，点击“开启夜间充电”或“关闭夜间充电”后刷新；若不可用，确认按钮仍显示并禁用，旁边展示具体原因。
5. 依次切换三种模式，确认托盘和窗口图标分别显示白色电池配深色插头、蓝色叶片图标、橙色闪电；仅改变模式卡片选择时图标应保持实际模式。
6. 在窗口开启自动启动，确认托盘菜单勾选同步；注销后登录，确认约 10 秒后程序仅出现在托盘，双击可以恢复窗口。电池供电时也应启动。
7. 关闭自动启动，确认任务计划程序中对应的 `BatteryCharge.Startup.<用户 SID>` 已删除，再次登录不应启动程序。
8. 关闭窗口后使用托盘菜单切换，最后点击“退出”。
9. 在窗口和托盘中分别切换中文、English，确认菜单、模式名称、状态与提示同步变化；重启后确认语言保留。设备不可用时，切换后的错误说明也应使用所选语言。
10. 确认程序旁生成了 `settings.json`；开启自动启动后执行“清理并退出”，确认任务和配置已移除、程序已退出，发布文件夹里的 EXE 和无关文件仍保留。重新启动应使用系统默认语言，且不会创建自动启动任务。
11. 在较小的屏幕和 125%、150%、200% 显示缩放下打开窗口，确认窗口不超出可用区域，文字随宽度换行，空间不足时可滚动查看。切换语言及移动到不同缩放的显示器后再次检查。
12. 程序在托盘运行时再次双击 EXE，确认已有窗口打开且没有“已在运行”提示；最小化后再试一次。已有实例运行时执行 `BatteryCharge.exe --startup`，应保持静默。退出后重新打开应正常启动。
13. 在系统设置中切换浅色、深色和高对比度，确认界面与标题栏同步更新；验证 F5、Alt+1、Alt+2、Tab、空格和方向键操作。缩窄窗口后确认顶部导航、纵向模式卡片和滚动区域可用。
14. 首次运行或关闭自动启动后点击“刷新状态”，应显示自动启动“已关闭”，没有 `0x80070002` 读取失败；重新开启后应正常创建任务并显示“已开启”。

如果另一个联想管理程序同时更改同一设置，设备状态可能变化；点击“刷新状态”读取实际值。本应用不自动恢复历史充电设置；只有用户主动切换自动启动开关时，才会注册或删除自己的登录任务。

## 当前验证情况

已在 Debian 13 / Linux x64、.NET SDK 10.0.401 环境运行独立行为检查，**30/30 项通过**；Windows 应用和界面检查项目的 Release 构建成功，**0 警告、0 错误**。检查使用模拟设备、模拟账户解析和临时配置目录，不访问真实硬件、不注册或删除真实计划任务。

安全回归检查还覆盖目录链接拒绝、配置大小限制、旧任务安全迁移，以及驱动超时后的排队/剩余命令停止；Windows 额外检查目录句柄锁、启动路径 ACL 策略和私有命名空间跨进程通信。测试使用临时目录和独立命名空间，不访问真实硬件。详细记录见 [安全审查与修复记录](docs/security-review.zh-CN.md)。

Windows 界面、托盘图标、登录自动启动，以及实机驱动读写尚未验证。请在 Windows 上运行发布脚本并按上述步骤实测；行为检查和跨平台构建成功不代表已通过硬件测试。

## 文件结构

```text
BatteryCharge.slnx            解决方案入口
Directory.Build.props        通用构建配置
global.json                  .NET SDK 版本选择
src/BatteryCharge.Core/       状态、协议及串行控制层
src/BatteryCharge.App/        独立驱动封装、WinForms 窗口及托盘
tests/BatteryCharge.Checks/   无硬件依赖的行为检查
tests/BatteryCharge.UiChecks/ Windows 界面检查与截图
docs/desktop-ui.md           桌面界面设计与验证说明
scripts/                     Windows / Linux 发布脚本
.github/workflows/           自动构建配置
LenovoLegionToolkit/          本地参考仓库，已被 Git 忽略
```

协议研究参考：[Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit)。
