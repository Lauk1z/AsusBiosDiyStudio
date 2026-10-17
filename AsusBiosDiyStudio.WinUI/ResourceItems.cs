using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AsusBiosDiyStudio.WinUI
{
    public sealed class ImageResourceItem : INotifyPropertyChanged
    {
        internal readonly UiImageEntry Entry;
        private bool _modified;
        public int Index { get { return Entry.Index; } }
        public string Caption { get { return "#" + Entry.Index; } }
        public string Dimensions { get { return Entry.Width + "×" + Entry.Height; } }
        public BitmapImage Thumbnail { get; }
        public string StateText { get { return _modified ? "已修改" : ""; } }
        public bool IsModified
        {
            get { return _modified; }
            set { _modified = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        internal ImageResourceItem(UiImageEntry entry)
        {
            Entry = entry;
            Thumbnail = new BitmapImage(new Uri(Path.GetFullPath(entry.FilePath))) { DecodePixelWidth = 224 };
        }
    }

    public sealed class ColorRecordItem : INotifyPropertyChanged
    {
        internal readonly ColorEntry Entry;
        public SolidColorBrush SourceBrush { get; }
        public string SourceText { get { return Entry.RgbText; } }
        public string AlphaText { get { return Entry.Alpha.ToString(); } }
        public string CountText { get { return Entry.Count.ToString(); } }
        public string Properties { get { return Entry.Properties; } }
        public string ReplacementText { get { return Entry.Replacement.HasValue ? "#" + (Entry.Replacement.Value & 0xFFFFFF).ToString("X6") : "未修改"; } }
        public event PropertyChangedEventHandler PropertyChanged;

        internal ColorRecordItem(ColorEntry entry)
        {
            Entry = entry; SourceBrush = new SolidColorBrush(MainWindow.ToColor(entry.Value));
        }

        internal void Refresh() { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReplacementText))); }
    }
}
