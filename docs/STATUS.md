# TaskTodo 当前状态

日期：2026-10-13。

本轮已重做 UI：暖白/炭黑与柔绿强调色、统一控件皮肤、任务卡片、分组设置、胶囊和恢复对话框。最新截图、构建与交付记录见文末“UI 优化”；前述功能与人工验收边界保持。

M0—M5 功能已实现，默认入口连接真实 SQLite：多清单任务面板、左右贴边胶囊、共享悬停预览、单实例、最近删除、JSON 导入导出和备份恢复均已接通。M6 回归、性能观测和自包含打包完成；最终全量自动化 **108 通过、0 失败、1 明确跳过**，Release 构建 **0 警告、0 错误**；发布程序进程测试 **6 通过、0 失败、1 明确跳过**。可运行 `artifacts/publish/win-x64/TaskTodo.exe`。用户此前确认的手工验收仅覆盖 M1，真实 IME、鼠标焦点/拖动、混合 DPI/热拔插、系统注销、ACL 和干净 Windows 发布验证仍保留待验。

## 阶段状态

| 阶段 | 实现 | 自动化验证 | Windows 手工验证 |
| --- | --- | --- | --- |
| M0 | 已完成 | 通过 | 不适用 |
| M1 | 已完成 | 通过：7/7 + 启动烟雾检查 | 通过：用户确认 |
| M2 | 已完成 | 通过：35/35（Core 6 + Infrastructure 29） | 不适用：数据层未接入 GUI |
| M3 | 已实现 | 通过：51/51 总计（Core 6 + Infrastructure 29 + App 16）；窗口渲染/独立进程 UI 自动化通过 | 真实 IME、Ctrl+Z、拖动排序等待手工 |
| M4 | 已实现 | 多清单、共享预览、悬停状态机、非激活消息及虚拟化列表通过 | 独占鼠标/完整菜单拖动轨迹待验 |
| M5 | 已实现 | 数据恢复、导入导出、单实例、进程唤起与保存队列通过 | 混合 DPI/热拔插、注销、真实 ACL 待验 |
| M6 | 回归、观测、自包含目录交付完成 | 108 通过、0 失败、1 跳过；发布程序 6 通过、1 跳过；1000 项观测完成 | 干净 Windows 及前述人工项待验 |

以下 M0—M3 部分保留历史验证记录；其中“未实现”“未接入 GUI”等描述是当时的阶段边界，当前状态以顶部和文末 M4—M6 记录为准。

## 既有实现（M0 + M1）

### 实际改动

- 建立 `TaskTodo.sln`，包含 `TaskTodo.App`、`TaskTodo.Core`、`TaskTodo.Infrastructure` 和两个测试项目；依赖方向为 App → Core/Infrastructure、Infrastructure → Core，Core 不依赖 WPF。
- 使用 `global.json` 固定 .NET SDK 10.0.401；使用中央包管理固定 CommunityToolkit.Mvvm 8.4.2、Microsoft.Data.Sqlite 10.0.12、Microsoft.NET.Test.Sdk 18.10.1、xUnit 2.9.3 和 runner 3.1.5。
- 实现 `WindowStyle=None`、`AllowsTransparency=True`、透明窗口壳体和单独带 alpha 的背景画刷。透明度滑块只修改背景 Brush，未使用 `Window.Opacity`。
- 实现标题空白区 `DragMove`；交互控件不会触发拖动。使用 WPF `WindowChrome` 提供窗口边缘缩放，主窗最小尺寸为 280×260 DIP。
- 实现 128×38 DIP 的独立胶囊窗口；收起时主窗隐藏，胶囊定位到主窗所在显示器工作区右侧，点击胶囊恢复主窗。没有创建全屏或工作区大小的透明遮罩。
- 实现托盘双击找回和“显示/找回、收起到边缘、隐藏、退出”菜单；普通关闭和 Alt+F4 隐藏到托盘，只有托盘“退出”终止应用并释放托盘资源。
- 实现 Core 层窗口状态转换和同坐标系工作区约束；Desktop 层集中处理 Win32 显示器工作区到 WPF DIP 的转换，并声明 PerMonitorV2 DPI awareness。
- 新增 `scripts/build.ps1`、`scripts/test.ps1`、`.gitignore`，更新 README 运行说明。

### 环境与命令结果

- 初始 `dotnet --info`：系统 `C:\Program Files\dotnet` 只有 .NET 7.0.20/8.0.14 运行时，未安装 SDK；项目目录不是 Git 仓库，因此没有可报告的 Git 未提交状态。
- 为本轮验证仅在 Codex 临时工作目录安装微软官方 .NET SDK 10.0.202；`dotnet --info` 显示 Host 10.0.6、Windows Desktop Runtime 10.0.6、RID `win-x64`。该便携 SDK 未写入 TaskTodo 项目，也未修改系统 PATH。
- 后续按用户要求执行 `winget install --id Microsoft.DotNet.SDK.10 --exact`：成功安装系统级 .NET SDK 10.0.401、Host/.NET Runtime 10.0.12 和 Windows Desktop Runtime 10.0.12；`global.json` 已同步为 10.0.401。
- `dotnet restore TaskTodo.sln`：成功，还原 5 个项目。
- 首次 `dotnet build TaskTodo.sln -c Release --no-restore`：发现 Windows Forms/WPF 隐式类型名冲突和一个错误的 WPF override，共 3 个错误、1 个可空警告；已修正，未作为最终通过结果。
- 最终 `dotnet build TaskTodo.sln -c Release --no-restore`：成功，0 警告、0 错误。
- `dotnet test TaskTodo.sln -c Release --no-build --no-restore`：成功；Core 6/6、Infrastructure 1/1，共 7/7 通过，0 失败、0 跳过。
- `scripts/build.ps1 -Configuration Release` 与 `scripts/test.ps1 -Configuration Release`：分别使用新安装的全局 SDK 执行成功，结果为 0 警告、0 错误及 7/7 测试通过。
- 直接可执行文件启动烟雾检查：运行 Release `TaskTodo.exe` 后，进程保持运行且响应，获得非零主窗口句柄 `35132372` 和标题 `TaskTodo`；检查后终止该无数据测试实例。此项证明安装 .NET 后可直接启动 GUI，但不代表视觉、拖动、缩放或托盘交互通过。
- 自动化测试覆盖 Expanded/Docked/Previewing/HiddenToTray 基础转换、从隐藏/预览找回、负坐标工作区约束、超大窗口缩回工作区、非法几何拒绝，以及 Core 不引用 WPF/Infrastructure 的分层检查。

### Windows 手工验证结果

2026-09-20，用户确认 M1 桌面交互验证通过，覆盖以下单显示器操作：

1. 运行 `dotnet run --project src/TaskTodo.App/TaskTodo.App.csproj`，确认出现约 340×440 DIP 的圆角无边框窗口，窗口外透明且不存在覆盖屏幕的透明点击层。
2. 将背景透明度从 35% 调到 100%，观察仅背景变实，标题、说明文字、按钮和滑块本身没有一起变淡。
3. 从标题左侧空白区拖动；分别点击置顶、收起、关闭和透明度滑块，确认控件交互不误拖动窗口。
4. 从四边和四角缩放，确认最小尺寸为 280×260 DIP，并且窗口最终保持在当前显示器工作区内、不会覆盖到任务栏外侧。
5. 点击 `–`，确认主窗消失且屏幕右侧只留下约 128×38 DIP 胶囊；点击胶囊恢复主窗，并点击原主窗区域确认没有不可见遮挡。
6. 点击 `×` 或按 Alt+F4，确认窗口隐藏但进程仍在；双击托盘图标以及依次使用“显示/找回、收起到边缘、隐藏”验证可恢复。
7. 将窗口拖到屏幕外缘后使用托盘“显示/找回”，确认窗口完整回收到可见工作区。负坐标副屏和不同缩放率仍属于 M5，待验证。
8. 使用托盘“退出”，确认进程结束、托盘图标消失且原窗口/胶囊区域不再拦截鼠标。

结论仅适用于上述 M1 原型范围。透明度/置顶设置重启保持、任务数据持久化、悬停预览、混合 DPI 和显示器断开恢复尚未实现或尚未验证，不能由本次确认外推为通过。

### 已知限制与设计取舍

