# TaskTodo 实施规划

版本：1.0  
日期：2026-09-20  
目标环境：Windows 11 x64  
文档性质：交给 Codex 的开发规格与任务规划，不是已实现或已验证的软件。

## 1. 产品目标与范围

TaskTodo 是一个本地优先的桌面任务小组件。用户可以把任务列表固定在桌面某个位置；直接新增、完成、取消完成、编辑、删除任务；收起后显示在屏幕边缘；悬停时展开可交互的预览。

参考 PaperTodo 的桌面交互，不以“纸片”或 Markdown 笔记为产品中心，也不是论文专用工具。所有用户界面和业务模型统一使用“任务”“清单”等通用名称。工作、学习、生活等名称仅作为用户自定义清单的例子，不写死业务场景。

### 必须完成的 V1

- 无边框、可拖动、可调背景不透明度的原生任务面板。
- 新增、编辑、完成/取消完成、删除与撤销；任务自动持久化。
- 左/右屏幕边缘停靠；胶囊入口；悬停预览；点击打开完整面板。
- 支持多个清单，但同时最多一个完整面板、一个共享预览窗口。
- 位置记忆、置顶开关、托盘隐藏/找回、单实例。
- 数据备份与恢复、基本故障提示、中文输入和多显示器位置回收。

### 不纳入 V1

云同步、用户账号、Web 服务端、团队协作、AI 自动拆任务、Markdown 编辑器、论文管理、插件系统、脚本执行、番茄钟、提醒调度、重复任务、复杂子任务、每个清单独立大窗口、壁纸层嵌入、整窗鼠标穿透、毛玻璃背景模糊、自动更新。

优先级、截止日期、开机自启、可配置全局快捷键、更多屏幕边缘方向列为 V1.1 候选，不提前搭建相关框架。

最小可用版本在 M3 完成；具有主要参考交互的版本在 M4 完成；正式 V1 还需 M5、M6。

## 2. 技术路线与复用边界

默认新建独立 TaskTodo 工程，而不是把 PaperTodo 全仓库改名。此前的 Python 原型不作为本工程的基础，也不需要迁移其代码。

选定技术：C#、.NET 10、WPF、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite。

“前端”指 WPF/XAML 界面及 ViewModel；“后端”指本地 C# 业务服务与 SQLite 数据访问。两者在同一个桌面进程中运行，通过 C# 接口调用，不通过 HTTP 通信，不需要 FastAPI、Flask、Express、ASP.NET、React 或远程数据库。

PaperTodo 的项目文件使用 .NET 10/WPF；WPF 官方文档说明它通过 XAML 与托管代码构建界面，且仅在 Windows 上运行。[R1][R2] MVVM Toolkit 可用于 WPF 的绑定通知和命令实现。[R4] Microsoft.Data.Sqlite 是 SQLite 的轻量 .NET 数据访问提供程序。[R5]

实施时检测 `dotnet --info`，选择可用的稳定 .NET 10 SDK 并锁定实际版本；NuGet 包同样固定实际使用的稳定版本，不写通配版本，不照抄参考项目的全部依赖。

PaperTodo 当前许可证包含非商业及附加许可限制，并不是 MIT 许可。[R3] 本项目只参考可观察行为和公开文档，自行编写代码与视觉资源。不得复制品牌、图标、字体文件或整段实现。若之后改为直接派生开发，须重新核对届时许可证与使用范围、保留要求的声明，不把改名当作新的授权。

## 3. 界面与交互规格

### 3.1 三种主要形态

完整面板：默认 340 × 440 DIP，最小 280 × 260 DIP。标题区包含清单切换、未完成数、置顶开关、设置和收起按钮；中间是任务列表；底部是新增输入框。顶部空白区域允许拖动；按钮、输入框、复选框和列表正文不能误触发窗口拖动。

