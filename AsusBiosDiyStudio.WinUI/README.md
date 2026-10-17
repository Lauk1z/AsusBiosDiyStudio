# ASUS BIOS DIY Studio — WinUI 3

v2.0.0 是原生 WinUI 3 桌面应用，使用 Windows App SDK 2.5.1、XAML 和 .NET 10。
没有 WinForms 窗口、XAML Islands、WebView 或自绘 WinUI 仿制控件。

## 运行

完整解压便携包，运行 `AsusBiosDiyStudio.WinUI.exe`。请保留同目录的 DLL、PRI、运行库、工具和脚本，不要只复制 EXE。
包内包含 .NET 与 Windows App SDK 运行库，无需管理员安装。支持 x64 Windows 10 1809 及以上版本；Mica 背景由系统支持情况决定。

## 功能

- 源 BIOS、可选参考 BIOS；选择 UI 图片、启动 Logo、主题色扫描范围。
- 原生 `NavigationView`、虚拟化 `GridView`、`ListView`、`ComboBox`、`ContentDialog` 和 `ColorPicker`。
- 图片索引、尺寸和对比预览；自定义图片、同索引参考图片、原图导出和撤销。
- 等比居中、拉伸、等比裁剪、要求尺寸一致；启动 Logo 转换为源图尺寸和 BMP。
- 主题色多选编辑，保留各记录 Alpha；自定义 GUID、Section Type 与 FlashBack 文件名。
- 构建修改清单、反向提取验证和 SHA256 报告。

## 后端与安全边界

通过项目链接直接编译 `../AsusBiosDiyStudio/src/Engine.cs`，与原版共用同一份 BIOS 处理源码。
`System.Drawing.Common` 仅用于图像转换，不用于界面。

不替换 SetupData、AGESA、微码、内存训练或硬件初始化模块。构建时校验修改模块、可提取的 SetupData、CAP 大小及前 4096 字节。
文件级验证不能证明可安全刷写或启动。不同主板的 GUID、图片格式和索引不保证相同，请始终保留官方恢复 BIOS。

## 构建

需要 .NET 10 SDK。运行 `build.ps1`，或用 `build.ps1 -DotNet <dotnet.exe 路径>` 指定 SDK。
脚本优先使用工作区 `.buildtools/dotnet`，NuGet 缓存也放在工作区。
首次构建需要访问 NuGet；后续可以使用 `-NoRestore`。

输出位于本项目的 `dist`，不会覆盖旧 WinForms 版本。

参考：[WinUI 官方项目](https://github.com/microsoft/microsoft-ui-xaml)、[自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)。
