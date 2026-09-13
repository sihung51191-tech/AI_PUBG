using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aimmy2.Class; // For Dictionary access if needed, or simply pass callback
using Rectangle = System.Windows.Shapes.Rectangle;
using Point = System.Windows.Point; // Explicit to avoid confusion with System.Drawing.Point
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Visuality
{
    public partial class RegionSelectorWindow : Window
    {
        private Point _startPoint;
        private bool _isDragging = false;
        private readonly System.Windows.Forms.Screen _screen;
        private double _scaleX = 1, _scaleY = 1;
        public System.Drawing.Rectangle SelectedRegion { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public RegionSelectorWindow(string promptText = "Select Region", System.Windows.Forms.Screen? screen = null)
        {
            InitializeComponent();
            _screen = screen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, DisplayManager.CurrentDisplay?.DeviceName, StringComparison.OrdinalIgnoreCase))
                ?? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            Loaded += (_, _) =>
            {
                var dpi = VisualTreeHelper.GetDpi(this); _scaleX = dpi.DpiScaleX; _scaleY = dpi.DpiScaleY;
                Left = _screen.Bounds.Left / _scaleX; Top = _screen.Bounds.Top / _scaleY;
                Width = _screen.Bounds.Width / _scaleX; Height = _screen.Bounds.Height / _scaleY;
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SetWindowPos(handle, new IntPtr(-1), _screen.Bounds.Left, _screen.Bounds.Top, _screen.Bounds.Width, _screen.Bounds.Height, 0x0010);
                global::Other.UiLanguage.RefreshTree(this);
            };
            TitleText.Text = promptText;
            this.KeyDown += RegionSelectorWindow_KeyDown;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        private void RegionSelectorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsConfirmed = false;
                this.Close();
            }
        }

        private void SelectionCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ConfirmPanel.Visibility == Visibility.Visible) return;

            _startPoint = e.GetPosition(SelectionCanvas);
            _isDragging = true;
            
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            SelectionRect.Visibility = Visibility.Visible;
        }

        private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var currentPoint = e.GetPosition(SelectionCanvas);
            
            var x = Math.Min(currentPoint.X, _startPoint.X);
            var y = Math.Min(currentPoint.Y, _startPoint.Y);
            
            var width = Math.Abs(currentPoint.X - _startPoint.X);
            var height = Math.Abs(currentPoint.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = width;
            SelectionRect.Height = height;

            InfoText.Text = $"Selecting: X={(int)x}, Y={(int)y}, W={(int)width}, H={(int)height}";
        }

        private async void SelectionCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;

            // Show confirm panel near the selection
            double panelLeft = Canvas.GetLeft(SelectionRect);
            double panelTop = Canvas.GetTop(SelectionRect) + SelectionRect.Height + 10;
            
            // Adjust if off screen
            if (panelLeft + ConfirmPanel.Width > this.ActualWidth)
                panelLeft = this.ActualWidth - ConfirmPanel.Width - 10;
            
            if (panelTop + ConfirmPanel.Height > this.ActualHeight)
                panelTop = Canvas.GetTop(SelectionRect) - ConfirmPanel.Height - 10;

            Canvas.SetLeft(ConfirmPanel, panelLeft);
            Canvas.SetTop(ConfirmPanel, panelTop);
            ConfirmPanel.Visibility = Visibility.Visible;
            int px = _screen.Bounds.Left + (int)Math.Round(Canvas.GetLeft(SelectionRect) * _scaleX);
            int py = _screen.Bounds.Top + (int)Math.Round(Canvas.GetTop(SelectionRect) * _scaleY);
            int pw = Math.Max(1, (int)Math.Round(SelectionRect.Width * _scaleX));
            int ph = Math.Max(1, (int)Math.Round(SelectionRect.Height * _scaleY));
            CoordinateText.Text = $"X={px}, Y={py}, Width={pw}, Height={ph}  •  {_screen.DeviceName}";
            try
            {
                Opacity = 0;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                using var bitmap = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(px, py, 0, 0, bitmap.Size);
                using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); stream.Position = 0;
                var preview = new BitmapImage(); preview.BeginInit(); preview.CacheOption = BitmapCacheOption.OnLoad; preview.StreamSource = stream; preview.EndInit(); preview.Freeze();
                PreviewImage.Source = preview;
            }
            finally { Opacity = 1; Activate(); }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            int x = _screen.Bounds.Left + (int)Math.Round(Canvas.GetLeft(SelectionRect) * _scaleX);
            int y = _screen.Bounds.Top + (int)Math.Round(Canvas.GetTop(SelectionRect) * _scaleY);
            int w = (int)Math.Round(SelectionRect.Width * _scaleX);
            int h = (int)Math.Round(SelectionRect.Height * _scaleY);

            SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
            IsConfirmed = true;
            this.Close();
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            ConfirmPanel.Visibility = Visibility.Collapsed;
            SelectionRect.Visibility = Visibility.Collapsed;
            InfoText.Text = "Click and drag to select a region. Press ESC to cancel.";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            this.Close();
        }
    }
}
