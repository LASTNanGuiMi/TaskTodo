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

