# TaskTodo

Windows 桌面任务小组件：贴边悬浮球/胶囊、悬停预览、玻璃与透明面板、多清单，任务只保存在本机。

## 下载安装

1. 打开本仓库的 [Releases 页面](../../releases/latest)，下载 `TaskTodo-v1.0.0-win-x64.zip`。
2. 解压到任意文件夹（例如 `D:\Apps\TaskTodo`），双击其中的 `TaskTodo.exe`。无需另装 .NET；请保留整个文件夹，只拷 exe 无法运行。
3. 程序没有数字签名，首次运行若出现“Windows 已保护你的电脑”，点“更多信息 → 仍要运行”。
4. 需要桌面图标时，右键 `TaskTodo.exe` →“发送到 → 桌面快捷方式”。

- 系统要求：64 位 Windows 10/11（在 Windows 11 上验证）。
- 数据位置：`%LOCALAPPDATA%\TaskTodo`，不联网、不上传。可在 齿轮设置 →“备份、导入与最近删除”中备份。
- 升级：先从托盘菜单“退出”，再用新版本文件夹覆盖旧文件夹，任务数据不受影响。
- 卸载：从托盘退出后删除程序文件夹；如需同时清除任务，再删除 `%LOCALAPPDATA%\TaskTodo`。

## 项目说明

目标：开发原生 Windows 11 桌面任务小组件；采用 C#/.NET 10/WPF、本地 C# 服务和 SQLite，不搭建 Web 或云服务。

已实现多清单任务面板、左右贴边胶囊、共享悬停预览、托盘找回，以及本地备份和恢复。任务使用真实 SQLite 持久化；悬停预览可以勾选和滚动，显式打开面板后编辑。

界面提供靛紫、珊瑚、玫瑰、琥珀四套配色，以及实色、玻璃、透明三种材质，均可在设置中切换；主窗、设置、胶囊、预览和数据管理使用统一控件样式。任务以扁平行显示，悬停时出现排序和菜单入口；小窗口会自动收紧间距。

发布目录位于 `artifacts/publish/win-x64`，运行其中的 `TaskTodo.exe` 即可，无需另装 .NET。请保留整个目录中的运行时与原生 SQLite 文件。该包在当前 Windows 主机验证；干净 Windows、多屏混合 DPI 与真实中文输入法仍需按 `docs/STATUS.md` 验收。

## 环境要求与运行