贴边胶囊：默认约 120 × 32 DIP，显示清单名和未完成数。多个清单沿一侧排列；清单过多时允许胶囊列表滚动，不得延伸到工作区外。默认停靠右侧，可拖到左侧；第一版不做顶部/底部停靠。

悬停预览：默认宽 320 DIP，高度按内容增长但不超过 420 DIP，同时不得超出当前显示器工作区。显示当前清单任务，允许滚动、完成/取消完成；编辑或新增通过明确的“打开”入口转入完整面板。V1 不在预览中输入长文本，不在预览中实现拖动排序。

以上尺寸和后文延时是 TaskTodo 的初始设计参数，不是从 PaperTodo 源码提取的数值。原项目的胶囊、悬停交互依据其 README；具体动效需在 Windows 实现后对照参考 GIF 调整。[R1]

### 3.2 视觉与透明

采用克制的圆角浮窗：浅色/深色主题、轻边框、小面积阴影、清晰文字。默认使用系统字体并保留中文回退，不附带第三方字体。不要求纸张纹理、折角、拟物纸片、科技风渐变或大型导航栏。

背景不透明度默认 88%，可调 35%—100%；文字、复选框和按钮保持清晰不透明。只改变背景 Brush 的 alpha，禁止通过 Window.Opacity 或整个根容器 Opacity 实现用户的背景透明度设置。动画可短暂改变专用动画容器的透明度，但不能改变保存的文字可读性设置。

透明壳体采用 `WindowStyle=None`、`AllowsTransparency=True` 和透明窗口背景。WPF 官方要求允许这种透明时 WindowStyle 为 None。[R6] 本阶段的半透明不等于系统毛玻璃，不承诺背景模糊。

不要创建覆盖全屏的透明顶层窗口。胶囊和预览 HWND 的范围应尽量小；预览关闭后必须隐藏，不保留不可见点击遮挡。鼠标穿透不属于本版本功能。

### 3.3 任务操作

任务左侧复选框改变完成状态；双击标题或使用右侧菜单编辑；右侧菜单含删除。空标题或只有空白的标题不得保存。标题去首尾空白，最长 500 字符，按纯文本展示；长标题换行，不撑破窗口。同名任务允许存在，使用独立 ID 区分。

新增输入框按 Enter 添加，但中文输入法仍在组词/确认候选时，不得触发提交。粘贴换行按单个标题的空白处理；智能多行拆分暂不做。

编辑期间 Enter 提交、Esc 取消，输入法组合态优先处理；失去焦点时提交有效编辑，空内容则保持原标题并提示。正在编辑时，不得因为失焦或鼠标离开直接折叠窗口。退出应用前先结束有效编辑，再等待保存队列完成；保存失败时给出重试或保留窗口的选择。

完成后显示删除线与弱化文字，归入可折叠的“已完成”区域；取消完成后回到未完成区。使用显式 `SetCompleted(id, value)`，而不是在多处各自调用 Toggle。

拖动排序仅作用于当前清单的未完成任务，使用专门拖动手柄；不能把拖动窗口、编辑文本和任务排序混为一件事。完成任务保留原始顺序值，取消完成后按其顺序值回到未完成区。

删除使用软删除，不立即物理移除记录；提供约 8 秒的撤销入口。M5 提供“最近删除”恢复入口；首版不自动永久清空软删除记录。撤销对象必须按任务 ID 记录，不用文本定位。文本框内 Ctrl+Z 优先保留为文本撤销，不全局抢走。

## 4. 窗口状态与屏幕管理

### 4.1 状态机

用独立可测试的状态机描述行为，不使用分散在多个窗口中的互相覆盖的布尔值。

| 状态 | 可见窗口 | 主要行为 |
| --- | --- | --- |
| Expanded | 完整任务面板；胶囊和预览隐藏 | 编辑、新增、排序；不会因鼠标离开自动收起 |
| Docked | 胶囊栏 | 等待悬停或点击 |
| Previewing | 胶囊栏和一个共享预览 | 悬停浏览、勾选；离开有效区域后延迟收回 |
| HiddenToTray | 仅托盘入口 | 主动隐藏后不因鼠标经过边缘自动恢复 |