- 本轮首次实现时全局没有 SDK，曾使用隔离的 10.0.202 SDK完成验证；随后已通过微软 Winget 源安装全局 .NET SDK 10.0.401（含 Windows Desktop Runtime 10.0.12），现在可直接执行 README 中的命令。
- M1 背景透明度和置顶状态不持久化；位置持久化、SQLite 和真实任务操作属于 M2/M3。
- M1 胶囊固定在当前工作区右侧，只实现显式收起/恢复；左侧停靠、多个胶囊、悬停预览与完整交互守卫属于 M4。
- 当前只完成目标显示器工作区查询和 DIP 转换的基础路径；混合 DPI、显示器断开和任务栏变动的完整回归属于 M5，现阶段均待验证。
- 托盘使用系统应用图标，没有复制 PaperTodo 的品牌、图标、字体或其他素材。

## 既有实现（M2，2026-09-20）

### 代码完成

- 在 Core 中补齐 `TodoTask`、`TaskList`、`SettingsSnapshot`、`TaskTodoSnapshot`、标题/设置校验、可识别业务错误、`ITaskTodoRepository` 和 `TaskTodoService`。模型使用独立 GUID 与 UTC 时间；任务标题按纯文本规范化，首尾空白被移除，换行转为空格，空标题和超过 500 字符的标题被拒绝，同名任务允许存在。
- 实现创建任务、改标题、显式 `SetCompleted(id, bool)`、软删除、恢复、未完成任务全量排序、创建/重命名清单、快照、设置保存、备份、排空和有序停止。重复设置相同完成状态不会刷新完成时间，取消完成会清空 `CompletedAtUtc`；未完成数量由未删除且未完成任务实时派生。
- 以请求 GUID 作为新增实体 ID，重试相同新增请求不会生成第二条任务或清单；队列接受写入后不再由调用方取消，避免“数据库已提交但调用方误判失败后再次新增”的歧义。
- 在 Infrastructure 中实现单后台线程、单连接、FIFO 的 `SerialDatabaseWorker`。数据库操作不使用无序 `Task.Run`，业务写入在事务提交后才完成；失败回滚并映射为可识别错误，`FlushPendingWrites` 作为队列屏障，停止时先排空已接收工作再关闭连接。
- 创建 `TaskLists`、`TodoTasks`、`Settings`，启用外键、完成时间状态约束、必要索引和 `PRAGMA user_version=1`。默认“收件箱”仅在清单表为空时创建，重开不重复创建，用户重命名后不会被还原。
- 正式路径由系统 API 解析为 `%LocalAppData%\TaskTodo\tasktodo.db`，同时保持路径可注入；测试全部使用 `%TEMP%\TaskTodo.Tests\<guid>`，本轮未打开或修改真实用户数据库。
- 区分首次建库、正常版本迁移、不支持的新版本、权限/锁定/I/O 错误和损坏数据库；已有数据库无法打开时不会创建空库覆盖，也不会自动降级或破坏性重建。
- 迁移前使用 SQLite 在线备份 API 生成一致性备份，备份完成并通过 `quick_check` 后才继续迁移；备份失败会阻止迁移。备份不是直接复制可能仍有活跃 WAL 的主文件。
- SQL 仅位于 Infrastructure；Core 不引用 WPF、Dispatcher、Win32 或 SQLite，App/Window/ViewModel 中没有 SQL。日志和错误消息不记录任务标题或正文。

### 自动化验证与命令结果

- 开始前重新检查源码和分层；目录不是 Git 仓库，因此没有可用的 `git status` 未提交清单，但未删除或重建任何既有文件。`dotnet --info`：SDK 10.0.401、Host/Windows Desktop Runtime 10.0.12、RID `win-x64`，与 `global.json` 一致。
- M2 开始前基线：`dotnet restore TaskTodo.sln`、`dotnet build TaskTodo.sln -c Release --no-restore` 和 `dotnet test TaskTodo.sln -c Release --no-build --no-restore` 均退出码 0；既有测试 7/7 通过，作为 M0/M1 实际状态检查，不引用上一轮回复代替证据。
- 最终 `dotnet restore TaskTodo.sln`：退出码 0，5 个项目还原成功。
- 最终 `dotnet build TaskTodo.sln -c Release --no-restore -p:BaseOutputPath='<项目目录>\artifacts\verify\bin\'`：退出码 0，5 个项目构建成功，0 警告、0 错误。
- 最终 `dotnet test TaskTodo.sln -c Release --no-build --no-restore -p:BaseOutputPath='<项目目录>\artifacts\verify\bin\'`：退出码 0；Core 6/6、Infrastructure 29/29，共 35/35 通过，0 失败、0 跳过。
- 默认 Release App 输出目录被用户当时正在运行的 `TaskTodo.exe`（PID 222308）占用；一次默认输出构建在复制 Core DLL 时退出码 1。未终止该进程，改用上述独立 `BaseOutputPath` 完成全解构建。此文件占用不属于源码编译错误。
- 一次同时改写 `BaseIntermediateOutputPath` 的隔离构建因新 `obj` 目录没有 restore 资产而以 NETSDK1004、退出码 1 结束；随后保留正常中间目录、仅隔离最终输出并通过。失败尝试不计入通过结果。

### DATA-01～DATA-05 对照

| 验收项 | 结果 | 自动化证据 |
| --- | --- | --- |
| DATA-01 | 通过 | 临时 SQLite 首次初始化、关闭重开、v0→v1 迁移、默认收件箱不重复、重命名后不复原、未来版本拒绝、损坏字节保留。 |
| DATA-02 | 通过 | 空/超长标题、换行规范化、非法任务/清单 ID、设置语义校验、外键和完成状态约束；同名任务被允许。 |
| DATA-03 | 通过 | 排序必须提交当前清单完整活动未完成集合；重复、遗漏、外部清单、已完成、已删除和不存在 ID 均被拒绝，完成/删除任务顺序键保持不变。 |
| DATA-04 | 通过 | 通过临时数据库触发器注入排序中途失败，确认事务整体回滚且无部分更新。 |
| DATA-05 | 通过 | 并发提交经同一专用工作线程串行处理；重复完成幂等、取消完成清空时间、最后状态持久化，停止前排空且停止后拒绝新工作。 |

其他覆盖包括真实持久化重开、任务/清单新增请求跨重开幂等、软删除恢复并保留原字段、设置保存重开、在线 WAL 备份可独立打开、迁移备份失败阻断、队列线程与提交顺序、快照稳定排序以及关闭前排空。故障注入只作用于临时数据库。

### 未执行与边界

- 未修改真实用户目录权限，也未对 `%LocalAppData%\TaskTodo` 建库；Windows 文件“只读”属性不能可靠模拟 SQLite 目录 ACL 拒绝，因此真实 OS ACL 权限失败仍标记为待验证。当前只自动验证了权限异常到可识别错误的确定性映射。
- 真实跨进程数据库锁竞争待 M5 验证；本轮已实现 busy/locked 错误映射和单进程内单连接串行化。
- M2 是非可视数据层阶段，没有新增 Windows GUI 手工验收项。当前正在运行的窗口仍是 M1 占位界面；任务数据绑定、错误反馈和重启后的界面状态恢复属于 M3，不能由 35/35 自动化测试外推为 GUI 已通过。
- 未实现删除清单、永久删除、跨清单移动、账号、HTTP 服务、云同步、网页前端、笔记编辑器或插件，也未提前实现 M3/M4 功能。

### 当时的下一阶段入口

M3：将现有 `TaskTodoService`/SQLite 仓储接入 WPF 生命周期与收件箱界面，实现真实任务行、新增/编辑/完成/删除撤销、排序、设置和错误反馈；继续使用同一持久化队列，并在 GUI 关闭前调用排空与有序停止。

## 本轮实现（M3，2026-10-13）

### 任务面板与代码取舍

