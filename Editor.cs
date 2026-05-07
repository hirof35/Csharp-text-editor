using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Editor
{
    public class EditorFile : System.ComponentModel.INotifyPropertyChanged
    {
        public string FileName { get; set; } = "new file.txt";
        public string FilePath { get; set; }
        public List<string> Content { get; set; } = new List<string> { "" };

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private System.Collections.ObjectModel.ObservableCollection<EditorFile> _openFiles;

        public MainWindow()
        {
            InitializeComponent();
            _openFiles = new System.Collections.ObjectModel.ObservableCollection<EditorFile>();
            FileTabs.ItemsSource = _openFiles;
            AddNewTab();
        }

        private void NewTab_Click(object sender, RoutedEventArgs e) => AddNewTab();

        private void AddNewTab(string path = null)
        {
            var file = new EditorFile();
            if (path != null)
            {
                file.FilePath = path;
                file.FileName = System.IO.Path.GetFileName(path);
                file.Content = System.IO.File.ReadAllLines(path).ToList();
            }
            _openFiles.Add(file);
            FileTabs.SelectedIndex = _openFiles.Count - 1;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                AddNewTab(dialog.FileName);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (FileTabs.SelectedItem is EditorFile currentFile)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog();
                dialog.FileName = currentFile.FileName;

                if (currentFile.FilePath != null || dialog.ShowDialog() == true)
                {
                    string path = currentFile.FilePath ?? dialog.FileName;
                    System.IO.File.WriteAllLines(path, currentFile.Content);
                    currentFile.FilePath = path;
                    currentFile.FileName = System.IO.Path.GetFileName(path);
                }
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is EditorFile fileToDelete)
            {
                _openFiles.Remove(fileToDelete);
                if (_openFiles.Count == 0) AddNewTab();
            }
        }
    }

    public class CustomEditor : FrameworkElement
    {
        // --- 状態管理 ---
        private int _caretLine = 0;
        private int _caretColumn = 0;
        private double _verticalOffset = 0;
        private bool _isCaretVisible = true;
        private DispatcherTimer _caretTimer;

        // --- デザイン設定 ---
        private readonly double _lineHeight = 22;
        private readonly double _fontSize = 14;
        private readonly Typeface _typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private double _charWidth;

        // --- カラー設定 ---
        private readonly Brush _bgBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        private readonly Brush _fgBrush = new SolidColorBrush(Color.FromRgb(212, 212, 212));
        private readonly Brush _lineNumBrush = Brushes.Gray;
        private readonly Brush _caretPenBrush = Brushes.White;

        public static readonly DependencyProperty TextLinesProperty =
            DependencyProperty.Register("TextLines", typeof(List<string>), typeof(CustomEditor),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public List<string> TextLines
        {
            get => (List<string>)GetValue(TextLinesProperty);
            set => SetValue(TextLinesProperty, value);
        }

        public CustomEditor()
        {
            Focusable = true;
            Cursor = Cursors.IBeam;

            // 文字レンダリングの設定（ピクセルスナップを強制）
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            // キャレットタイマー初期化
            _caretTimer = new DispatcherTimer();
            _caretTimer.Interval = TimeSpan.FromMilliseconds(500);
            _caretTimer.Tick += (s, e) => { _isCaretVisible = !_isCaretVisible; InvalidateVisual(); };
            _caretTimer.Start();

            // 1文字の基準幅を計測
            var ft = new FormattedText("A", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black, null, TextFormattingMode.Display, 96.0 / 72.0);
            _charWidth = ft.WidthIncludingTrailingWhitespace;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var lines = TextLines;
            if (lines == null || lines.Count == 0) return;

            dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

            double lineNumAreaWidth = (lines.Count.ToString().Length * _charWidth) + 20;

            int startLine = (int)(_verticalOffset / _lineHeight);
            int visibleLinesCount = (int)(ActualHeight / _lineHeight) + 1;
            int endLine = Math.Min(lines.Count - 1, startLine + visibleLinesCount);

            for (int i = startLine; i <= endLine; i++)
            {
                double y = (i * _lineHeight) - _verticalOffset;
                string lineText = lines[i];
                double currentX = lineNumAreaWidth;

                // 行番号の描画
                DrawText(dc, (i + 1).ToString(), 5, y, _lineNumBrush);

                // 本文の描画（1文字ずつ絶対座標で配置）
                foreach (char c in lineText)
                {
                    if (c == '\t')
                    {
                        DrawSingleChar(dc, '→', currentX, y, Brushes.DimGray); // タブは暗い色で描画
                        currentX += (_charWidth * 4);
                    }
                    else
                    {
                        DrawSingleChar(dc, c, currentX, y, _fgBrush);
                        currentX += (c <= 127) ? _charWidth : (_charWidth * 2);
                    }
                }
            }

            // キャレット描画
            if (_isCaretVisible && IsFocused)
            {
                double cx = GetCaretX(_caretLine, _caretColumn);
                double cy = (_caretLine * _lineHeight) - _verticalOffset;

                if (cy >= 0 && cy < ActualHeight)
                {
                    dc.DrawLine(new Pen(_caretPenBrush, 2), new Point(cx, cy), new Point(cx, cy + _lineHeight));
                }
            }
        }

        private void DrawSingleChar(DrawingContext dc, char c, double x, double y, Brush brush)
        {
            var ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush, null, TextFormattingMode.Display, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x, y));
        }

        private void DrawText(DrawingContext dc, string text, double x, double y, Brush brush)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush, null, TextFormattingMode.Display, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x, y));
        }

        private double GetCaretX(int lineIndex, int charIndex)
        {
            double x = (TextLines.Count.ToString().Length * _charWidth) + 20;
            if (lineIndex < 0 || lineIndex >= TextLines.Count) return x;

            string text = TextLines[lineIndex];
            for (int i = 0; i < charIndex && i < text.Length; i++)
            {
                if (text[i] == '\t') x += (_charWidth * 4);
                else x += (text[i] <= 127) ? _charWidth : (_charWidth * 2);
            }
            return x;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var lines = TextLines;
            if (lines == null) return;

            switch (e.Key)
            {
                case Key.Left: MoveLeft(); e.Handled = true; break;
                case Key.Right: MoveRight(); e.Handled = true; break;
                case Key.Up: if (_caretLine > 0) _caretLine--; e.Handled = true; break;
                case Key.Down: if (_caretLine < lines.Count - 1) _caretLine++; e.Handled = true; break;
                case Key.Back: HandleBack(); e.Handled = true; break;
                case Key.Enter: HandleEnter(); e.Handled = true; break;
                case Key.Tab:
                    lines[_caretLine] = lines[_caretLine].Insert(_caretColumn, "\t");
                    _caretColumn++;
                    e.Handled = true;
                    break;
            }
            _caretColumn = Math.Clamp(_caretColumn, 0, lines[_caretLine].Length);
            ResetCaret();
        }

        private void MoveLeft()
        {
            if (_caretColumn > 0) _caretColumn--;
            else if (_caretLine > 0) { _caretLine--; _caretColumn = TextLines[_caretLine].Length; }
        }

        private void MoveRight()
        {
            if (_caretColumn < TextLines[_caretLine].Length) _caretColumn++;
            else if (_caretLine < TextLines.Count - 1) { _caretLine++; _caretColumn = 0; }
        }

        private void HandleBack()
        {
            if (_caretColumn > 0)
            {
                TextLines[_caretLine] = TextLines[_caretLine].Remove(_caretColumn - 1, 1);
                _caretColumn--;
            }
            else if (_caretLine > 0)
            {
                int oldLen = TextLines[_caretLine - 1].Length;
                TextLines[_caretLine - 1] += TextLines[_caretLine];
                TextLines.RemoveAt(_caretLine);
                _caretLine--;
                _caretColumn = oldLen;
            }
        }

        private void HandleEnter()
        {
            string tail = TextLines[_caretLine].Substring(_caretColumn);
            TextLines[_caretLine] = TextLines[_caretLine].Substring(0, _caretColumn);
            TextLines.Insert(_caretLine + 1, tail);
            _caretLine++;
            _caretColumn = 0;
        }

        protected override void OnTextInput(TextCompositionEventArgs e)
        {
            TextLines[_caretLine] = TextLines[_caretLine].Insert(_caretColumn, e.Text);
            _caretColumn += e.Text.Length;
            ResetCaret();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            var lines = TextLines;
            if (lines == null || lines.Count == 0) return;
            Focus();
            var pos = e.GetPosition(this);
            _caretLine = Math.Clamp((int)((pos.Y + _verticalOffset) / _lineHeight), 0, lines.Count - 1);

            double lineNumAreaWidth = (lines.Count.ToString().Length * _charWidth) + 20;
            double curX = lineNumAreaWidth;
            int col = 0;
            foreach (char c in lines[_caretLine])
            {
                double w = (c == '\t') ? (_charWidth * 4) : (c <= 127) ? _charWidth : (_charWidth * 2);
                if (pos.X < curX + (w / 2)) break;
                curX += w;
                col++;
            }
            _caretColumn = col;
            ResetCaret();
        }

        private void ResetCaret()
        {
            _isCaretVisible = true;
            _caretTimer.Stop();
            _caretTimer.Start();
            EnsureCaretVisible();
            InvalidateVisual();
        }

        private void EnsureCaretVisible()
        {
            double caretY = _caretLine * _lineHeight;
            if (caretY + _lineHeight > _verticalOffset + ActualHeight) _verticalOffset = caretY + _lineHeight - ActualHeight;
            else if (caretY < _verticalOffset) _verticalOffset = caretY;
        }
    }
}