首次启动进入 Expanded；用户按收起按钮或 Ctrl+W 后进入 Docked。点击胶囊/预览“打开”按钮进入 Expanded。完整面板关闭按钮和 Alt+F4 默认隐藏到托盘，明确的“收起”与“隐藏”不能使用同一个含糊按钮。托盘“退出”才终止进程。

托盘“显示/找回”打开完整面板并回收位置；托盘“收起到边缘”进入 Docked；托盘“隐藏”进入 HiddenToTray。正常退出后，下次启动恢复上次 Expanded 或 Docked；不恢复临时 Previewing，也不恢复为完全不可见的 HiddenToTray。第一次启动始终可发现。

### 4.2 悬停规则

建议悬停约 150 ms 打开预览，离开约 350 ms 收回，展开/收起动画约 160—200 ms；系统关闭动画时尊重其偏好。参数集中配置，不能散落魔法数字。

有效悬停区是胶囊栏、预览和二者之间的过渡区域的并集。鼠标从胶囊移向预览时不能先关闭。离开使用可取消定时器；切换胶囊或重新进入时取消旧计时任务，避免旧回调关闭新预览。

打开菜单、进行鼠标捕获、拖动、显式编辑、打开设置或执行交互时，暂停自动收起。相关交互结束后重新计算，而不是强行执行过期的收起操作。

纯悬停打开预览不得抢走当前应用键盘焦点；显式点击打开完整面板才可激活并安排输入焦点。预览中的复选框只能触发一次业务操作；快速移动鼠标不能重复提交勾选。

所有列表窗口共用一个业务状态源；预览切换只改变当前 ListId 和视图，不重复加载整个应用、不为每个胶囊创建独立数据库服务。关闭窗口时解除订阅，避免重复刷新。

### 4.3 置顶、坐标和位置恢复

置顶是设置项，默认开启，对胶囊、预览和完整面板一致应用。关闭置顶后允许被其他应用遮挡；不宣称此时就嵌入了桌面壁纸层。不要强行把窗口挂在 Explorer/WorkerW 下。

窗口位置必须根据目标显示器的工作区计算，避开任务栏。原生屏幕像素坐标与 WPF DIP 分开管理：所有 Win32/显示器坐标转换集中在 MonitorService；使用目标显示器缩放，不以主屏缩放转换所有屏幕。

建议保存显示器标识、相对该显示器工作区的局部 DIP 矩形、胶囊停靠方向、归一化沿边偏移。保存前验证有限数值和尺寸；恢复时检查显示器仍存在，否则回收至可用主屏工作区。不要假设屏幕原点均非负或任务栏一定在下方。

拖动、缩放中合并位置保存，约 500 ms 防抖；结束拖动/缩放和正常退出时补写最终值。任务变更不能套用这个延迟策略。

托盘“找回窗口”是强制兜底入口。探测显示器移除、缩放变化和工作区变化，必要时重新定位。不支持的混合 DPI 情况应如实记录，不伪装通过。

## 5. 前端、本地后端与工程边界

### 5.1 结构