- App 启动时打开真实 SQLite，载入唯一的 TaskStore 提交快照，再创建主窗和胶囊。默认数据目录仍是系统 API 解析的 LocalAppData；开发验证支持显式 `--data-dir <绝对路径>`。
- 主窗展示真实任务、未完成数、新增框和可折叠的已完成区。支持纯文本/长标题换行、新增、双击或菜单编辑、Enter 提交、Esc 取消、失焦提交、显式完成状态、软删除及最近一次删除的 8 秒撤销。
- 任务写入使用已有后台串行队列。界面通过一个提交状态防止重复操作，确认成功后发布快照；失败显示中文错误并保留草稿，重试会使用当前修改后的输入。Checkbox 的鼠标、键盘和辅助功能 Toggle 都提交显式目标值，绑定只反映成功结果。
- 未完成项通过专用手柄排序，提交完整 ID 序列；成功后读取服务快照，保持顺序与更新时间一致。完成项和已删除项不能加入排序。
- 主题、背景不透明度、置顶、显示器标识、局部 DIP 位置和最后可见形态持久化。窗口位置和外观按约 500 ms 合并保存；主窗与胶囊背景使用画刷透明度，窗口及文字保持不透明。主题资源只在主题改变时更新。
- MonitorService 统一处理 Win32 物理像素和保存的显示器局部 DIP，并在当前屏验证找回/恢复。多屏混合 DPI 与显示器热拔插仍须在 M5 验收。
- 正常退出先提交有效编辑及非空新增草稿，再保存最终设置、排空队列并停止数据库线程；失败保留可继续操作的窗口。关闭主窗仍隐藏到托盘；下次启动恢复 Expanded/Docked。
- 输入框单独处理 WPF 输入法组合事件，候选确认期间不触发新增/取消；未注册全局 Ctrl+Z。真实输入法行为仍待手工确认。
- 启动失败提供重试、打开数据目录和退出；损坏数据库保留原文件。完整备份选择/恢复流程属于 M5。
- 按“少些防御性的代码”的要求，WindowManager 改为构造时完整组装窗口，去掉每个动作中的重复空值检查；排序集合校验统一放在数据库事务内；事务依靠 using 的生命周期自动回滚，去掉嵌套 catch/二次回滚。移除固定深色的背景转换器和重复透明度钳制，边界校验继续由 Core 负责。

### 环境、完整命令与结果

Windows 11（10.0.26200）、PowerShell 7.6.5、.NET SDK 10.0.401、Windows Desktop Runtime 10.0.12。目录仍不是 Git 仓库；保留既有文件并直接修改对应阶段代码，未进行提交或推送。本轮命令工具的沙箱初始化出现 `helper_sandbox_lock_failed`，经工具自动审批在沙箱外执行检查和验证；所有故障测试均限定在独立临时目录。

改动前基线：

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release --no-restore
dotnet test TaskTodo.sln -c Release --no-build --no-restore
```

结果：均退出码 0；0 警告、0 错误；Core 6 + Infrastructure 29，共 35/35 通过。

新增 App 测试项目：

```powershell
dotnet sln TaskTodo.sln add tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj
```

最终验证实际命令：

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release --no-restore
$env:TASKTODO_TEST_ARTIFACTS = '<项目目录>\artifacts\m3-ui'
dotnet test TaskTodo.sln -c Release --no-build --no-restore --logger 'trx;LogFilePrefix=M3-final' --results-directory 'artifacts/test-results'
```

结果：均退出码 0；6 个项目构建成功，0 警告、0 错误；Core **6/6**、Infrastructure **29/29**、App **16/16**，总计 **51/51**，0 失败、0 跳过。TRX 证据位于 `artifacts/test-results/M3-final_net10.0_20261013105224.trx`、`...105225.trx` 和 `...105231.trx`。

实现过程曾出现未接完构造参数、WPF/Forms ColorConverter 类型歧义、测试工程 System.IO 隐式导入缺失，均已修正。显式添加 UIAutomation 引用产生过 4 条冗余引用警告，已移除并使用 WPF 自带引用。一次独立进程测试过早终止程序，未等待 500 ms 窗口状态保存，还遇到 Windows 释放已终止进程文件句柄的短暂延迟；测试现明确等待 Expanded 设置落盘，并在结束测试进程后留出 100 ms 清理时间。失败尝试不计入通过结果。

针对独立程序复核执行：

```powershell
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AppProcessTests' --logger 'console;verbosity=normal'
```

最终结果：退出码 0，2/2 通过。

### 已取得的验证证据

| 范围 | 实际结果 |
| --- | --- |
| 新增、编辑、错误保留 | 临时数据库触发器注入失败；输入、原标题与已确认快照保持，修正后重试成功；同名任务与 500 字符标题可用。 |
| 重复提交与共享状态 | 等待事务锁时不提前更新快照、不接受重复点击；完成状态、时间与两个 ViewModel 的数量同步。 |
| 删除、排序 | 撤销恢复同一 ID；8 秒到期后任务仍为软删除；重排重开保持，写入失败时视觉顺序和数据库均保持旧值。 |
| 设置与退出 | 主题、背景不透明度、置顶、局部位置和形态重开保持；退出前提交编辑/新增草稿，失败后继续可编辑。 |
| WPF 窗口 | 在 STA/Dispatcher 中创建真实主窗与胶囊，检查绑定、默认及最小尺寸、当前显示器位置恢复，未发现绑定错误。 |
| 输入路由 | 模拟 WPF 组合开始/结束及 Enter，确认候选期间不新增；组合结束后的 Enter 添加一次；Esc 取消编辑。此项不是实体中文输入法验收。 |
| 独立进程 | 从生成的 TaskTodo.exe 启动，使用 UI Automation 新增、完成、展开已完成区、取消完成、收起、点击胶囊打开；读取临时库确认提交，再结束该测试实例并重开确认记录保持。关闭主窗后进程继续存在。 |
| 损坏文件 | 独立程序打开隔离的损坏测试库，显示重试/数据目录入口；退出后逐字节确认原文件不变。 |
| 渲染检查 | 实际生成并查看 `artifacts/m3-ui/panel-light.png`、`panel-dark.png`、`settings-dark.png`、`panel-minimum.png`、`capsule.png`，没有观察到任务布局溢出或文字随背景整体变淡。 |

本轮没有以真实用户数据库做测试，也没有修改系统启动设置。测试正常结束后无遗留 TaskTodo 测试进程；失败尝试留下的少量临时测试目录不作自动清理。

### 桌面手工验证步骤（待执行）

1. 使用 README 的临时数据目录命令启动。新增中英文、换行粘贴、同名任务和 500 字符长标题；空白和超长输入应提示错误并保留输入。
2. 用系统中文输入法组词，按 Enter 确认候选，确认此时未新增；再按 Enter 只新增一次。在编辑框验证 Ctrl+Z 撤销文字、Esc 取消以及 Enter/失焦保存。
3. 勾选任务，在“已完成”中取消完成；从 `⋯` 删除并在 8 秒内撤销。观察主窗和胶囊未完成数一致。
4. 仅从点状手柄拖动排序，在行上半/下半松开分别插入其前/后；重启后确认顺序保持。文字选择、标题拖动和任务排序应互不误触发。
5. 切换浅/深色，调整背景不透明度到 35% 和 100%，确认文字清晰；切换置顶，拖动/缩放窗口，退出并重开验证设置与位置。
6. 用收起按钮或 Ctrl+W 切到胶囊，点击胶囊打开；用 ×/Alt+F4 隐藏，用托盘找回。打开菜单、编辑和输入法组合期间，检查不会意外折叠或遗留点击遮挡。
7. 在有效编辑和新增草稿存在时用托盘“退出”，重开确认都已保存。多屏、不同 DPI、显示器断开另按 M5 矩阵验收。

### 已知限制与下一阶段入口

- M3 为单清单面板，展示已有快照中的第一个清单；多清单创建/重命名/切换、左侧停靠和悬停预览属于 **M4**。
- 真实中文 IME、Ctrl+Z、手柄拖动手感、托盘完整手工回归待验证；自动化结果不能替代这些结论。
- 当前屏坐标恢复已自动化检查，混合 DPI、屏幕热拔插、任务栏工作区改变尚未验收。
- “跟随系统”在启动或选择主题时读取；实时监听系统主题变化未加入本阶段。
- 最近删除、备份恢复界面、导入导出、单实例和系统注销处理在 **M5**。此时应只运行一个使用相同数据目录的实例。
- 下一轮入口：在现有 TaskStore、单个主窗/胶囊和 Core 状态机基础上完成 **M4 多清单、贴边胶囊队列、共享悬停预览**，并继续保留 M3 待手工项。

## 本轮实现（M4—M6，2026-10-13）

### 功能与实现

