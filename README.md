# ASUS BIOS DIY Studio

面向华硕 AMI Aptio BIOS 的通用视觉资源编辑器，不绑定 PRIME、ROG 或某一块具体主板。

当前版本 **v2.0.0** 使用 Windows App SDK、XAML 和 .NET 10 从界面框架重建为原生 WinUI 3 应用，不是 WinForms 换皮。
任务栏和 EXE 使用与标题栏一致的芯片图标，包含适配不同 DPI 的多种分辨率。

## 可以编辑

- 浏览 BIOS UI 图片包中的全部 PNG 资源及索引；
- 选择任意索引，替换为自己的 PNG/BMP/JPG；
- 从另一份参考 BIOS 复制同索引图片；
- 等比居中、拉伸、等比裁剪或要求尺寸完全一致；
- 替换启动 Logo，并自动转换为源 Logo 的像素尺寸；
- 扫描 AMITSE 结构化主题色，选择具体颜色后进行 DIY；
- 自定义 UI、Boot Logo、AMITSE、SetupData 的 GUID 与 Section Type；
- 自定义 FlashBack 文件名。

## 安全约束

- 不提供 SetupData、AGESA、微码、内存训练或硬件初始化模块替换功能；
- 颜色只修改 AMITSE `.data` 中具有 `owner/property` 结构的记录，不全文替换 RGB；
- 所有图片资源保持原索引；
- 构建后自动反向提取所有修改模块并进行 SHA256 校验；完整文本树报告不是成功条件；
- SetupData 能够提取时必须与源 BIOS 保持一致；
- CAP 文件大小及前 4096 字节必须与源 BIOS 一致。

## 使用步骤

1. 完整解压便携包，双击 `AsusBiosDiyStudio.WinUI.exe`，不要只复制 EXE。
2. 选择需要修改的源 BIOS CAP。
3. 如需从其他 BIOS 复制图片，再选择参考 BIOS；使用自己的图片时可留空。
4. 选择要扫描的 UI 图片、启动 Logo 和主题颜色。
5. 必要时在“资源 GUID 设置”中修改 GUID 与 Section Type。
6. 点击“加载并扫描”。
7. 从左侧导航进入图片资源、启动 Logo 或主题颜色页面，选择资源并加入修改清单。
8. 在“修改清单与构建”中选择输出目录并构建。

华硕不同系列常使用相似结构，但不能保证 GUID、图片包格式或资源索引相同。程序默认值来自当前已分析的 ASUS AM5 固件；其他系列若使用不同 GUID，需要手动填写。无法识别的结构会停止，不会标记为构建成功。

修改后的 ASUS Capsule 原厂数字签名可能失效。文件级验证不代表实机安全或可启动，只建议通过主板 USB BIOS FlashBack 测试，并始终保留官方恢复 BIOS。


## 下载

请从 [v2.0.0 发布页](https://github.com/Lauk1z/AsusBiosDiyStudio/releases/tag/v2.0.0) 下载 Windows x64 便携包，并使用发布页提供的 `SHA256SUMS.txt` 校验文件完整性。
包内包含 .NET、Windows App SDK 运行库、Node.js 和 UEFI 工具；不包含主板 BIOS 固件。
旧版 WinForms 便携包仍保留在 [v1.0.0 发布页](https://github.com/Lauk1z/AsusBiosDiyStudio/releases/tag/v1.0.0)。

## 源码与构建

- `AsusBiosDiyStudio.WinUI/`：WinUI 3 界面、芯片图标和发布脚本。
- `AsusBiosDiyStudio/src/Engine.cs`：与原版共用的 BIOS 处理引擎。
- `tools/`：图片包处理和结构化颜色处理脚本。

需要 Windows x64、.NET 10 SDK 和 Windows SDK。构建已在安装 Visual Studio 和 Windows SDK 的环境中验证。
先将本版本便携包解压到源码输出目录以外的位置，用 `-RuntimeDirectory` 指定其中含 `runtime`、`tools` 子目录的应用文件夹：

```powershell
.\AsusBiosDiyStudio.WinUI\build.ps1 -RuntimeDirectory 'C:\Downloads\AsusBiosDiyStudio.WinUI'
```

该目录只用于取得 Node.js、UEFIExtract 和 UEFIReplace，不会从中替换界面源码或 BIOS 引擎。
首次构建需要连接 NuGet；后续可加 `-NoRestore`。可使用 `-DotNet <dotnet.exe 路径>` 指定 .NET SDK。
输出在 `AsusBiosDiyStudio.WinUI/dist/`。源码仓库不包含运行库、第三方工具二进制、BIOS 固件或本地分析资料。