```text
TaskTodo/
├── AGENTS.md
├── TaskTodo.sln
├── global.json
├── README.md
├── docs/
│   ├── IMPLEMENTATION_PLAN.md
│   ├── TASKS.md
│   ├── ACCEPTANCE.md
│   └── STATUS.md
├── src/
│   ├── TaskTodo.App/
│   │   ├── Views/          # MainWindow、DockRailWindow、PreviewWindow、SettingsWindow
│   │   ├── Controls/       # TaskRow、QuickAdd、TaskListPicker
│   │   ├── ViewModels/     # 绑定、命令、视图状态
│   │   ├── Styles/         # 主题和视觉参数
│   │   └── Desktop/        # WindowManager、MonitorService、TrayService、单实例
│   ├── TaskTodo.Core/
│   │   ├── Models/         # TodoTask、TaskList、SettingsSnapshot
│   │   ├── Services/       # TaskService、TaskListService、接口
│   │   └── Interaction/    # 纯状态机和坐标策略
│   └── TaskTodo.Infrastructure/
│       ├── Storage/        # SQLite、参数化 SQL、仓储、工作队列
│       ├── Migrations/     # 版本迁移
│       └── Backup/         # 备份、JSON 导入导出、恢复
├── tests/
│   ├── TaskTodo.Core.Tests/
│   └── TaskTodo.Infrastructure.Tests/
└── scripts/
    ├── build.ps1
    ├── test.ps1
    └── publish.ps1
```

这是待生成的目录结构，当前规划包不包含上述应用源代码和构建脚本。优先保持少量清晰模块，不额外引入通用微服务框架。

App 调用 Core 服务；Infrastructure 实现 Core 中的数据接口；App 在启动位置组装依赖。Core 不引用 WPF、窗口对象或 Win32。窗口 code-behind 可处理原生输入、生命周期和视图事件，但不得含 SQL 或承担任务业务规则。

### 5.2 主要模块职责

| 模块 | 职责 | 不应承担 |
| --- | --- | --- |
| TaskRow / TaskListViewModel | 展示任务、校验提示、绑定命令 | 直接写数据库 |
| TaskStore | UI 线程内保存已确认业务快照，向多个视图发布变化 | 各窗口独立维护互相覆盖的数据副本 |
| TaskService | 添加、编辑、设置状态、软删除、恢复、排序规则 | 计算窗口坐标 |
| TaskListService | 创建、重命名、列出清单 | 云账号或权限系统 |
| Repository / DbWorker | 参数化 SQL、事务、迁移、数据库 I/O | 调用 WPF 控件 |
| WindowManager | 主窗口、预览、胶囊的显示与生命周期 | 决定任务是否完成 |
| EdgeStateMachine | 输入事件到窗口状态的转换 | 文件访问或直接调用 Dispatcher |
| MonitorService | 工作区、DPI 转换、可见范围回收 | 保存任务标题 |
| TrayService / SingleInstanceService | 显示、隐藏、找回、退出、已有实例唤起 | 对用户数据做隐式重置 |

前端到本地服务的操作约定：CreateTask、UpdateTaskTitle、SetTaskCompleted、SoftDeleteTask、RestoreTask、ReorderActiveTasks、CreateList、RenameList、GetSnapshot、SaveSettings、FlushPendingWrites。实现可采用 Async 方法返回 Task，但名称不代表底层 SQLite I/O 自动成为异步。

调用链为：用户操作 → ViewModel 命令 → TaskService → 数据工作队列/事务 → 提交成功 → TaskStore 更新 → 主面板、预览、未完成数量同步刷新。

V1 优先采用“提交成功才确认界面状态”的简化一致性策略：操作期间给当前项 pending 状态，不重复触发；错误提示可重试，保留输入。不得先显示“已保存”再静默丢弃写盘异常。

### 5.3 SQLite 模型

所有数据放在当前用户的 LocalApplicationData 下：

```text
%LOCALAPPDATA%\TaskTodo\
├── tasktodo.db
├── Backups\
└── Logs\
```

通过系统用户目录 API 获取实际路径，不硬编码盘符，不写到安装目录。

| 表 | 字段与含义 |
| --- | --- |
| TaskLists | Id、Name、SortOrder、CreatedAtUtc、UpdatedAtUtc |
| TodoTasks | Id、ListId、Title、IsCompleted、SortOrder、CreatedAtUtc、UpdatedAtUtc、CompletedAtUtc、DeletedAtUtc |
| Settings | Key、JsonValue；包含窗口、主题、透明度和停靠配置 |

迁移版本使用 PRAGMA user_version，由迁移代码管理。任务类型命名为 TodoTask，避免与异步类型 Task 混淆。