- 多清单：新增、重命名、切换清单，选中清单持久化；切换前保存有效编辑和非空新增草稿。主窗、胶囊、预览读取同一已提交快照，重命名/排序的重试保留原清单 ID。
- 贴边交互：左右停靠、顶部手柄拖动、多个胶囊滚动；共享预览在悬停 150 ms 后出现、离开 350 ms 后收回。Core 的悬停状态机取消过期计时，保留胶囊至预览的过渡区域。160 ms 动画尊重系统动画偏好。
- 预览：允许勾选、滚动和显式打开面板；通过 `WS_EX_NOACTIVATE`、`WM_MOUSEACTIVATE` 保持非激活契约。菜单、鼠标捕获、拖动、编辑、IME 组合及未完成保存均参与收回判断；隐藏主窗的设置状态不再阻止预览。
- 数据管理：最近删除恢复、一致性备份、带版本 JSON 导出/导入和 SQLite 备份恢复。导入先校验和展示数量，确认后先备份再事务替换；保留本机窗口位置，清除旧数据的撤销/重试/错误状态，同步应用导入外观。取消导入保留原状态。
- 启动恢复：损坏/不支持版本/访问失败提供重试、打开目录和选择备份。恢复前保留原数据库与 WAL/SHM；从启动错误页恢复时重置窗口位置。对话框恢复期间不允许关闭或注销打断。
- 生命周期：同一 Windows 用户、同一规范化数据目录使用互斥体和当前用户限定命名管道；再次启动唤起已有窗口。托盘退出和系统会话结束等待有效编辑与保存，失败保留窗口。打开模态对话框时保护其生命周期并暂停旧设置自动保存。
- 显示器：封装物理像素与目标屏局部 DIP 转换，监听显示器、DPI 与工作区变化，重新约束主窗、胶囊和预览；混合 DPI/热拔插需要真实硬件确认。
- 1000 项优化：主窗和预览改为受约束、可回收的虚拟化任务列表；切清单一次替换集合，外观设置不再逐项刷新任务。任务行异步操作捕获原任务 ID，避免容器回收或 DataContext 变化后误操作另一项。
- 交付：增加 `scripts/publish.ps1` 与 `scripts/measure.ps1`；发布保留 .NET/WPF/SQLite 原生依赖的 win-x64 自包含目录。

没有引入通用重试框架或静默空数据兜底。校验保留在用户输入、导入文件和事务边界；业务失败通过明确错误返回，不吞保存错误。测试只使用临时目录，没有向真实 LocalAppData 注入任务，也没有修改开机自启或推送远端。

### 本轮验证环境

Windows 11 10.0.26200；PowerShell 7.6.5；.NET SDK 10.0.401，Windows Desktop Runtime 10.0.12。工作目录 `<项目目录>` 不是 Git 仓库，无法生成 Git 差异或提交记录，已有文件保留。

### 最终还原、构建与测试命令

在项目根目录以 PowerShell 7 执行：

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release --no-restore
$env:TASKTODO_TEST_ARTIFACTS = '<项目目录>\artifacts\m6-ui'
dotnet test TaskTodo.sln -c Release --no-build --no-restore --logger 'trx;LogFilePrefix=M6-complete' --results-directory 'artifacts/test-results'
```

全部退出码 0。构建 0 警告、0 错误；Core **12/12**、Infrastructure **55/55**、App **41 通过 / 0 失败 / 1 跳过**，合计 **108 通过 / 0 失败 / 1 跳过**。App 默认测试约 35 秒。被跳过的是要求独占实体鼠标的 `PreviewProcessTests`，不是未实现功能伪造通过。

最终 TRX：`artifacts/test-results/M6-complete_net10.0_20261013120729.trx`、`M6-complete_net10.0_20261013120730.trx`、`M6-complete_net10.0_20261013120804.trx`。最终 WPF 烟雾测试已通过，之前超时记录仅为历史失败证据。

`build.ps1`、`test.ps1`、`publish.ps1`、`measure.ps1` 均检查原生命令退出码；在独立 PowerShell 子进程将 `TASKTODO_DOTNET` 指向不存在的程序，4 个脚本均返回退出码 1。未修改会话外环境变量。

### 1000 项性能观测

来源为最终默认测试生成的 `artifacts/m6-ui/performance-observation.json`：启动至窗口约 **1.57 秒**；10 轮上下滚动 **0.43 秒**；30 次胶囊打开/收起 **6.42 秒**。窗口句柄数（包括隐藏窗口）**21 → 21**；工作集 **38.51 MiB**，私有内存 **130.25 MiB**，句柄数 **856**。

切换结束等待 1 秒后连续取三个约 2 秒样本，CPU 按单核口径为 **99.77%、1.56%、0.78%**，6.02 秒累计约 **34.00%**。第一段仍有明显尾部工作，后两段下降；本次没有观察到持续忙循环或 HWND 增长，但不能据此断言长期无泄漏或全部空闲阶段低占用。耗时包含 UI Automation 调度，不是绘制帧率或纯应用微基准。

### 自包含发布目录

实际执行：

```powershell
& .\scripts\publish.ps1
```

脚本内部命令：

```powershell
dotnet publish src/TaskTodo.App/TaskTodo.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/publish/win-x64
```

退出码 0，产物为 `artifacts/publish/win-x64/TaskTodo.exe` 及整个同级目录。发布目录含 487 个文件，约 173.79 MiB（文档更新会使大小略变）；已核对 `coreclr.dll`、`hostfxr.dll`、`PresentationFramework.dll`、`e_sqlite3.dll`。`TaskTodo.runtimeconfig.json` 使用 `includedFrameworks`，包含 Microsoft.NETCore.App 和 Microsoft.WindowsDesktop.App 10.0.12。

发布目录携带 README 和三份状态/任务/验收文档。运行时必须保留整个目录；此产物是自包含目录包，不是安装程序，也未做干净 Windows 验收。

首次发布进程测试有 5 项通过、1 项失败、1 项跳过：旧 `AppProcessTests` 仍用独立窗口查找代码，窗口出现时 QuickAdd 自动化元素尚未就绪。已删除重复的启动/窗口/停止辅助代码，统一复用 WPF 提供程序识别，并在实际目标控件可用后操作一次；不重试业务点击、不吞失败。发布产品代码未改变，修正后任务操作/重启和损坏库保护的定向测试 2/2 通过，构建仍为 0 警告、0 错误。

最终从发布目录运行的命令：

```powershell
$env:TASKTODO_APP_PATH = '<项目目录>\artifacts\publish\win-x64\TaskTodo.exe'
$env:TASKTODO_TEST_ARTIFACTS = '<项目目录>\artifacts\m6-published'
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProcessTests' --logger 'trx;LogFilePrefix=M6-published-final' --results-directory 'artifacts/test-results'
```

退出码 0；**6 通过、0 失败、1 明确跳过**，约 14.4 秒。包括实际发布程序的任务新增/完成/取消/收起/重启、损坏库保护、多清单窗口、备份选择恢复、单实例和同时启动；跳过项仍为独占鼠标焦点测试。证据：`artifacts/test-results/M6-published-final_net10.0_20261013121459.trx`；之前定向 2/2 为 `M6-published-app-final_net10.0_20261013121426.trx`。所有进程使用临时数据目录。

### 自动化证据范围

| 范围 | 实际检查 |
| --- | --- |
| Core | 12 个用例：窗口状态、工作区/边缘几何、悬停延迟/过渡区域/过期计时/交互守卫。 |
| Infrastructure | 55 个用例：原有事务/持久化/故障测试，以及 JSON 往返、删除标记、无效关联/版本、备份失败不替换、恢复保留旧文件、本机位置保留与启动恢复位置重置。 |
| ViewModel | 多清单草稿提交/失败保留、原清单重试、导入确认与取消、最近删除、两视图外观/完成状态同步、退出准备与单实例通信。 |
| WPF | 真窗口渲染、绑定与输入路由、预览非激活消息和 Toggle 提交；1000 项真实滚动、视口容器数量、末项完成，以及待提交操作期间显式重绑容器仍使用原任务 ID。 |
| 独立程序 | 新增/完成/取消完成/收起/打开/重启持久化、多清单创建/重命名/数据管理、重复及同时启动、损坏库不覆盖、系统文件选择框恢复备份并保留原文件。 |
| 性能观测 | 临时 1000 任务、3 清单，10 次上下滚动、30 次胶囊打开/收起、3 段连续 CPU 采样和包括隐藏窗口的 HWND 数量。 |

截图在 `artifacts/m6-ui`：`panel-light.png`、`panel-dark.png`、`settings-dark.png`、`panel-minimum.png`、`capsule.png`、`preview-dark.png`、`data-tools-dark.png`、`list-editor-dark.png`、`panel-thousand-tasks.png`。已检查预览、数据管理、清单编辑和长列表截图，未观察到内容溢出；长任务采用省略/滚动。截图为 WPF 渲染证据，不代表鼠标手感或跨屏验收。

实现中发现并修正：1000 行全部生成造成 UI Automation 遍历长时间停顿；WPF 窗口出现后可能先暴露原生标题栏代理，需要等真正 WPF 提供程序就绪；文件选择按钮可能先存在后启用；测试持 SQLite 锁期间不应等待窗口焦点/布局。综合 WPF 烟雾测试整体上限调整为 90 秒，并输出阶段耗时，内部业务等待仍为 2 秒。失败尝试保留在 `artifacts/test-results`，不计为通过。

### Windows 手工验收步骤与剩余限制

以下均使用 README 的 `--data-dir` 临时目录命令，避免影响日常任务。

1. 新建两个清单，在一个清单输入有效编辑和新增草稿后切换；重开确认原清单数据和选中清单保存。真实中文 IME 确认候选不提前新增，编辑 Ctrl+Z/Esc/Enter 行为正确。
2. 收起到左右边缘并拖动顶部手柄；鼠标从胶囊慢速经过间隙进入预览，快速切换胶囊、滚动、勾选、开菜单，再点击打开面板。用另一应用输入时悬停，确认键盘焦点保持；检查隐藏后原区域不拦截鼠标。
3. 删除后从最近删除恢复；创建备份、导出 JSON、修改临时任务，再确认导入/恢复；确认导入计数与恢复结果一致。用无效 JSON 验证旧数据保留。真实只读目录 ACL 拒绝另行验证。
4. 双击启动两次、托盘隐藏/显示/退出；有效编辑时退出再重开。在可中断的测试桌面执行注销，验证保存和恢复；本轮没有主动注销用户会话。
5. 在 100%/125%/150%/200% 缩放、负坐标副屏和混合 DPI 双屏上拖动/停靠；断开副屏、改变任务栏工作区后从托盘找回。
6. 将完整发布目录复制到没有 .NET SDK/运行时的 Windows 11 x64，运行 `TaskTodo.exe`，检查增改任务、托盘、重启和数据目录。当前主机已装 SDK，不能替代干净系统验收。

真实鼠标焦点测试需独占鼠标，默认显式跳过；可在独占桌面设置 `TASKTODO_EXCLUSIVE_MOUSE=1` 后运行 `PreviewProcessTests`。默认测试仍检查非激活窗口消息、程序化显示时的前台窗口及预览提交同步。真实 IME/Ctrl+Z、拖动手感、混合 DPI/热拔插、真实 ACL 拒绝、系统注销和干净 Windows 均未完成验收。

“跟随系统”仍在启动/选择主题时读取，不实时响应系统主题变化；托盘沿用系统应用图标。第一段资源采样可能仍包含切换与 UI 自动化尾部开销，不能把短时观测当长期性能保证。

下一阶段入口：在现有发布包上完成上述真实桌面与硬件验收，依据复现结果修正；不重新初始化工程。TASKS.md 保留这些未勾选项。

## UI 优化（2026-10-13）

### 本轮变更

- 重排主窗：独立品牌/工具栏、大号清单选择、未完成数量、圆角任务卡片及整合新增框。保留默认 340×440 与最小 280×260 DIP。
- 用暖白/炭黑底色和柔绿强调色统一浅深主题；统一下拉框、选项、按钮、复选框、滑块、细滚动条、展开区、菜单和键盘焦点。原生 UIA 控件和模板部件保留；清单选择继续按真实 Name 搜索。
- 任务菜单与右侧排序手柄在悬停/行内键盘焦点时显示，已完成项弱化；行内编辑、显式完成和虚拟化回收逻辑保持。
- 设置分为清单管理、外观、停靠卡片，底部改为“完成设置”；任务模式保留新增入口。低于 340 DIP 高度使用紧凑标题/行间距，隐藏装饰性空态图标，让已完成区和新增框保持可操作。
- 胶囊增加数量徽标、当前清单标记与精细拖柄；预览统一标题、数量、任务和打开入口。只新增 UI 用的清单选中状态，不改数据库结构。
- 重做清单编辑、数据管理和启动恢复的间距、分组、空态与主次按钮。仍使用本地 SQLite 和现有提交/恢复流程，没有引入第三方 UI 框架或图像资产。

### 实际验证

项目仍不是 Git 仓库；PowerShell 7.6.5、SDK 10.0.401 与当前 Windows 环境不变。实际命令：

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release --no-restore
$env:TASKTODO_TEST_ARTIFACTS = '<项目目录>\artifacts\ui-refresh'
dotnet test TaskTodo.sln -c Release --no-build --no-restore --logger 'trx;LogFilePrefix=UI-final' --results-directory 'artifacts/test-results'
```

