# TaskTodo — 项目约定

本项目开发一个 Windows 11 桌面任务小组件。用户要求参考 PaperTodo 的透明浮窗、贴边胶囊与悬停预览，但使用通用任务/清单模型。

## 开始工作前

读取 docs/IMPLEMENTATION_PLAN.md、docs/TASKS.md、docs/ACCEPTANCE.md、docs/STATUS.md。先检查当前目录、未提交改动和 dotnet --info。已有文件要保留，不覆盖无关内容。此规划默认独立新工程，不直接 fork 或复制 PaperTodo 的实现和视觉资源。

## 技术与范围

使用 C#、.NET 10、WPF、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite。前端是 XAML/ViewModel，本地后端是 C# 服务/SQLite；不建立网页、HTTP 服务、账号、云同步或插件系统。使用稳定 SDK/依赖并锁定实际版本。

App 管视图与原生桌面功能；Core 管模型、业务规则和可测试状态机；Infrastructure 管数据库与备份。不要把 SQL 写进 Window 或 ViewModel，也不要让 Core 依赖 WPF。

## 不可破坏的行为

背景透明度只改变背景，不把文字和按钮一起变透明。禁止全屏透明顶层遮挡窗口，不默认启用鼠标穿透。纯悬停不抢键盘焦点。鼠标进入预览、打开菜单、拖动、输入法组合和文本编辑时，不能错误收起。

任务的单一事实来源是服务提交成功后的数据。多视图共享快照；使用显式 SetCompleted(id, bool)，防止重复切换。SQLite I/O 离开 UI 线程并串行处理，连接不并发跨线程操作。不得吞掉保存错误。

任务存入用户 LocalAppData；测试仅用临时目录。删除采用软删除及撤销。已有数据损坏时禁止静默覆盖空库。备份采用一致性备份，不单独拷贝活跃 WAL 数据库主文件。退出前处理有效编辑并等待保存队列。

DIP 与物理像素分开；使用目标显示器工作区。窗口出屏后必须能从托盘找回。多屏、DPI、托盘和焦点必须在 Windows 验证，编译通过不等于 GUI 通过。

## 工作方式

按用户本轮指定的阶段实现。首次执行默认只做 M0、M1；之后根据 STATUS.md 继续下一阶段，不重复从零生成项目。规格中的明确默认值直接采用；非关键取舍记录在 STATUS.md，不把整轮任务变成反复询问或再次写方案。

每阶段产生实际代码、对应测试、运行说明。只有开发原型可有明确标注的占位数据；M3 起默认入口必须接真实 SQLite，不能只完成静态界面。

运行适用的 restore/build/test，并记录完整命令和结果。缺少环境时写“未执行/待验证”和原因，不虚构通过。禁止未经要求推送远端、静默修改系统启动设置、清空用户数据或删除未提交工作。

输出本阶段变更摘要、构建与测试结果、桌面验证步骤、已知问题、下一阶段入口。同步更新 docs/TASKS.md 与 docs/STATUS.md。

## 预期命令（工程生成后）

```powershell
dotnet restore TaskTodo.sln
dotnet build TaskTodo.sln -c Release
dotnet test TaskTodo.sln -c Release
dotnet run --project src/TaskTodo.App/TaskTodo.App.csproj
```
