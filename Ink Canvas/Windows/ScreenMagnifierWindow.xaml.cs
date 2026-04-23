using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ink_Canvas.Windows
{
    public partial class ScreenMagnifierWindow : Window
    {
        private const uint WdaNone = 0x00000000;
        private const uint WdaMonitor = 0x00000001;
        private const uint WdaExcludeFromCapture = 0x00000011;

        private readonly IntPtr _mainWindowHandle;
        private readonly DispatcherTimer _captureTimer;

        private bool _isResizing;
        private bool _isLeftHandle;
        private System.Windows.Point _resizeStartPoint;
        private double _resizeStartWidth;

        private uint _selfOriginalAffinity = WdaNone;
        private uint _mainOriginalAffinity = WdaNone;
        private bool _hasSelfOriginalAffinity;
        private bool _hasMainOriginalAffinity;

        public event EventHandler RequestClose;

        private static bool IsExcludeFromCaptureSupported()
        {
            Version osVersion = Environment.OSVersion.Version;
            return osVersion.Major >= 10 && (osVersion.Build >= 19041 || osVersion.Major > 10);
        }

        private static bool TrySetWindowAffinity(IntPtr windowHandle, uint affinity)
        {
            if (windowHandle == IntPtr.Zero) return false;

            try
            {
                return SetWindowDisplayAffinity(windowHandle, affinity);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetWindowAffinity(IntPtr windowHandle, out uint affinity)
        {
            affinity = WdaNone;
            if (windowHandle == IntPtr.Zero) return false;

            try
            {
                return GetWindowDisplayAffinity(windowHandle, out affinity);
            }
            catch
            {
                return false;
            }
        }

        private void ApplyCaptureExclusion(IntPtr windowHandle, bool isMainWindow)
        {
            if (windowHandle == IntPtr.Zero) return;

            if (TryGetWindowAffinity(windowHandle, out uint originalAffinity))
            {
                if (isMainWindow)
                {
                    _mainOriginalAffinity = originalAffinity;
                    _hasMainOriginalAffinity = true;
                }
                else
                {
                    _selfOriginalAffinity = originalAffinity;
                    _hasSelfOriginalAffinity = true;
                }
            }

            // magnifier 自身优先使用 WDA_MONITOR，确保被任何桌面捕获路径排除，避免递归放大。
            uint targetAffinity = isMainWindow
                ? (IsExcludeFromCaptureSupported() ? WdaExcludeFromCapture : WdaMonitor)
                : WdaMonitor;

            if (!TrySetWindowAffinity(windowHandle, targetAffinity))
            {
                _ = TrySetWindowAffinity(windowHandle, WdaMonitor);
            }
        }

        private void ClearCaptureExclusion(IntPtr windowHandle, bool isMainWindow)
        {
            if (windowHandle == IntPtr.Zero) return;

            uint affinityToRestore = WdaNone;
            if (isMainWindow && _hasMainOriginalAffinity)
            {
                affinityToRestore = _mainOriginalAffinity;
            }
            else if (!isMainWindow && _hasSelfOriginalAffinity)
            {
                affinityToRestore = _selfOriginalAffinity;
            }

            _ = TrySetWindowAffinity(windowHandle, affinityToRestore);
        }

        public ScreenMagnifierWindow(IntPtr mainWindowHandle)
        {
            _mainWindowHandle = mainWindowHandle;
            InitializeComponent();

            _captureTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _captureTimer.Tick += (_, __) => RenderMagnifiedFrame();

            Loaded += ScreenMagnifierWindow_Loaded;
            Closed += ScreenMagnifierWindow_Closed;
        }

        private void RefreshZoomLabel()
        {
            if (TxtZoom == null || ZoomSlider == null) return;
            TxtZoom.Text = $"{ZoomSlider.Value:F1}x";
        }

        private void ScreenMagnifierWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
                var cursor = WinForms.Cursor.Position;
                WinForms.Screen screen = WinForms.Screen.FromPoint(cursor);
                Rect workAreaPx = new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
                Rect workAreaDip = Rect.Transform(workAreaPx, fromDevice);

                Left = workAreaDip.Left + (workAreaDip.Width - Width) / 2;
                Top = workAreaDip.Top + (workAreaDip.Height - Height) / 2;
            }
            else
            {
                Left = (SystemParameters.WorkArea.Width - Width) / 2;
                Top = (SystemParameters.WorkArea.Height - Height) / 2;
            }

            IntPtr magnifierHandle = new WindowInteropHelper(this).Handle;
            ApplyCaptureExclusion(magnifierHandle, false);
            ApplyCaptureExclusion(_mainWindowHandle, true);

            RefreshZoomLabel();
            _captureTimer.Start();
        }

        private void ScreenMagnifierWindow_Closed(object sender, EventArgs e)
        {
            _captureTimer.Stop();

            IntPtr magnifierHandle = new WindowInteropHelper(this).Handle;
            ClearCaptureExclusion(magnifierHandle, false);
            ClearCaptureExclusion(_mainWindowHandle, true);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void RenderMagnifiedFrame()
        {
            if (!IsLoaded || ImageViewport == null || ZoomSlider == null || ActualWidth < 40 || ActualHeight < 60) return;

            PresentationSource source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            Matrix toDevice = source.CompositionTarget.TransformToDevice;

            double zoom = ZoomSlider.Value;
            const double barHeightDip = 42;

            double captureWidthDip = ActualWidth / zoom;
            double captureHeightDip = (ActualHeight - barHeightDip) / zoom;
            if (captureWidthDip < 1 || captureHeightDip < 1) return;

            double centerXDip = Left + ActualWidth / 2;
            double centerYDip = Top + (ActualHeight - barHeightDip) / 2;

            System.Windows.Point topLeftDev = toDevice.Transform(
                new System.Windows.Point(centerXDip - captureWidthDip / 2, centerYDip - captureHeightDip / 2));
            Vector sizeDev = toDevice.Transform(new Vector(captureWidthDip, captureHeightDip));

            int srcX = (int)Math.Round(topLeftDev.X);
            int srcY = (int)Math.Round(topLeftDev.Y);
            int srcW = Math.Max(1, (int)Math.Round(sizeDev.X));
            int srcH = Math.Max(1, (int)Math.Round(sizeDev.Y));

            using (var bitmap = new Bitmap(srcW, srcH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(srcX, srcY, 0, 0, new System.Drawing.Size(srcW, srcH), CopyPixelOperation.SourceCopy);
                }

                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();
                    ImageViewport.Source = bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, Math.Round((ZoomSlider.Value - 0.5) * 10) / 10);
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, Math.Round((ZoomSlider.Value + 0.5) * 10) / 10);
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtZoom == null || ZoomSlider == null) return;

            RefreshZoomLabel();
            RenderMagnifiedFrame();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _isResizing = true;
                _isLeftHandle = string.Equals(element.Tag as string, "Left", StringComparison.OrdinalIgnoreCase);
                _resizeStartPoint = PointToScreen(e.GetPosition(this));
                _resizeStartWidth = Width;
                element.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ResizeHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isResizing) return;

            System.Windows.Point nowPoint = PointToScreen(e.GetPosition(this));
            double delta = nowPoint.X - _resizeStartPoint.X;
            if (_isLeftHandle)
            {
                delta = -delta;
            }

            double maxWidth = Math.Max(260, SystemParameters.WorkArea.Width * 0.95);
            double newWidth = Math.Max(260, Math.Min(maxWidth, _resizeStartWidth + delta));

            double maxHeight = Math.Max(180, SystemParameters.WorkArea.Height * 0.8);
            double newHeight = Math.Min(maxHeight, Math.Max(180, newWidth * 0.66));

            Width = newWidth;
            Height = newHeight;

            if (_isLeftHandle)
            {
                Left = Left + (_resizeStartWidth - newWidth);
            }
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _isResizing = false;
                element.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