均退出码 0。构建 **0 警告、0 错误**；Core **12/12**、Infrastructure **55/55**、App **41 通过、0 失败、1 跳过**，合计 **108 通过、0 失败、1 跳过**。TRX：`UI-final_net10.0_20261013124437.trx`、`...124438.trx`、`...124514.trx`，位于 `artifacts/test-results`。

截图复核发现最小窗口展开已完成区时空间不足，随后进一步缩小紧凑任务行间距并调整两组的高度分配；增加“复选框有完整可操作高度、已完成列表不覆盖新增框”的断言。最后一次构建仍 0 警告/错误，执行：

```powershell
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~WpfSmokeTests' --logger 'trx;LogFilePrefix=UI-compact' --results-directory 'artifacts/test-results'
```

退出码 0，**1/1 通过**，证据 `artifacts/test-results/UI-compact_net10.0_20261013124700.trx`。本轮未添加仅重复样式实现的测试类；在原有真窗口测试内扩展边界截图、清单选择/展开契约和最小窗口布局检查。

`artifacts/ui-refresh` 共生成 **18 张 PNG**，包含主窗浅深色、设置浅深色/底部、空态/最小空态、清单下拉选项、最小窗口/长任务/展开完成区、1000 任务、胶囊、预览及三个对话框。实际查看后，未发现按钮或路径遮挡；最小尺寸的多行任务在列表内滚动，未越过新增区域。图片来自真实 WPF 渲染，使用临时数据库。

同轮资源观测在 `artifacts/ui-refresh/performance-observation.json`：1000 任务启动至窗口约 **1.24 秒**，10 轮上下滚动 **0.34 秒**，30 次胶囊循环 **8.86 秒**，HWND **21 → 21**。连续三个约 2 秒的单核 CPU 样本为 **93.57%、37.19%、0%**；私有内存约 **123.35 MiB**。前两段仍有切换尾部工作，工作集受 Windows 裁剪影响；不是受控性能对比，不据此宣称新版更快。

### 发布与复验

```powershell
& .\scripts\publish.ps1
$env:TASKTODO_APP_PATH = '<项目目录>\artifacts\publish\win-x64\TaskTodo.exe'
$env:TASKTODO_TEST_ARTIFACTS = '<项目目录>\artifacts\ui-published'
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProcessTests' --logger 'trx;LogFilePrefix=UI-published' --results-directory 'artifacts/test-results'
```

发布脚本已成功更新 `artifacts/publish/win-x64` 的整个自包含目录。发布程序复验退出码 0，**6 通过、0 失败、1 鼠标独占项跳过**，约 14 秒；TRX 为 `artifacts/test-results/UI-published_net10.0_20261013124808.trx`。正式数据未用于测试。README、STATUS、TASKS、ACCEPTANCE 同步到发布目录。

### 桌面查看与下一步

运行发布目录中的 `TaskTodo.exe`。分别查看浅色/深色、设置下拉和滑块、任务悬停菜单/排序、胶囊当前清单标记；缩至 280×260，展开已完成区并滚动长任务，确认新增框始终可见。数据管理可从齿轮设置底部打开。

下一步仅根据真实使用反馈微调视觉和完成此前待验的硬件/输入法项目。实体鼠标独占测试仍默认跳过；真实 IME、混合 DPI/热拔插、系统注销和干净 Windows 验收未在本轮外推为通过。

