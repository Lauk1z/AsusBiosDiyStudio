# ASUS BIOS DIY Studio

面向华硕 AMI Aptio BIOS 的通用视觉资源编辑器，不绑定 PRIME、ROG 或某一块具体主板。

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

1. 双击 `AsusBiosDiyStudio.exe`。
2. 选择需要修改的源 BIOS CAP。
3. 如需从其他 BIOS 复制图片，再选择参考 BIOS；使用自己的图片时可留空。
4. 选择要扫描的 UI 图片、启动 Logo 和主题颜色。
5. 必要时在“资源 GUID 设置”中修改 GUID 与 Section Type。
6. 点击“加载并扫描”。
7. 在各标签页中选择资源并加入修改清单。
8. 在“修改清单与构建”中选择输出目录并构建。

华硕不同系列常使用相似结构，但不能保证 GUID、图片包格式或资源索引相同。程序默认值来自当前已分析的 ASUS AM5 固件；其他系列若使用不同 GUID，需要手动填写。无法识别的结构会停止，不会标记为构建成功。

修改后的 ASUS Capsule 原厂数字签名可能失效。文件级验证不代表实机安全或可启动，只建议通过主板 USB BIOS FlashBack 测试，并始终保留官方恢复 BIOS。


## 下载

请从 [Releases](https://github.com/Lauk1z/AsusBiosDiyStudio/releases) 下载 Windows 版本，并使用发布页提供的 `SHA256SUMS.txt` 校验文件完整性。