ID 使用 GUID；时间统一用 UTC 并以一致的 ISO 8601 表示持久化；显示时再转换本地时间。IsCompleted 有约束，未完成时 CompletedAtUtc 必须为空。DeletedAtUtc 非空表示软删除，不参与主列表与胶囊计数。

启用外键约束；TodoTasks.ListId 引用有效清单；删除清单功能不在 V1，避免隐式级联丢任务。默认清单为“收件箱”。各任务列表按完成组、SortOrder 和 Id 稳定显示。

排序必须在事务内写入当前清单完整的未完成 ID 序列；拒绝重复 ID、外部清单 ID、已删除/已完成 ID，以及不完整集合，避免筛选状态下破坏其他记录。胶囊未完成数量是派生值，不作为独立易失真的统计列维护。

### 5.4 持久化、备份与异常

每次添加、编辑确认、勾选、删除、恢复和排序立即进入串行持久化队列。界面位置和连续滑动设置可以合并写入。业务操作均有事务边界；失败不部分更新任务与计数。

SQLite 工作在 UI 线程以外。Microsoft.Data.Sqlite 的异步 ADO.NET 方法仍同步执行，不能只给方法名加 Async 就视为消除了卡顿。[R7] 使用受控串行工作队列，连接不被多个线程同时操作；结果通过 UI Dispatcher 更新绑定。可启用 WAL，但不能把 WAL 当成备份。

正常退出需要等待有效编辑和待提交队列完成。已经显示“保存完成”的记录必须在数据库中；进程被强制结束时，不保证尚未提交的编辑，但不能损坏之前已提交的数据。

迁移和导入前创建一致性备份；常规备份保留最近 10 份，备份成功后才轮换旧文件。采用 SQLite 备份能力（如 BackupDatabase），不能在活跃 WAL 模式下只复制主 .db 文件来假装备份。[R8]

JSON 导出包含 schemaVersion、导出时间、所有清单、任务（含软删除记录）及可移植设置。导入先校验格式、版本、大小和引用完整性，展示记录数，并要求用户确认“替换业务数据”；先备份，再事务写入。导入不直接应用源机器的屏幕坐标。首版不做自动合并。错误格式或较新不支持版本必须拒绝且不改旧数据。

数据库损坏或权限错误时，不得自动建空库覆盖原文件。进入清楚标识的恢复流程，允许重试、打开数据目录或选择备份；保留原始文件。错误日志默认不记录任务标题或正文。

本地文件不等于加密存储。首版不加密数据库或 JSON 导出，依赖当前系统账户的文件权限；README 应说明这一点。应用不上传任务，不启用遥测，不执行任务文本中的脚本或命令。

## 6. 开发顺序与完成门槛

详尽任务见 TASKS.md；验收测试见 ACCEPTANCE.md。必须按依赖顺序推进，阶段有可观察结果，不一次把全部功能写入 MainWindow。

| 阶段 | 主要内容 | 可交付结果 |
| --- | --- | --- |
| M0 | 环境检查、解决方案和分层、测试项目、依赖锁定 | 可构建的空白 WPF 工程与清楚的运行说明 |
| M1 | 原生透明窗体、拖动/缩放、托盘、单胶囊折叠原型 | 在 Windows 验证真实透明、点击、收起、找回；核心窗口风险已暴露 |
| M2 | SQLite 模型、迁移、服务、串行写入、基础备份 | 真实数据操作和持久化测试，不依赖 UI |
| M3 | 单清单任务面板、增改完成删除撤销、排序、设置持久化 | 可以日常使用的任务小组件，重启后数据保持 |
| M4 | 多清单、贴边胶囊队列、共享交互预览和状态机 | 参考交互链完整：贴边→悬停→勾选→打开→收起 |
| M5 | 多屏/DPI、生命周期、单实例唤起、最近删除、导入导出与恢复 | 数据与窗口不会轻易丢失，异常和恢复有入口 |
| M6 | 回归、性能观测、发布脚本和自包含包 | 在 Windows 验证的发布目录包和真实测试记录 |