## 第二轮视觉打磨（2026-10-13 下午）

### 变更

- 任务勾选框由圆角方块改为圆形，悬停显示柔和强调底色，选中后保持实心。
- 清单标题下的副标题行右侧新增 4 px 完成进度条和百分比（`MainWindowViewModel.CompletionRatio`，随任务变化通知；无任务时隐藏）。
- 窗口按钮中最小化/关闭图标由 12 调为 10，与置顶、设置图标粗细一致；新增按钮去除 1 px 描边；标题区高度 98 → 104（紧凑模式不变）。
- 未改动数据层、窗口状态机、自动化 ID 与交互契约。

### 验证

- `dotnet build src/TaskTodo.App/TaskTodo.App.csproj -c Release`：0 警告、0 错误。
- 使用 `--data-dir` 隔离数据启动真实 exe，PrintWindow 抓取浅色/深色主窗，复核圆形勾选、进度条、新增按钮与间距。
- `scripts/publish.ps1` 已更新 `artifacts/publish/win-x64`。
- `dotnet test TaskTodo.sln -c Release`：Core/Infrastructure 通过；App 测试中 `WpfSmokeTests.Real_windows_*` 首次因前台窗口断言失败，单独重跑 3 次通过（该断言依赖当前前台窗口，属环境抖动）。
- **未通过项**：`AppProcessTests.Actual_entry_point_adds_docks_reopens_and_keeps_committed_data_after_process_exit` 在“点击胶囊打开后等待主窗口 TaskTodo 出现”处超时。以同一环境对**改动前**的发布 exe 复跑同样失败，因此不是本轮修改引入；今日上午该项曾通过，原因尚未查明，保留为待排查，不据此宣称通过。发布程序进程测试本轮结果：5 通过、1 失败、1 鼠标独占项跳过。

## 毛玻璃半透明面板（2026-10-13 傍晚）

### 变更

- `SettingsRules` 背景不透明度下限由 0.35 放宽到 0.15（滑块同步），默认值 0.88 不变；新增 `SettingsOpacityRuleTests`（边界 0.15/0.14/1.0/1.01/NaN）。
- 新增 `Desktop/GlassBackdrop.cs`：不透明度低于 98% 时调用 `SetWindowCompositionAttribute`（BLURBEHIND）对窗口后方做模糊，并用 `SetWindowRgn` 把窗口裁成面板圆角；恢复到 98% 以上时关闭模糊并清除区域。调用失败时静默退化为普通半透明，不影响任务数据。
- 模糊开启期间窗口被裁到面板边缘，外圈 8 DIP 的阴影随之消失，缩放热区相应内移（`ResizeBorderThickness` 7 → 15）。
- 主题新增玻璃边缘渐变、顶部高光、任务行玻璃底色和与主题表面同色的文字柔光（`TextHaloEffect`），用于浅色文字压在亮壁纸上、深色文字压在暗壁纸上时保持可读；预览窗口边缘同步使用玻璃边缘。

### 验证与限制

- `dotnet test TaskTodo.sln -c Release`：Core 19 通过（含新增 7 项），Infrastructure 55 通过，App 40 通过、1 跳过、**1 失败**；失败项仍为上一节记录的 `AppProcessTests…keeps_committed_data_after_process_exit`（改动前发布包同样失败），未修复。
- 真实进程截图（隔离数据、45% 不透明度、浅/深色）：背景被模糊，浅色主题在深色背后时次要文字对比度偏低，属于玻璃透明度的固有取舍，可调高不透明度。
- 未在多种壁纸、多显示器/混合 DPI 下验证；DWM 模糊在拖动/缩放时的跟手程度待手工确认。
- 低不透明度下的预览窗口与胶囊仍是普通半透明，没有模糊。

## 悬浮图标球（2026-10-13 晚）

### 变更

- `SettingsSnapshot` 新增 `CollapseStyle`（`Capsule` / `Orb`，init 属性，旧数据缺省为 `Capsule`；`SettingsRules` 校验枚举）。选择写入 SQLite 设置 JSON，不进入 JSON 导出包。
- `DockRailWindow` 增加图标球视觉：44 DIP 强调色圆盘 + 勾形图标 + 未完成数徽标（0 时隐藏）；窗口 56×56 DIP，沿用左右边缘吸附、`DockOffset`、悬停预览和右键菜单。单击打开当前清单；超过系统拖动阈值才视为拖动，拖动结束不触发点击。
- `MonitorService.PlaceDock` 增加 `CollapseStyle` 参数（默认胶囊，旧调用不变）；`WindowManager` 在样式变化时重新定位并关闭预览。
- 设置“停靠方向”卡片新增“收起样式”下拉；收起后的右键菜单有“切换为图标球 / 切换为胶囊条”。
- 枚举原命名 `DockStyle` 与 `System.Windows.Forms.DockStyle` 冲突，故定为 `CollapseStyle`。

### 验证

- `dotnet test TaskTodo.sln -c Release`：Core 20 通过，Infrastructure 56 通过，App 40 通过、1 跳过、1 失败（与前两节相同的 `AppProcessTests…keeps_committed_data_after_process_exit`，改动前发布包同样失败，未处理）。
- 真实进程（隔离数据）：图标球渲染截图复核；UIA `OpenOrb` Invoke 后主窗口出现；真实鼠标按下并向下拖动约 300 px，图标球沿右边缘下移、仍为收起状态，`dockSide=Right`、`dockOffset` 由 0.2 变为约 0.41 并保存。
- 上述 `AppProcessTests` 失败项对应的“胶囊点击打开主窗”流程，用 UIA 在旧发布包上手工复现一次成功，说明失败更可能出在测试探针与当前桌面环境，而非功能本身；根因仍待查。
- 未验证：多显示器/混合 DPI 吸附、图标球悬停预览的真实鼠标焦点行为（EDGE-05 仍需独占鼠标测试）。

## 胶囊点击丢失修复（2026-10-15）

### 根因

前几节记录的 `AppProcessTests…keeps_committed_data_after_process_exit` 间歇失败不是环境问题。修复前单独复跑 7 次失败 3 次，失败均在“点击胶囊打开后等待主窗口出现”（`AppProcessTests.cs` 原第 43 行）。

`WindowManager.ChangePresentation(Dock)` 先显示胶囊，再 `await SaveSettingsAsync()`，此期间 `_transitioning` 仍为 true；`OpenListAsync` 在过渡中直接 `return`，点击被静默丢弃。UIA 点击足够快，会落入这段时间；真实用户在收起后立即点击、或保存较慢时同样会“点了没反应”。

### 变更

- `WindowManager.OpenListAsync`：过渡中收到的胶囊/图标球/预览“打开”请求记入 `_queuedOpenListId`（只保留最后一次），不再丢弃。
- 新增 `EndTransition()`，统一替换所有 `_transitioning = false`：结束过渡后，若有排队请求且胶囊或预览仍可见，经 Dispatcher 重放该打开请求；窗口已隐藏到托盘、已展开或已释放时丢弃，避免过期点击重新弹出主窗。
- `AppProcessTests`：点击胶囊前等待数据库中的 `lastPresentationState` 变为 `Docked`，明确测试前置条件。
- 托盘“显示/找回”（`ShowOrRecover`）在过渡中仍直接返回，本次未改。

### 验证

- `dotnet build TaskTodo.sln -c Release`：0 警告、0 错误。
- 先只改产品代码、保留原测试，单独运行该测试 10 次：**9 通过**，原第 43 行失败未再出现；1 次失败在重启阶段的任务行查找（第 59 行，5 秒内未在新进程 UIA 树中找到任务）。历史 `M6-published_net10.0_20261013121129.trx` 也有重启阶段（第 58 行）失败，属于修复前已有的 UIA 探针不稳定，与胶囊点击无关，未处理。
- 加入 Docked 等待后，单独运行 **15 次全部通过**（每次 3–4 秒）。
- 全量 `dotnet test TaskTodo.sln -c Release --no-build`：Core **20/20**、Infrastructure **56/56**、App **41 通过、0 失败、1 跳过**（独占鼠标项），合计 **117 通过、0 失败、1 跳过**。TRX：`artifacts/test-results/Fix-capsule-click_net10.0_20261015165602.trx`、`…165604.trx`、`…165641.trx`。
- 真实鼠标“收起后立即点击胶囊”未手工验证。

### 发布与发布程序复验