源码构建需要 Windows 11 x64 和 .NET SDK 10.0.401。`global.json` 固定该 SDK 功能带；依赖版本集中在 `Directory.Packages.props`。

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release
dotnet test TaskTodo.sln -c Release
dotnet run --project src/TaskTodo.App/TaskTodo.App.csproj
```

也可以用 PowerShell 7 运行 `scripts/build.ps1`、`scripts/test.ps1`、`scripts/publish.ps1`。若 `dotnet` 不在 PATH，可把 `TASKTODO_DOTNET` 环境变量临时指向 `dotnet.exe`。

启动后，清单选择框下的未完成数区域可拖动窗口，窗口边缘可调整尺寸；标题栏的 `×`、Alt+F4 或 Ctrl+W 收起到屏幕边缘（按设置显示胶囊条或图标球）；只有托盘菜单的“隐藏”或胶囊右键“隐藏到托盘”会连悬浮窗一起隐藏。悬浮窗在置顶开启时会不断回到最上层，可显示在无边框全屏应用之上，独占全屏的游戏除外。双击托盘图标或使用“显示/找回”恢复主窗，托盘菜单还提供“收起到边缘”“隐藏”和“退出”。

齿轮设置内可新建、重命名清单；顶部选择框切换清单。收起后，每个胶囊显示清单及未完成数。悬停 150 ms 显示预览，离开胶囊及预览区域 350 ms 后关闭；悬停和预览勾选不抢走外部应用的键盘焦点。点击胶囊或预览中的“打开”进行完整编辑。胶囊顶部手柄可拖动并吸附最近的左右边缘，设置或右键菜单也可切换方向；清单过多时可滚动。

设置中的“收起样式”可改为“图标球”：收起后只剩一个带未完成数量的圆形图标，点击展开当前清单，悬停显示预览，按住拖动可沿屏幕边缘移动并吸附左右；右键菜单可随时切换回胶囊条。

在底部输入任务并按 Enter 添加；双击标题或点击右侧 `⋯ → 编辑` 修改，Enter 保存、Esc 取消，离开编辑框会保存有效标题。使用复选框完成或取消完成，在“已完成”区域查看完成项；悬停任务行后，使用右侧排序手柄拖动未完成任务。删除后有 8 秒的“撤销”入口。

点击齿轮设置浅色、深色或跟随系统主题、配色和材质。“实色”面板不透明并带阴影；“玻璃”会对主窗和预览身后的内容做模糊（Windows 不支持时退化为普通半透明），默认不透明度 60%；“透明”不做模糊，身后内容清晰可见，默认 45%。两者都可调 15%—95%，此时面板铺满窗口、不显示阴影。不透明度只作用于背景画刷，文字和按钮保持清晰。外观和窗口位置约 500 ms 合并保存；任务操作立即提交。保存失败时显示错误并保留输入，可修改后重试。

托盘“退出”会提交有效编辑与新增框中的非空草稿，等待保存完成；失败时返回面板。下次启动恢复展开或贴边状态。“跟随系统”在启动和选择主题时读取系统设置。

开发验证可以使用独立目录，避免触及日常任务：

```powershell
$testDataDirectory = Join-Path $env:TEMP ('TaskTodo.Manual-' + [guid]::NewGuid().ToString('N'))
dotnet run --project src/TaskTodo.App/TaskTodo.App.csproj -- --data-dir $testDataDirectory
```

## 本地数据层

生产数据库默认路径为 `%LOCALAPPDATA%\TaskTodo\tasktodo.db`，备份位于同目录的 `Backups`。路径由 `TaskTodoStorageOptions.CreateDefault()` 通过系统 API 生成，也可注入其他绝对路径；自动化测试只使用 `%TEMP%\TaskTodo.Tests` 下的独立临时目录。

M2 提供任务/清单模型、标题校验、显式完成状态、软删除恢复、严格全集排序、设置快照、串行数据库工作线程、`FlushPendingWrites`、有序停止、`user_version` 迁移和 SQLite 在线备份。创建任务时由调用方生成请求 GUID；同一次新增重试必须复用该 GUID，以获得幂等结果。

齿轮 →“备份、导入与最近删除”提供恢复删除项、创建备份、JSON 导出/导入和恢复 SQLite 备份。导入前显示清单与任务数量，确认后先备份当前数据，再事务性替换。JSON 包含版本、清单、任务（含删除标记）和外观设置，不包含屏幕位置；支持最大 32 MiB 的 TaskTodo JSON。

备份使用 SQLite 一致性备份，自动保留最近 10 份。不要只复制正在使用的数据库主文件；通过界面创建备份或导出 JSON。恢复最近删除的任务会保留原 ID、内容和清单。

数据库损坏、未来 schema 版本和访问错误会显示启动错误窗口，提供重试、打开数据目录和选择备份恢复。只有用户确认恢复后才替换数据，原数据库及 WAL/SHM 保存在 `RecoveryOriginal-时间-GUID` 目录。常规运行中的恢复保留本机窗口位置；从启动错误窗口恢复会重新计算可见位置。

数据库和导出文件不加密，依赖当前 Windows 账户的文件权限。应用不会上传任务，也没有遥测。同一用户、同一数据目录只运行一个实例；再次启动会唤起已有窗口。不同测试目录可独立运行。

测试数据全部位于临时目录。App 测试包含真实窗口、独立程序 UI 自动化和 1000 条任务资源观测，需要可用的 Windows 桌面。设置 `TASKTODO_TEST_ARTIFACTS` 后输出截图和性能记录。`scripts/measure.ps1` 可单独运行资源观测；`TASKTODO_APP_PATH` 可指定发布目录中的 `TaskTodo.exe` 供进程测试验证。移动真实鼠标的悬停焦点测试需要独占鼠标，设置 `TASKTODO_EXCLUSIVE_MOUSE=1` 后才运行；默认明确跳过，非激活窗口消息与预览数据同步仍在默认测试中检查。

实际构建、测试结果与桌面验证步骤见 [docs/STATUS.md](docs/STATUS.md)。真实中文输入法、Ctrl+Z、鼠标排序手感和多屏 DPI 仍需手工验证。

## 文件用途

| 文件 | 用途 |
| --- | --- |
| AGENTS.md | 技术栈、分层、可靠性约束和执行规则 |
| START_WITH_CODEX.txt | 首轮 M0/M1 的原始任务指令 |
| docs/IMPLEMENTATION_PLAN.md | 产品范围、界面、本地后端、数据模型、状态机与实现原则 |
| docs/TASKS.md | M0—M6 分阶段开发任务 |
| docs/ACCEPTANCE.md | 功能、数据、Windows 桌面和发布验收矩阵 |
| docs/STATUS.md | 实现、命令结果与待执行的 Windows 验证 |

Codex 官方文档说明其会读取适用的 AGENTS.md；这里同时在启动指令中显式要求阅读其他规格文件。技术和参考资料的原始来源均列在 IMPLEMENTATION_PLAN.md 第 8 节。

下一轮继续时可直接输入：

```text
读取 docs/STATUS.md 和 docs/TASKS.md，在现有发布包上补完尚未验证的真实桌面与硬件验收，依据复现结果修正。保留既有代码，不重复初始化项目。记录实际结果，不能把单元测试或当前开发机启动当成混合 DPI、真实 IME 和干净 Windows 已通过。
```

当前视觉与原生交互均为独立实现，没有复制 PaperTodo 的代码、图标、字体或视觉素材。完整计划中的尺寸、延时、目录结构和数据模型是 TaskTodo 自身规格，不是对参考项目源码或性能的承诺。
