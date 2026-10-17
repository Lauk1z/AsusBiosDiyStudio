using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AsusBiosDiyStudio.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly ObservableCollection<ImageResourceItem> _images = new();
        private readonly ObservableCollection<ColorRecordItem> _colors = new();
        private readonly ObservableCollection<string> _changes = new();
        private readonly StringBuilder _log = new();
        private ResourceSettings _settings = new();
        private DiyEngine _engine;
        private DiyProject _project;
        private bool _busy;
        private bool _closeWhenIdle;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        public MainWindow()
        {
            InitializeComponent();
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragRegion);
            double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1400 * scale), (int)(940 * scale)));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)(1100 * scale);
                presenter.PreferredMinimumHeight = (int)(800 * scale);
            }
            ResourceGrid.ItemsSource = _images;
            ColorList.ItemsSource = _colors;
            ChangesList.ItemsSource = _changes;
            Root.ActualThemeChanged += delegate { UpdateTitleBar(); };
            AppWindow.Closing += WindowClosing;
            UpdateTitleBar();
            RefreshChanges();
        }

        private void UpdateTitleBar()
        {
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonForegroundColor = Root.ActualTheme == ElementTheme.Dark
                ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        }

        private void NavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (ImagesPage == null || args.SelectedItem is not NavigationViewItem item) return;
            string page = (string)item.Tag;
            ImagesPage.Visibility = page == "images" ? Visibility.Visible : Visibility.Collapsed;
            BootPage.Visibility = page == "boot" ? Visibility.Visible : Visibility.Collapsed;
            ColorsPage.Visibility = page == "colors" ? Visibility.Visible : Visibility.Collapsed;
            BuildPage.Visibility = page == "build" ? Visibility.Visible : Visibility.Collapsed;
            PageTitle.Text = (string)item.Content;
            PageDescription.Text = page switch
            {
                "boot" => "对比启动画面，使用自己的图片或参考 BIOS Logo。",
                "colors" => "选择结构化颜色记录，调整文字、边框与高亮色。",
                "build" => "检查待执行修改，设置输出文件并构建 BIOS。",
                _ => "浏览资源索引，对比原图与替换效果。"
            };
        }

        private void InitializePicker(object picker)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        }

        private async Task<string> PickFileAsync(bool image)
        {
            FileOpenPicker picker = new();
            foreach (string extension in image ? new[] { ".png", ".bmp", ".jpg", ".jpeg" } : new[] { ".cap" })
                picker.FileTypeFilter.Add(extension);
            InitializePicker(picker);
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }

        private async void BrowseSource(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () => { string path = await PickFileAsync(false); if (path != null) SourcePath.Text = path; });
        }

        private async void BrowseDonor(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () => { string path = await PickFileAsync(false); if (path != null) DonorPath.Text = path; });
        }

        private async void BrowseOutput(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () =>
            {
                FolderPicker picker = new(); picker.FileTypeFilter.Add("*"); InitializePicker(picker);
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null) OutputDirectory.Text = folder.Path;
            });
        }

        private async void LoadProject(object sender, RoutedEventArgs args)
        {
            string source = SourcePath.Text.Trim(), donor = DonorPath.Text.Trim();
            bool images = LoadImages.IsChecked == true, boot = LoadBoot.IsChecked == true, colors = LoadColors.IsChecked == true;
            ResourceSettings settings = _settings;
            await RunAsync("正在扫描 BIOS 资源…", async () =>
            {
                _engine = new DiyEngine(AppendLog);
                DiyProject loaded = await Task.Run(() => _engine.Load(source, donor, settings, images, boot, colors));
                ResourceGrid.SelectedItem = null;
                SourcePreview.Source = DonorPreview.Source = ReplacementPreview.Source = null;
                BootSource.Source = BootDonor.Source = BootReplacement.Source = null;
                _images.Clear(); _colors.Clear();
                _project?.Dispose(); _project = loaded;
                foreach (UiImageEntry entry in loaded.SourceImages) _images.Add(new ImageResourceItem(entry));
                foreach (ColorEntry entry in loaded.Colors) _colors.Add(new ColorRecordItem(entry));
                ImageCount.Text = "图片资源 · " + _images.Count + " 项";
                GalleryEmpty.Visibility = _images.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                ImageInfo.Text = "选择左侧资源查看尺寸与替换效果。";
                BootSource.Source = LoadBitmap(loaded.SourceBoot);
                BootDonor.Source = LoadBitmap(loaded.DonorBoot);
                if (_images.Count > 0) ResourceGrid.SelectedIndex = 0;
                RefreshChanges(); SetStatus("资源扫描完成，可开始 DIY");
            });
        }

        private ImageResourceItem SelectedImage { get { return ResourceGrid.SelectedItem as ImageResourceItem; } }
        private ImageFitMode SelectedFitMode { get { return (ImageFitMode)Math.Max(0, FitMode.SelectedIndex); } }

        private void ImageSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (CustomImageButton == null) return;
            ImageResourceItem selected = SelectedImage;
            if (_project == null || selected == null)
            {
                SourcePreview.Source = DonorPreview.Source = ReplacementPreview.Source = null;
                UpdateControls(); return;
            }
            UiImageEntry donor = _project.DonorImages.FirstOrDefault(i => i.Index == selected.Index);
            SourcePreview.Source = LoadBitmap(selected.Entry.FilePath);
            DonorPreview.Source = LoadBitmap(donor?.FilePath);
            ReplacementPreview.Source = LoadBitmap(_project.UiReplacements.TryGetValue(selected.Index, out string path) ? path : null);
            ImageInfo.Text = "索引 #" + selected.Index + " · 源尺寸 " + selected.Dimensions +
                (donor == null ? " · 无参考图片" : " · 参考尺寸 " + donor.Width + "×" + donor.Height);
            UpdateControls();
        }

        private async void ReplaceCustomImage(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () =>
            {
                ImageResourceItem selected = SelectedImage;
                if (selected == null || _project == null) return;
                string file = await PickFileAsync(true); if (file == null) return;
                string result = _engine.CreateUiReplacement(_project, selected.Index, file, SelectedFitMode);
                ReplacementPreview.Source = LoadBitmap(result); selected.IsModified = true; RefreshChanges();
            });
        }

        private async void ReplaceDonorImage(object sender, RoutedEventArgs args)
        {
            await EditAsync(() =>
            {
                ImageResourceItem selected = SelectedImage;
                UiImageEntry donor = selected == null ? null : _project?.DonorImages.FirstOrDefault(i => i.Index == selected.Index);
                if (donor != null)
                {
                    string result = _engine.CreateUiReplacement(_project, selected.Index, donor.FilePath, SelectedFitMode);
                    ReplacementPreview.Source = LoadBitmap(result); selected.IsModified = true; RefreshChanges();
                }
                return Task.CompletedTask;
            });
        }

        private void ResetImage(object sender, RoutedEventArgs args)
        {
            if (_project == null || SelectedImage == null) return;
            _project.UiReplacements.Remove(SelectedImage.Index); SelectedImage.IsModified = false;
            ReplacementPreview.Source = null; RefreshChanges();
        }

        private async void ExportImage(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () =>
            {
                ImageResourceItem selected = SelectedImage; if (selected == null) return;
                FileSavePicker picker = new() { SuggestedFileName = "ui-" + selected.Index.ToString("D4") };
                picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" }); InitializePicker(picker);
                var file = await picker.PickSaveFileAsync();
                if (file != null) { File.Copy(selected.Entry.FilePath, file.Path, true); SetStatus("原图已导出：" + file.Path); }
            });
        }

        private async void ReplaceCustomBoot(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () =>
            {
                if (_project?.SourceBoot == null) return;
                string file = await PickFileAsync(true); if (file == null) return;
                BootReplacement.Source = LoadBitmap(_engine.CreateBootReplacement(_project, file, SelectedFitMode)); RefreshChanges();
            });
        }

        private async void ReplaceDonorBoot(object sender, RoutedEventArgs args)
        {
            await EditAsync(() =>
            {
                if (_project?.DonorBoot != null)
                { BootReplacement.Source = LoadBitmap(_engine.CreateBootReplacement(_project, _project.DonorBoot, SelectedFitMode)); RefreshChanges(); }
                return Task.CompletedTask;
            });
        }

        private void ResetBoot(object sender, RoutedEventArgs args)
        {
            if (_project == null) return;
            _project.BootReplacement = null; BootReplacement.Source = null; RefreshChanges();
        }

        private void ColorSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (SetColorButton != null) UpdateControls();
        }

        private async void SetColor(object sender, RoutedEventArgs args)
        {
            await EditAsync(async () =>
            {
                ColorRecordItem[] selected = ColorList.SelectedItems.Cast<ColorRecordItem>().ToArray();
                if (selected.Length == 0) return;
                uint initial = selected[0].Entry.Replacement ?? selected[0].Entry.Value;
                ColorPicker picker = new() { IsAlphaEnabled = false, Color = ToColor(initial) };
                ContentDialog dialog = CreateDialog("设置选中颜色", picker, "应用");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                foreach (ColorRecordItem item in selected)
                {
                    item.Entry.Replacement = ((uint)item.Entry.Alpha << 24) | ((uint)picker.Color.R << 16) | ((uint)picker.Color.G << 8) | picker.Color.B;
                    item.Refresh();
                }
                RefreshChanges();
            });
        }

        private void ResetColor(object sender, RoutedEventArgs args)
        {
            foreach (ColorRecordItem item in ColorList.SelectedItems.Cast<ColorRecordItem>())
            { item.Entry.Replacement = null; item.Refresh(); }
            RefreshChanges();
        }

        private async void BuildBios(object sender, RoutedEventArgs args)
        {
            string directory = OutputDirectory.Text.Trim(), name = OutputName.Text.Trim(), flash = FlashbackName.Text.Trim();
            await RunAsync("正在构建并反向验证…", async () =>
            {
                BuildResult result = await Task.Run(() => _engine.Build(_project, directory, name, flash));
                SetStatus("构建和文件级验证全部通过");
                ContentDialog dialog = CreateDialog("构建完成", new TextBlock
                {
                    Text = "FlashBack 文件：\n" + result.FlashbackPath + "\n\nSHA256：\n" + result.Sha256,
                    TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
                });
                await dialog.ShowAsync();
            });
        }

        private bool HasChanges { get { return _project != null && (_project.UiReplacements.Count > 0 ||
            _project.BootReplacement != null || _project.Colors.Any(c => c.Replacement.HasValue && c.Replacement != c.Value)); } }

        private void RefreshChanges()
        {
            _changes.Clear();
            if (_project != null)
            {
                foreach (var pair in _project.UiReplacements.OrderBy(p => p.Key)) _changes.Add("UI 图片 #" + pair.Key + " → " + Path.GetFileName(pair.Value));
                if (_project.BootReplacement != null) _changes.Add("启动 Logo → " + Path.GetFileName(_project.BootReplacement));
                foreach (ColorEntry color in _project.Colors.Where(c => c.Replacement.HasValue && c.Replacement != c.Value))
                    _changes.Add("主题色 " + color.RgbText + " → #" + (color.Replacement.Value & 0xFFFFFF).ToString("X6") + "（" + color.Count + " 条）");
            }
            ChangesTitle.Text = "待执行修改 · " + _changes.Count + " 项";
            if (_changes.Count == 0) _changes.Add("尚未选择任何修改");
            UpdateControls();
        }

        private void UpdateControls()
        {
            bool selected = !_busy && _project != null && SelectedImage != null;
            LoadButton.IsEnabled = SourcePath.IsEnabled = DonorPath.IsEnabled = GuidButton.IsEnabled = !_busy;
            SourceBrowseButton.IsEnabled = DonorBrowseButton.IsEnabled = OutputBrowseButton.IsEnabled = !_busy;
            OutputDirectory.IsEnabled = OutputName.IsEnabled = FlashbackName.IsEnabled = !_busy;
            LoadImages.IsEnabled = LoadBoot.IsEnabled = LoadColors.IsEnabled = !_busy;
            CancelButton.IsEnabled = _busy;
            CustomImageButton.IsEnabled = ExportImageButton.IsEnabled = selected;
            DonorImageButton.IsEnabled = selected && _project.DonorImages.Any(i => i.Index == SelectedImage.Index);
            ResetImageButton.IsEnabled = selected && _project.UiReplacements.ContainsKey(SelectedImage.Index);
            CustomBootButton.IsEnabled = !_busy && _project?.SourceBoot != null;
            DonorBootButton.IsEnabled = !_busy && _project?.DonorBoot != null;
            ResetBootButton.IsEnabled = !_busy && _project?.BootReplacement != null;
            SetColorButton.IsEnabled = ResetColorButton.IsEnabled = !_busy && ColorList.SelectedItems.Count > 0;
            BuildButton.IsEnabled = !_busy && HasChanges;
            BusyProgress.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RunAsync(string status, Func<Task> operation)
        {
            if (_busy) return;
            _busy = true; SetStatus(status); UpdateControls();
            try { await operation(); }
            catch (OperationCanceledException) { SetStatus("操作已取消；继续构建前请重新加载 BIOS。"); }
            catch (Exception error) { await ShowErrorAsync(error); }
            finally
            {
                _busy = false; UpdateControls();
                if (_closeWhenIdle) Close();
            }
        }

        private async Task EditAsync(Func<Task> operation)
        {
            if (_busy) return;
            try { await operation(); }
            catch (Exception error) { await ShowErrorAsync(error); }
        }

        private async Task ShowErrorAsync(Exception error)
        {
            AppendLog(error.ToString()); SetStatus("操作失败：" + error.Message);
            if (!_closeWhenIdle)
                await CreateDialog("操作失败", new TextBlock { Text = error.Message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }).ShowAsync();
        }

        private void CancelOperation(object sender, RoutedEventArgs args) { _engine?.Cancel(); }

        private void SetStatus(string text)
        {
            StatusText.Text = text; ToolTipService.SetToolTip(StatusText, text);
        }

        private void AppendLog(string message)
        {
            if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => AppendLog(message)); return; }
            _log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }

        private ContentDialog CreateDialog(string title, object content, string primary = null)
        {
            return new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = title, Content = content,
                PrimaryButtonText = primary ?? "", CloseButtonText = primary == null ? "关闭" : "取消",
                DefaultButton = primary == null ? ContentDialogButton.Close : ContentDialogButton.Primary
            };
        }

        private async void ShowLog(object sender, RoutedEventArgs args)
        {
            TextBox text = new() { Text = _log.Length == 0 ? "尚无运行日志。" : _log.ToString(), IsReadOnly = true,
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 540, MaxHeight = 480 };
            ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
            await CreateDialog("运行日志", text).ShowAsync();
        }

        private async void ShowGuidSettings(object sender, RoutedEventArgs args)
        {
            Grid grid = new() { RowSpacing = 12, ColumnSpacing = 12, MinWidth = 480 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            string[] labels = { "UI 图片包", "启动 Logo", "AMITSE", "SetupData" };
            string[] guids = { _settings.UiGuid, _settings.BootGuid, _settings.AmitseGuid, _settings.SetupGuid };
            string[] types = { _settings.UiType, _settings.BootType, _settings.AmitseType, _settings.SetupType };
            TextBox[] guidFields = new TextBox[4], typeFields = new TextBox[4];
            for (int row = 0; row < 4; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new() { Text = labels[row], VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                guidFields[row] = new TextBox { Header = "GUID", Text = guids[row], FontSize = 12 };
                typeFields[row] = new TextBox { Header = "Type", Text = types[row] };
                Grid.SetRow(label, row); Grid.SetRow(guidFields[row], row); Grid.SetRow(typeFields[row], row);
                Grid.SetColumn(guidFields[row], 1); Grid.SetColumn(typeFields[row], 2);
                grid.Children.Add(label); grid.Children.Add(guidFields[row]); grid.Children.Add(typeFields[row]);
            }
            if (await CreateDialog("资源 GUID 与 Section Type", grid, "保存").ShowAsync() != ContentDialogResult.Primary) return;
            _settings = new ResourceSettings
            {
                UiGuid = guidFields[0].Text.Trim(), UiType = typeFields[0].Text.Trim(),
                BootGuid = guidFields[1].Text.Trim(), BootType = typeFields[1].Text.Trim(),
                AmitseGuid = guidFields[2].Text.Trim(), AmitseType = typeFields[2].Text.Trim(),
                SetupGuid = guidFields[3].Text.Trim(), SetupType = typeFields[3].Text.Trim()
            };
        }

        private async void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_busy)
            {
                args.Cancel = true;
                if (await CreateDialog("操作仍在进行", "是否取消当前操作并退出？", "取消操作并退出").ShowAsync() == ContentDialogResult.Primary)
                { _closeWhenIdle = true; _engine?.Cancel(); }
                return;
            }
            _project?.Dispose();
        }

        private static BitmapImage LoadBitmap(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // Replacement files reuse the same path when the user edits an item again.
            return new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(Path.GetFullPath(path)) };
        }

        internal static Windows.UI.Color ToColor(uint value)
        {
            return Windows.UI.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
    }
}