```powershell
& .\scripts\publish.ps1
$env:TASKTODO_APP_PATH = '<项目目录>\artifacts\publish\win-x64\TaskTodo.exe'
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProcessTests' --logger 'trx;LogFilePrefix=Fix-published' --results-directory 'artifacts/test-results'
```

- 发布脚本退出码 0，`artifacts/publish/win-x64` 已整体更新（`TaskTodo.exe`/`TaskTodo.dll` 为 2026-10-15 16:58），README 与 STATUS/TASKS/ACCEPTANCE 已随包同步。脚本末行打印的路径是 `<当前目录>\artifacts\publish\win-x64`，这是错误的：`[IO.Path]::GetFullPath` 按进程当前目录解析，而不是按 `Push-Location` 后的位置。实际输出仍在项目内，dotnet 输出行和文件时间均已确认，项目外也没有生成目录。脚本未改。
- 发布程序进程测试（TRX：`artifacts/test-results/Fix-published_net10.0_20261015165922.trx`）：**6 通过、0 失败、1 跳过**（独占鼠标项），其中包括本次修复的 `AppProcessTests…keeps_committed_data_after_process_exit`。
- 不写 TRX 再连跑 3 次：2 次 6/0/1；1 次 `StartupRecoveryProcessTests.User_can_preview_and_confirm_a_backup_to_recover_a_corrupt_database` 失败。单独重跑该项 8 次，失败 1 次。失败点在第 33 行：系统文件对话框中按 AutomationId `1` 找到的元素调用 `InvokePattern`，抛出 `InvalidOperationException: 不支持的模式`，说明当时找到的元素尚不支持 Invoke。启动恢复流程在 `WindowManager` 创建前运行，与本次修复无关；属于测试探针对系统对话框的时序依赖，未处理。
- 另外仍有的间歇项：`AppProcessTests` 重启阶段的 UIA 查找（见上节）。两项都只在重复运行中偶发，不应据单次通过宣称进程测试已稳定。


## 界面重新设计：配色与玻璃材质（2026-10-15）

### 变更

- 设置新增“配色”（靛紫 / 珊瑚 / 玫瑰 / 琥珀）和“材质”（实色 / 玻璃）。`SettingsSnapshot` 增加 `Palette`、`Material` 两个 init 属性，旧数据缺省为靛紫 + 实色；`SettingsRules` 校验枚举。两者与收起样式一样，只存本机设置，不进入 JSON 导出包。
- 背景不透明度只在玻璃材质下生效：`MainWindowViewModel.SurfaceOpacity` / `PreviewViewModel.SurfaceOpacity` 在实色时恒为 1.0。切换到玻璃时若原值高于 95%，改为默认 75%；玻璃滑块范围 15%—95%。以前只要低于 98% 就自动模糊，现在只有玻璃材质会模糊；旧数据（默认 88%）升级后变为实色不透明面板。
- `ThemeService` 按“配色 × 浅/深 × 材质”生成整套画刷。每套配色包含带色调的中性色阶和一个强调色，另加 `AccentTextBrush`（可读的强调文字色）、`TrackBrush`、`ErrorTextBrush`。玻璃材质下边框、悬停和输入底改为白色半透明，次要文字向主文字色靠拢以保持对比度。
- 去掉所有文字上的 `TextHaloEffect` 光晕（之前文字发虚的主要原因）。主窗阴影移到独立的背景层，正文不再经过效果渲染。
- 主窗重排：清单名和下拉箭头合为标题（`TitleComboBoxStyle`），右侧是窗口按钮；副标题显示待完成数与百分比，下方是全宽 3 px 进度条；去掉品牌行。任务行改为扁平行（无卡片边框，悬停才有底色），勾选圈改为 18 px、使用三级文字色描边，悬停时预显对勾。新增框改为整行浅底输入，空白时加号按钮为透明底。
- 设置页改为分组列表（清单 / 外观 / 收起 + 数据入口）。“完成”移到设置标题右侧，去掉底部大按钮；配色用色块选择（`SwatchListBoxStyle`），材质用分段控件（`SegmentedListBoxStyle`）。
- 预览窗口、胶囊和图标球改用同一套画刷；预览在玻璃材质下同样做桌面模糊（胶囊未加模糊）。圆角统一为 14（窗口）/ 8（行与控件）。偏绿的阴影色改为中性黑。
- 自动化 ID 与交互契约不变；`MainWindow.UpdateLayoutDensity` 改用新的元素名。

### 验证

- `dotnet build TaskTodo.sln -c Release`：0 警告、0 错误。
- 全量 `dotnet test TaskTodo.sln -c Release --no-build`：Core **20/20**、Infrastructure **57/57**（新增配色/材质默认值与持久化）、App **42 通过、0 失败、1 跳过**（新增玻璃不透明度 ViewModel 测试；跳过的是独占鼠标项），合计 **119 通过、0 失败、1 跳过**。TRX：`artifacts/test-results/UI-redesign_net10.0_20261015173031.trx`、`…173033.trx`、`…173115.trx`。
- `WpfSmokeTests` 新增 `RenderThemeGallery`：遍历 4 配色 × 实色/玻璃 × 浅/深共 16 种组合，加上一张玻璃设置页，均无绑定错误。设置 `TASKTODO_TEST_ARTIFACTS` 后，这些组合会叠在模拟壁纸上保存为截图，但 RenderTargetBitmap 无法呈现 DWM 模糊。
- 截图位于 `artifacts/ui-redesign-1015`（共 39 张）：原有 18 张界面截图、17 张 gallery 截图，另有 4 张真实进程截屏 `real-*.png`。真实截屏使用隔离数据启动构建产物，从屏幕抓取，能看到真实的 DWM 模糊；组合为珊瑚玻璃浅色、靛紫玻璃深色、玫瑰实色浅色、琥珀实色深色。改动前的截图在 `artifacts/ui-before-1015`。
- 已知取舍：浅色玻璃叠在深色窗口或壁纸上时会偏灰，这是半透明叠加的固有表现，可调高不透明度改善。胶囊在玻璃材质下只是半透明，没有模糊。
- 未验证：不同壁纸、拖动/缩放时玻璃的跟手程度，多显示器/混合 DPI 下的观感。发布目录 `artifacts/publish/win-x64` 仍是改版前的包，未重新发布。

### 细节调整（同日）

- 程序图标：新增 `src/TaskTodo.App/Assets/TaskTodo.ico`（靛紫圆角方块 + 白色对勾，16—256 px 共 9 个尺寸，每个尺寸单独渲染，与图标球一致）。csproj 设置 `ApplicationIcon` 并作为 WPF 资源嵌入；主窗 `Icon` 使用 `/TaskTodo;component/Assets/TaskTodo.ico`（带程序集名，测试进程中也能解析）。托盘按当前 DPI 的小图标尺寸加载同一资源，加载失败时回退系统图标。已从构建出的 exe 提取图标确认已嵌入。图标由临时 Python/Pillow 脚本生成，脚本未纳入项目。
- 置顶按钮：开启时不再显示强调色底块（默认开启时标题栏显得重），改为实心图钉图标（`&#xE841;`）+ 强调文字色，图标切换放在 `PinButtonStyle` 中。
- 最小化（收起）图标字号 11 → 10，与关闭图标粗细一致。新增框为空时加号按钮为透明底 + 三级文字色，有内容时恢复强调色填充。清单为空时隐藏副标题“0 项待完成”，避免与中间的空状态提示重复。
- 验证：Release 构建 0 警告、0 错误；全量 **119 通过、0 失败、1 跳过**（TRX：`artifacts/test-results/UI-details_net10.0_20261015173921.trx`、`…173922.trx`、`…174000.trx`）；`artifacts/ui-redesign-1015` 中的截图和 `real-*.png` 已按新版本重新生成。
- 注意：生成 `real-*.png` 的脚本会启动真实窗口并激活它，运行期间会抢走键盘焦点（有一次桌面上的键入落进了测试窗口的新增框，只影响临时数据）。之后不再在有人使用的桌面上运行这种截屏。
- 托盘图标在任务栏通知区的实际显示、发布目录均未验证/更新。

### 改版后发布与复验（同日）

```powershell
& .\scripts\publish.ps1
$env:TASKTODO_APP_PATH = '<项目目录>\artifacts\publish\win-x64\TaskTodo.exe'
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProcessTests' --logger 'trx;LogFilePrefix=UI-redesign-published' --results-directory 'artifacts/test-results'
```