M1 中用于窗体验证的占位内容仅限明确的开发模式；M3 起默认入口必须使用真实 SQLite 数据。禁止将模拟界面冒充可用应用。

M4 的悬停状态机先写可复现的单元测试，再连接 WPF 窗口事件。动画是最后的润色，不得先追逐像素级复刻而推迟任务可靠保存。

## 7. 测试、交付与过程要求

每阶段都要更新 docs/STATUS.md：实现了什么、改了哪些文件、执行了什么命令、结果如何、哪些桌面测试还没执行。没有跑过的项目保持“待验证”，不能写“全部通过”。

基础命令在项目生成后应可用：

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release
dotnet test TaskTodo.sln -c Release
dotnet run --project src/TaskTodo.App/TaskTodo.App.csproj
```

必须区分构建、自动化测试和 Windows GUI/多显示器实测。Linux 或无交互桌面环境可运行适用的核心测试，但不能据此宣称 Windows UI、托盘和混合 DPI 通过。遇到缺失 SDK、无网络或 GUI 环境限制时，记录真实障碍；不擅自换成网页方案。

测试数据库和导入文件全部创建在测试临时目录，绝不触碰用户真实 LocalAppData。性能观测使用临时测试数据，不向真实收件箱注入任务。记录空闲 CPU、启动过程、1000 条任务滚动和反复悬停时的表现；不照搬参考项目的高刷新率或内存承诺。

第一版发布 win-x64 自包含目录包，保留 SQLite 和运行时需要的文件，不强求单 exe。[R9] 预期发布命令：

```powershell
dotnet publish src/TaskTodo.App/TaskTodo.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

不得在没有真实 Windows 发布验证的情况下把输出称为“已测试安装包”。首次交付应包含 README、实际构建/测试记录、手工测试步骤、已知限制和数据备份说明。

不执行用户没有要求的系统修改，不静默打开开机自启，不上传代码或推送远端，不覆盖无关文件。已有工作目录先检查状态、保留未提交内容；如果已有 AGENTS.md 则合并适用约定，不粗暴覆盖。

## 8. 参考依据

核对时间为 2026-09-20。以下资料用于确认技术能力与参考交互，不代表 TaskTodo 已实现。未在当前环境运行 PaperTodo，也未逐帧验证参考 GIF；视觉和动效对比属于后续 Windows 手工验收。

- [R1] PaperTodo README 与项目文件：胶囊、悬停预览、通用待办及 .NET/WPF 工程。
- [R2] Microsoft：WPF Overview，XAML、数据绑定与 Windows 支持范围。
- [R3] PaperTodo LICENSE：现有许可与附加许可。
- [R4] Microsoft：CommunityToolkit.Mvvm。
- [R5] Microsoft：Microsoft.Data.Sqlite Overview。
- [R6] Microsoft：Window.AllowsTransparency。
- [R7] Microsoft：Microsoft.Data.Sqlite Async limitations。
- [R8] Microsoft：SQLite Online backup。
- [R9] Microsoft：.NET application publishing overview。
- [R10] OpenAI：Custom instructions with AGENTS.md。

```text
R1 README: https://github.com/snownico0722/PaperTodo
R1 project: https://raw.githubusercontent.com/snownico0722/PaperTodo/main/PaperTodo.csproj
R1 visual reference: https://github.com/snownico0722/PaperTodo/blob/main/assets/edge4.0.gif
R2: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/
R3: https://github.com/snownico0722/PaperTodo/blob/main/LICENSE.md
R4: https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/
R5: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/
R6: https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0
R7: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
R8: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup
R9: https://learn.microsoft.com/en-us/dotnet/core/deploying/
R10: https://developers.openai.com/codex/guides/agents-md
```
