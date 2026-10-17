using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AsusBiosDiyStudio
{
    internal sealed class ResourceSettings
    {
        public string UiGuid = "CC5840D2-D8EA-459E-BAF4-349AC710EBBE";
        public string UiType = "19";
        public string BootGuid = "7BB28B99-61BB-11D5-9A5D-0090273FC14D";
        public string BootType = "19";
        public string AmitseGuid = "B1DA0ADF-4F77-4070-A88E-BFFE1C60529A";
        public string AmitseType = "10";
        public string SetupGuid = "FE612B72-203C-47B1-8560-A66D946EB371";
        public string SetupType = "FF";
        public string FlashbackName = "A5660.CAP";
    }

    internal sealed class UiImageEntry
    {
        public int Index;
        public int Width;
        public int Height;
        public string FilePath;
        public string Hash;
    }

    internal sealed class ColorEntry
    {
        public uint Value;
        public Color Rgb;
        public int Alpha;
        public int Count;
        public string Properties;
        public uint? Replacement = null;
        public string ArgbText { get { return "0x" + Value.ToString("x8"); } }
        public string RgbText { get { return string.Format("#{0:X2}{1:X2}{2:X2}", Rgb.R, Rgb.G, Rgb.B); } }
    }

    internal sealed class DiyProject : IDisposable
    {
        public string SourceCap;
        public string DonorCap;
        public string WorkDirectory;
        public ResourceSettings Settings;
        public string SourceUi;
        public string SourceBoot;
        public string SourceAmitse;
        public string SourceSetup;
        public string DonorUi;
        public string DonorBoot;
        public List<UiImageEntry> SourceImages = new List<UiImageEntry>();
        public List<UiImageEntry> DonorImages = new List<UiImageEntry>();
        public List<ColorEntry> Colors = new List<ColorEntry>();
        public Dictionary<int, string> UiReplacements = new Dictionary<int, string>();
        public string BootReplacement;
        public bool KeepFiles;

        public void Dispose()
        {
            if (KeepFiles || string.IsNullOrEmpty(WorkDirectory) || !Directory.Exists(WorkDirectory)) return;
            try { Directory.Delete(WorkDirectory, true); } catch { }
        }
    }

    internal sealed class BuildResult
    {
        public string CapPath;
        public string FlashbackPath;
        public string ReportPath;
        public string Sha256;
    }

    internal sealed class ToolRunner
    {
        private readonly string _baseDirectory;
        private readonly Action<string> _log;
        private Process _process;
        private volatile bool _cancel;

        public string Node { get { return Path.Combine(_baseDirectory, "runtime", "node.exe"); } }
        public string Extract { get { return Path.Combine(_baseDirectory, "tools", "UEFIExtract.exe"); } }
        public string Replace { get { return Path.Combine(_baseDirectory, "tools", "UEFIReplace.exe"); } }
        public string Scripts { get { return Path.Combine(_baseDirectory, "scripts"); } }

        public ToolRunner(Action<string> log)
        {
            _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _log = log ?? delegate { };
        }

        public void SelfTest()
        {
            Require(Node); Require(Extract); Require(Replace);
            string[] scripts = { "unpack_asus_ui_bundle.mjs", "replace_ui_bundle_images.mjs",
                "inventory_amitse_diy_colors.mjs", "patch_amitse_diy_colors.mjs" };
            foreach (string script in scripts) Require(Path.Combine(Scripts, script));
            Run(Node, new[] { "--version" }, "检查内置运行环境");
        }

        public void Cancel()
        {
            _cancel = true;
            try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
        }

        public string RunNode(string script, string[] args, string label)
        {
            List<string> all = new List<string>();
            all.Add(Path.Combine(Scripts, script));
            all.AddRange(args);
            return Run(Node, all.ToArray(), label);
        }

        public string Run(string executable, string[] args, string label)
        {
            if (_cancel) throw new OperationCanceledException();
            _log("\r\n▶ " + label);
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = executable;
            info.Arguments = string.Join(" ", args.Select(Quote).ToArray());
            info.WorkingDirectory = _baseDirectory;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            StringBuilder captured = new StringBuilder();
            using (ManualResetEvent outputDone = new ManualResetEvent(false))
            using (ManualResetEvent errorDone = new ManualResetEvent(false))
            using (Process process = new Process())
            {
                _process = process;
                process.StartInfo = info;
                process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { outputDone.Set(); return; }
                    lock (captured) captured.AppendLine(e.Data);
                    _log(e.Data);
                };
                process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { errorDone.Set(); return; }
                    lock (captured) captured.AppendLine(e.Data);
                    _log(e.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                while (!process.WaitForExit(200))
                {
                    if (_cancel) { try { process.Kill(); } catch { } throw new OperationCanceledException(); }
                }
                int code = process.ExitCode;
                WaitHandle.WaitAll(new WaitHandle[] { outputDone, errorDone }, 100);
                _process = null;
                string output;
                lock (captured) output = captured.ToString();
                if (_cancel) throw new OperationCanceledException();
                if (code != 0) throw new InvalidOperationException(label + "失败，退出码 " + code + "。\r\n" + output);
                return output;
            }
        }

        private static void Require(string file) { if (!File.Exists(file)) throw new FileNotFoundException("缺少组件", file); }
        private static string Quote(string value)
        {
            if (value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"')) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class DiyEngine
    {
        private readonly ToolRunner _tools;
        private readonly Action<string> _log;

        public DiyEngine(Action<string> log)
        {
            _log = log ?? delegate { };
            _tools = new ToolRunner(_log);
        }

        public void Cancel() { _tools.Cancel(); }

        public DiyProject Load(string sourceCap, string donorCap, ResourceSettings settings,
            bool loadUi, bool loadBoot, bool loadColors)
        {
            if (!File.Exists(sourceCap)) throw new FileNotFoundException("源 BIOS 不存在", sourceCap);
            if (!string.IsNullOrWhiteSpace(donorCap) && !File.Exists(donorCap)) throw new FileNotFoundException("参考 BIOS 不存在", donorCap);
            if (!loadUi && !loadBoot && !loadColors) throw new InvalidOperationException("至少选择一种要编辑的资源。");
            ValidateSettings(settings);
            _tools.SelfTest();

            DiyProject project = new DiyProject();
            project.SourceCap = Path.GetFullPath(sourceCap);
            project.DonorCap = string.IsNullOrWhiteSpace(donorCap) ? null : Path.GetFullPath(donorCap);
            project.Settings = settings;
            project.WorkDirectory = Path.Combine(Path.GetTempPath(), "AsusBiosDiyStudio", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(project.WorkDirectory);
            _log("项目工作目录：" + project.WorkDirectory);
            _log("源 BIOS SHA256=" + HashFile(project.SourceCap));

            try
            {
                if (loadUi)
                {
                    project.SourceUi = Extract(project.SourceCap, settings.UiGuid, settings.UiType, "source-ui", project.WorkDirectory, "提取源 UI 图片包");
                    string sourceImages = Path.Combine(project.WorkDirectory, "source-images");
                    string sourceManifest = Path.Combine(project.WorkDirectory, "source-images.txt");
                    _tools.RunNode("unpack_asus_ui_bundle.mjs", new[] { project.SourceUi, sourceImages, sourceManifest }, "解析源 UI 图片包");
                    project.SourceImages = ParseImageManifest(File.ReadAllText(sourceManifest, Encoding.UTF8), sourceImages);
                    if (project.SourceImages.Count == 0) throw new InvalidOperationException("UI 图片包中没有识别到图片。");
                    if (project.DonorCap != null)
                    {
                        project.DonorUi = Extract(project.DonorCap, settings.UiGuid, settings.UiType, "donor-ui", project.WorkDirectory, "提取参考 UI 图片包");
                        string donorImages = Path.Combine(project.WorkDirectory, "donor-images");
                        string donorManifest = Path.Combine(project.WorkDirectory, "donor-images.txt");
                        _tools.RunNode("unpack_asus_ui_bundle.mjs", new[] { project.DonorUi, donorImages, donorManifest }, "解析参考 UI 图片包");
                        project.DonorImages = ParseImageManifest(File.ReadAllText(donorManifest, Encoding.UTF8), donorImages);
                    }
                }

                if (loadBoot)
                {
                    project.SourceBoot = Extract(project.SourceCap, settings.BootGuid, settings.BootType, "source-boot", project.WorkDirectory, "提取源启动 Logo");
                    ValidateImage(project.SourceBoot, "源启动 Logo");
                    if (project.DonorCap != null)
                    {
                        project.DonorBoot = Extract(project.DonorCap, settings.BootGuid, settings.BootType, "donor-boot", project.WorkDirectory, "提取参考启动 Logo");
                        ValidateImage(project.DonorBoot, "参考启动 Logo");
                    }
                }

                if (loadColors)
                {
                    project.SourceAmitse = Extract(project.SourceCap, settings.AmitseGuid, settings.AmitseType, "source-amitse", project.WorkDirectory, "提取 AMITSE");
                    string inventory = _tools.RunNode("inventory_amitse_diy_colors.mjs", new[] { project.SourceAmitse }, "扫描结构化主题色");
                    project.Colors = ParseColors(inventory);
                }

                project.SourceSetup = TryExtract(project.SourceCap, settings.SetupGuid, settings.SetupType, "source-setup", project.WorkDirectory, "提取 SetupData 校验基线");
                return project;
            }
            catch
            {
                project.KeepFiles = true;
                throw;
            }
        }

        public BuildResult Build(DiyProject project, string outputDirectory, string outputName, string flashbackName)
        {
            if (project == null) throw new InvalidOperationException("请先加载 BIOS 项目。");
            bool colorChanges = project.Colors.Any(c => c.Replacement.HasValue && c.Replacement.Value != c.Value);
            if (project.UiReplacements.Count == 0 && string.IsNullOrEmpty(project.BootReplacement) && !colorChanges)
                throw new InvalidOperationException("修改清单为空。");
            ValidateFileName(outputName, "输出 CAP 名称");
            ValidateFileName(flashbackName, "FlashBack 名称");
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidOperationException("请选择输出目录。");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);
            string finalCap = Path.Combine(outputDirectory, outputName);
            if (File.Exists(finalCap)) throw new InvalidOperationException("输出文件已存在：" + finalCap);

            string current = project.SourceCap;
            string expectedUi = project.SourceUi;
            string expectedBoot = project.SourceBoot;
            string expectedAmitse = project.SourceAmitse;
            int stage = 0;

            if (project.UiReplacements.Count > 0)
            {
                expectedUi = Path.Combine(project.WorkDirectory, "modified-ui.bin");
                List<string> args = new List<string>();
                args.Add(project.SourceUi); args.Add(expectedUi);
                foreach (KeyValuePair<int, string> item in project.UiReplacements.OrderBy(p => p.Key)) args.Add(item.Key + "=" + item.Value);
                _tools.RunNode("replace_ui_bundle_images.mjs", args.ToArray(), "构建自定义 UI 图片包");
                current = Replace(current, project.Settings.UiGuid, project.Settings.UiType, expectedUi,
                    Path.Combine(project.WorkDirectory, "stage-" + (++stage) + ".CAP"), "写入 UI 图片包");
            }

            if (!string.IsNullOrEmpty(project.BootReplacement))
            {
                expectedBoot = project.BootReplacement;
                current = Replace(current, project.Settings.BootGuid, project.Settings.BootType, expectedBoot,
                    Path.Combine(project.WorkDirectory, "stage-" + (++stage) + ".CAP"), "写入启动 Logo");
            }

            if (colorChanges)
            {
                expectedAmitse = Path.Combine(project.WorkDirectory, "modified-amitse.bin");
                List<string> args = new List<string>();
                args.Add(project.SourceAmitse); args.Add(expectedAmitse);
                foreach (ColorEntry color in project.Colors.Where(c => c.Replacement.HasValue && c.Replacement.Value != c.Value))
                    args.Add("0x" + color.Value.ToString("x8") + "=0x" + color.Replacement.Value.ToString("x8"));
                _tools.RunNode("patch_amitse_diy_colors.mjs", args.ToArray(), "写入自定义结构化主题色");
                current = Replace(current, project.Settings.AmitseGuid, project.Settings.AmitseType, expectedAmitse,
                    Path.Combine(project.WorkDirectory, "stage-" + (++stage) + ".CAP"), "写入 AMITSE 主题色");
            }

            File.Copy(current, finalCap, false);
            if (expectedUi != null) VerifyExtracted(finalCap, project.Settings.UiGuid, project.Settings.UiType, expectedUi, "verify-ui", project.WorkDirectory, "UI 图片包");
            if (expectedBoot != null) VerifyExtracted(finalCap, project.Settings.BootGuid, project.Settings.BootType, expectedBoot, "verify-boot", project.WorkDirectory, "启动 Logo");
            if (expectedAmitse != null) VerifyExtracted(finalCap, project.Settings.AmitseGuid, project.Settings.AmitseType, expectedAmitse, "verify-amitse", project.WorkDirectory, "AMITSE");
            if (project.SourceSetup != null) VerifyExtracted(finalCap, project.Settings.SetupGuid, project.Settings.SetupType, project.SourceSetup, "verify-setup", project.WorkDirectory, "SetupData 未修改");

            FileInfo sourceInfo = new FileInfo(project.SourceCap);
            FileInfo finalInfo = new FileInfo(finalCap);
            if (sourceInfo.Length != finalInfo.Length) throw new InvalidOperationException("最终 CAP 大小与源 BIOS 不同。");
            if (!FirstBytesEqual(project.SourceCap, finalCap, 4096)) throw new InvalidOperationException("最终 CAP 前 4096 字节与源 BIOS 不同。");

            string flashDirectory = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputName) + "-USB-FlashBack");
            Directory.CreateDirectory(flashDirectory);
            string flash = Path.Combine(flashDirectory, flashbackName);
            if (File.Exists(flash)) throw new InvalidOperationException("FlashBack 文件已存在：" + flash);
            File.Copy(finalCap, flash, false);
            string hash = HashFile(finalCap);
            if (!string.Equals(hash, HashFile(flash), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("FlashBack 副本哈希不一致。");

            string report = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(outputName) + ".diy-verification.txt");
            WriteReport(report, project, finalCap, flash, hash, colorChanges);
            BuildResult result = new BuildResult();
            result.CapPath = finalCap; result.FlashbackPath = flash; result.ReportPath = report; result.Sha256 = hash;
            return result;
        }

        public string CreateUiReplacement(DiyProject project, int index, string inputImage, ImageFitMode mode)
        {
            UiImageEntry target = project.SourceImages.First(i => i.Index == index);
            string output = Path.Combine(project.WorkDirectory, "replacements", "ui-" + index.ToString("D4") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            ImageTransformer.Transform(inputImage, output, target.Width, target.Height, mode, ImageFormat.Png);
            project.UiReplacements[index] = output;
            return output;
        }

        public string CreateBootReplacement(DiyProject project, string inputImage, ImageFitMode mode)
        {
            if (project.SourceBoot == null) throw new InvalidOperationException("未加载启动 Logo。");
            int width, height;
            using (Image source = Image.FromFile(project.SourceBoot)) { width = source.Width; height = source.Height; }
            string output = Path.Combine(project.WorkDirectory, "replacements", "boot-logo.bmp");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            ImageTransformer.Transform(inputImage, output, width, height, mode, ImageFormat.Bmp);
            project.BootReplacement = output;
            return output;
        }

        private string Extract(string cap, string guid, string type, string name, string work, string label)
        {
            string directory = Path.Combine(work, name);
            _tools.Run(_tools.Extract, new[] { cap, guid, "-o", directory, "-m", "body", "-t", type }, label);
            string body = Path.Combine(directory, "body.bin");
            if (!File.Exists(body)) throw new InvalidOperationException(label + "未生成 body.bin。");
            return body;
        }

        private string TryExtract(string cap, string guid, string type, string name, string work, string label)
        {
            try { return Extract(cap, guid, type, name, work, label); }
            catch (Exception ex) { _log("提示：" + label + "未成功，将跳过该项验证。" + ex.Message); return null; }
        }

        private string Replace(string input, string guid, string type, string body, string output, string label)
        {
            _tools.Run(_tools.Replace, new[] { input, guid, type, body, "-o", output }, label);
            if (!File.Exists(output)) throw new InvalidOperationException(label + "未生成输出。");
            return output;
        }

        private void VerifyExtracted(string cap, string guid, string type, string expected, string name, string work, string label)
        {
            string actual = Extract(cap, guid, type, name, work, "反向提取 " + label);
            if (!string.Equals(HashFile(expected), HashFile(actual), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + "反向提取哈希不一致。");
            _log(label + "：反向验证通过。");
        }

        private static List<UiImageEntry> ParseImageManifest(string text, string directory)
        {
            List<UiImageEntry> entries = new List<UiImageEntry>();
            Regex regex = new Regex(@"IMAGE=(\d+) WIDTH=(\d+) HEIGHT=(\d+) SIZE=(\d+) SHA256=([0-9A-F]+) FILE=(.+)");
            foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = regex.Match(line);
                if (!match.Success) continue;
                UiImageEntry entry = new UiImageEntry();
                entry.Index = int.Parse(match.Groups[1].Value); entry.Width = int.Parse(match.Groups[2].Value);
                entry.Height = int.Parse(match.Groups[3].Value); entry.Hash = match.Groups[5].Value;
                entry.FilePath = Path.Combine(directory, match.Groups[6].Value.Trim()); entries.Add(entry);
            }
            return entries;
        }

        private static List<ColorEntry> ParseColors(string text)
        {
            List<ColorEntry> entries = new List<ColorEntry>();
            Regex regex = new Regex(@"COLOR=0x([0-9a-fA-F]{8}) RGB=#([0-9A-F]{6}) ALPHA=(\d+) COUNT=(\d+) PROPERTIES=(.*)");
            foreach (string line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = regex.Match(line); if (!match.Success) continue;
                uint value = Convert.ToUInt32(match.Groups[1].Value, 16);
                int rgb = Convert.ToInt32(match.Groups[2].Value, 16);
                ColorEntry entry = new ColorEntry(); entry.Value = value;
                entry.Rgb = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
                entry.Alpha = int.Parse(match.Groups[3].Value); entry.Count = int.Parse(match.Groups[4].Value);
                entry.Properties = match.Groups[5].Value; entries.Add(entry);
            }
            return entries;
        }

        private static void ValidateSettings(ResourceSettings settings)
        {
            string[] guids = { settings.UiGuid, settings.BootGuid, settings.AmitseGuid, settings.SetupGuid };
            foreach (string guid in guids)
            {
                Guid ignored;
                if (!Guid.TryParse(guid, out ignored)) throw new InvalidOperationException("GUID 格式无效：" + guid);
            }
        }

        private static void ValidateImage(string path, string label)
        {
            try { using (Image image = Image.FromFile(path)) { int w = image.Width; } }
            catch (Exception ex) { throw new InvalidOperationException(label + "不是 Windows 可识别的图片。", ex); }
        }

        private static void ValidateFileName(string name, string label)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetExtension(name), ".CAP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(label + "必须是有效的 .CAP 文件名。");
        }

        internal static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create()) using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static bool FirstBytesEqual(string first, string second, int count)
        {
            byte[] a = new byte[count], b = new byte[count];
            using (FileStream fa = File.OpenRead(first)) using (FileStream fb = File.OpenRead(second))
            { if (fa.Read(a, 0, count) != count || fb.Read(b, 0, count) != count) return false; }
            return a.SequenceEqual(b);
        }

        private static void WriteReport(string path, DiyProject project, string cap, string flash, string hash, bool colors)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("ASUS BIOS DIY Studio 构建验证报告");
            text.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("源 BIOS：" + project.SourceCap);
            text.AppendLine("源 SHA256：" + HashFile(project.SourceCap));
            text.AppendLine("参考 BIOS：" + (project.DonorCap ?? "未使用"));
            text.AppendLine("UI 图片替换：" + project.UiReplacements.Count);
            text.AppendLine("启动 Logo 替换：" + (!string.IsNullOrEmpty(project.BootReplacement)));
            text.AppendLine("主题色修改：" + project.Colors.Count(c => c.Replacement.HasValue && c.Replacement.Value != c.Value));
            text.AppendLine("输出 CAP：" + cap); text.AppendLine("FlashBack：" + flash); text.AppendLine("SHA256：" + hash);
            text.AppendLine(); text.AppendLine("反向提取验证已通过；SetupData 在可提取时保持源 BIOS 不变；CAP 大小和前 4096 字节保持不变。");
            text.AppendLine("警告：文件级验证不代表刷写安全。修改会使 ASUS Capsule 原厂签名失效，只使用 USB BIOS FlashBack。");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        }
    }

    internal enum ImageFitMode { Contain, Stretch, Crop, Exact }

    internal static class ImageTransformer
    {
        public static void Transform(string input, string output, int width, int height, ImageFitMode mode, ImageFormat format)
        {
            using (Image source = Image.FromFile(input))
            {
                if (mode == ImageFitMode.Exact && (source.Width != width || source.Height != height))
                    throw new InvalidOperationException(string.Format("图片必须正好是 {0}x{1}。", width, height));
                using (Bitmap target = new Bitmap(width, height, format.Guid == ImageFormat.Bmp.Guid ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(target))
                {
                    graphics.Clear(format.Guid == ImageFormat.Bmp.Guid ? Color.Black : Color.Transparent);
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    Rectangle destination = new Rectangle(0, 0, width, height);
                    if (mode == ImageFitMode.Contain)
                    {
                        double scale = Math.Min((double)width / source.Width, (double)height / source.Height);
                        int w = Math.Max(1, (int)Math.Round(source.Width * scale)); int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                        destination = new Rectangle((width - w) / 2, (height - h) / 2, w, h);
                    }
                    else if (mode == ImageFitMode.Crop)
                    {
                        double scale = Math.Max((double)width / source.Width, (double)height / source.Height);
                        int w = Math.Max(1, (int)Math.Round(source.Width * scale)); int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                        destination = new Rectangle((width - w) / 2, (height - h) / 2, w, h);
                    }
                    graphics.DrawImage(source, destination);
                    target.Save(output, format);
                }
            }
        }
    }
}