- 发布脚本退出码 0，`artifacts/publish/win-x64` 已整体更新为改版后的版本（`TaskTodo.exe`/`TaskTodo.dll` 为 2026-10-15 17:41）。从发布出的 `TaskTodo.exe` 提取到的图标为新的靛紫图标。README 与 STATUS/TASKS/ACCEPTANCE 已随包同步。脚本末行仍会打印错误的 `<当前目录>\...` 路径（见前文），实际输出位置正确。
- 发布程序进程测试（TRX：`artifacts/test-results/UI-redesign-published_net10.0_20261015174206.trx`）：**6 通过、0 失败、1 跳过**（独占鼠标项）。不写 TRX 再连跑 2 次，均为 6/0/1；此前记录的启动恢复项偶发失败（约 1/8）这 3 次未出现，但不能据此认为已经消除。
- 用户本机的旧设置升级后会变为“靛紫 + 实色”（新字段缺省值），之前的背景不透明度保留，切到玻璃后生效。
- 未验证：托盘通知区图标的实际显示、真实中文输入法、多显示器/混合 DPI、干净 Windows。


## 用户反馈修复：玻璃黑框、透明材质、悬浮球（2026-10-15 晚）

用户在实际使用中反馈 5 个问题。用户本机为 3 块屏幕（150% / 100% / 125%），Windows 11 build 26200。

### 根因与修改

- **玻璃外圈黑框**：用临时诊断测试复现（窗口不激活，直接从屏幕抓取合成结果）。旧 `GlassBackdrop` 的 accent 带 gradient 标志（`AccentFlags = 2`、颜色全透明），DWM 会把面板外 8 DIP 阴影留白的透明像素画成黑色。`SetWindowRgn` 对 WPF 分层窗口不起裁剪作用，所以旧注释里“窗口被裁成圆角”的说法其实并不成立。现在去掉 gradient 标志、不再设置窗口区域；玻璃和透明模式下面板铺满窗口（无留白、无阴影层），实色模式保留 8 DIP 留白和阴影。诊断截图确认无黑框，圆角外露出的是清晰的背景。也试过把窗口改为非分层，用 DWM 延伸边框或系统亚克力：DWM 会在面板后铺一层不透明底，系统亚克力在窗口未激活时也是实色，因此放弃。
- **“不够透明，看不到后面”**：新增第三种材质“透明”（`SurfaceMaterial.Clear`），不加模糊，按不透明度直接透出身后内容。默认不透明度改为玻璃 60%、透明 45%；每次切换到玻璃或透明都会用该材质的默认值（之前从实色切过来会沿用 88%）。设置项改名为“背景不透明度”，材质改为三段选择。
- **输入法组字时提示字不消失**：输入法组字期间文字不进入 `Text`，提示按 `NewTitle` 是否为空判断，所以一直显示。`TaskTextBox.IsComposing` 改为只读依赖属性，组字开始时隐藏提示。
- **图标球拖不动**：收起后的窗口为不抢焦点设置了 `WS_EX_NOACTIVATE`。后台窗口只有在光标位于窗口上时才能保持鼠标捕获，快速拖动时光标会滑出 56 DIP 的图标球，拖动随即中断。新增 `PointerDrag`：按住后每帧读取光标位置和按键状态，直到松开为止，不再依赖捕获；图标球和胶囊顶部手柄都改用它。另外，拖动期间若收到显示器或 DPI 变化，不再执行 `PlaceDock` 把窗口拉回原位。
- **图标球被全屏应用盖住**：本身也置顶的全屏应用激活后会压在悬浮窗上，而不激活的窗口不会自己回到上层。`WindowManager` 改为每次前台窗口切换（`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`）以及收起期间每 2 秒，把胶囊/图标球和预览重新放到置顶层最上面（不激活）。独占全屏（DirectX exclusive）的游戏仍然无法覆盖。
- **X 改为收起**：标题栏去掉单独的“–”按钮，`×`（AutomationId 仍为 `DockToEdge`）、Alt+F4、Ctrl+W 都收起到屏幕边缘（按设置显示胶囊或图标球）。只有托盘“隐藏”和悬浮窗右键“隐藏到托盘”会连悬浮窗一起隐藏，这部分原本就是如此。

### 验证

- `dotnet build TaskTodo.sln -c Release`：0 警告、0 错误。全量 `dotnet test`：Core **20/20**、Infrastructure **57/57**、App **42 通过、0 失败、1 跳过**，合计 **119 通过、0 失败、1 跳过**。TRX：`artifacts/test-results/Glass-orb-fix_net10.0_20261015182038.trx`、`…182039.trx`、`…182118.trx`。
- 新增或调整的断言：`WpfSmokeTests` 检查组字开始时提示隐藏、结束后恢复；`AppProcessTests` 检查关闭主窗后出现胶囊窗口；`TaskPanelTests` 检查透明/玻璃各自的默认不透明度。烟雾测试的配色组合随枚举扩展为 4 配色 × 3 材质 × 浅/深，均无绑定错误。
- 诊断截图（临时测试，已删除）：同一彩色背景上的实色 / 玻璃 / 透明 × 浅/深，确认玻璃有模糊且无黑框、透明能看清背景文字。截图未纳入 artifacts。
- **未验证**：真实鼠标快速拖动图标球（需要移动用户的鼠标，未执行）、全屏应用之上的显示、150%/125% 屏幕上的玻璃观感（诊断窗口落在 100% 和 150% 屏各一次，150% 那次定位有误，未得到有效截图）。发布目录未更新。

### 修复后发布与复验（同日）

```powershell
& .\scripts\publish.ps1
$env:TASKTODO_APP_PATH = '<项目目录>\artifacts\publish\win-x64\TaskTodo.exe'
dotnet test tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProcessTests' --logger 'trx;LogFilePrefix=Glass-orb-published' --results-directory 'artifacts/test-results'
```

- 发布前检查到用户自己的 TaskTodo（发布目录 exe，真实数据）正在运行；没有强制结束它，而是等用户从托盘“退出”后再确认无进程才发布。
- 发布脚本退出码 0，`artifacts/publish/win-x64` 已整体更新（`TaskTodo.exe`/`TaskTodo.dll` 为 2026-10-15 18:32），新 DLL 中包含 `PointerDrag`。README 与 STATUS/TASKS/ACCEPTANCE 已随包同步。脚本末行打印的路径仍是错误的 `<当前目录>\...`，实际输出位置正确。
- 发布程序进程测试（TRX：`artifacts/test-results/Glass-orb-published_net10.0_20261015183252.trx`）：**6 通过、0 失败、1 跳过**（独占鼠标项），其中包括“关闭主窗后出现胶囊”的新断言。不写 TRX 再连跑 2 次，均为 6/0/1。
- 仍待用户实测：真实鼠标快速拖动图标球、全屏应用之上的显示、150%/125% 屏幕上的玻璃与透明观感。

## 桌面快捷方式与旧产物清理（2026-10-15 晚）

- 新增桌面快捷方式 `<桌面>\TaskTodo.lnk`，指向 `artifacts\publish\win-x64\TaskTodo.exe`（工作目录为发布目录，图标取自 exe）。之后重新发布会覆盖同一目录，快捷方式无需更新。系统非 Unicode 代码页为 cp1252，`WScript.Shell` 无法保存含中文的路径，因此改用 IShellLinkW 创建，并读回确认目标正确。
- 按用户选择“清理旧产物”，以下内容移入回收站（共 60 项、约 402 MB，可从回收站恢复）：`artifacts/m3-ui`、`m6-lifecycle`、`m6-ui`、`ui-refresh`、`ui-before-1015`、`verify`；`artifacts/test-results` 中除 `Glass-orb-fix_*`（最新全量测试 3 个）和 `Glass-orb-published_*`（最新发布复验 1 个）以外的结果文件；`src/*`、`tests/*` 下全部 `bin`/`obj`。
- 保留：源码、测试、脚本、文档、`artifacts/publish/win-x64`、最新截图 `artifacts/ui-redesign-1015`。
- 前文各节引用的旧截图目录和旧 TRX 文件名作为历史记录保留在文档里，文件本身已不在；如需查看，可从回收站恢复。编译缓存已删除，下次 `dotnet build`/`dotnet test` 会重新生成。
