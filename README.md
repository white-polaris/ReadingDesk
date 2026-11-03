# 研读桌面 / ReadingDesk

版本：2.0.4。参赛主题：科研与知识挖掘。

面向大学生的 Windows 文献阅读工具：粘贴摘要或文献片段，整理研究问题、方法、结论、关键词与局限；对照原文修改后保存为阅读任务，完成后离线回顾。

## 下载与运行

[下载 Windows x64 最终交付包](https://github.com/white-polaris/ReadingDesk/releases/latest)。压缩包包含可运行程序与精简功能源码，无需安装 .NET；解压后双击 `程序/Start.cmd`，数据保存在旁边自动创建的 `Data` 文件夹中。

已有旧版数据时，从托盘彻底退出旧版，只替换原文件夹中的 ReadingDesk.exe，保留原启动脚本、Data / DemoData 和配置，再用原脚本启动。

API Key 由用户在本机“模型设置”中输入，两种模式分别加密保存。仓库和下载包不包含用户密钥或运行时配置。

## 功能

- 新建、修改、保存与完成归档阅读任务，支持截止日期、拖动排序、撤销和本地备份。
- 原文与笔记并排核对；引文不匹配时显示具体结论编号与引用，定位字段，修改后即时更新。
- 一个 DeepSeek 预设和一个通用 HTTPS / Bearer / Chat Completions 文本接口。通用模式可选强制 JSON，预览最终地址。
- 两种模式分别使用 Windows 当前用户保护加密保存密钥；已有笔记可离线查看。
- 可取消请求、超时和错误提示；透明背景、托盘与窗口交互。
- 按钮、卡片与字段使用统一深色悬停提示，长说明和网址自动换行。

输入上限 12,000 字符，只分析粘贴片段；原文网址仅保存为链接，不解析 PDF 或自动抓取全文。引文校验忽略空白、保留符号和大小写，不能代替人工核对结论。

## 编译与发布

环境：Windows x64，.NET 8 SDK（需具备 .NET 8 与 WPF 桌面引用包）。本项目使用 .NET 内置组件，无第三方模型 SDK。

在本目录执行：

```powershell
.\Build.ps1
```

编译结果位于 `src/bin/Release/net8.0-windows`。生成可独立运行、无需安装 .NET 的 Windows x64 程序：

```powershell
.\Build.ps1 -Task Portable
```

发布结果为 `dist/win-x64/ReadingDesk.exe`。首次发布需访问微软 NuGet 源下载运行时构建包。也可用 Visual Studio 打开 [DDLDesk.csproj](src/DDLDesk.csproj)。

## 代码结构

| 文件 | 职责 |
| --- | --- |
| src/App.xaml.cs | 启动、单实例、数据路径和启动项 |
| src/MainWindow.cs | 主界面、卡片与交互 |
| src/Core.cs | 任务、排序、保存与恢复 |
| src/ReadingNote.cs | 阅读数据、引文校验、归档与撤销 |
| src/ReadingAnalysisService.cs | 模型请求、响应解析、取消与超时 |
| src/ModelSettings.cs | 双模式配置、密钥持久化与迁移 |
| src/ModelSettingsDialog.cs | 模型设置与连接测试 |
| src/ReadingDialogs.cs | 原文输入、核对、保存与归档查看 |
| src/UserKeyProtection.cs | Windows 当前用户密钥保护 |
| src/WindowAppearance.cs | 透明背景与窗口关联 |
| src/Theme.xaml、Ui.cs、Native.cs、DragGhost.cs、SmoothScroll.cs | 样式、控件与桌面交互 |

## 数据

默认数据目录为 `%LOCALAPPDATA%\ReadingDesk\Data`；可通过 `--data-dir` 指定独立目录，`--no-startup` 禁止修改登录启动项。任务和模型配置分开保存，不含内置 API Key。测试连接与生成由用户主动触发，使用所选平台账户；查看已保存笔记不请求模型。
