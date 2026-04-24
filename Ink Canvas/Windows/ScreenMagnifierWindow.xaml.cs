using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        private double _resizeStartLeft;

        private uint _selfOriginalAffinity = WdaNone;
        private uint _mainOriginalAffinity = WdaNone;
        private bool _hasSelfOriginalAffinity;
        private bool _hasMainOriginalAffinity;

        private bool _useLegacySnapshotMode;
        private Bitmap _legacyScreenBitmap;
        private Rect _virtualScreenBounds;
        private Window _legacyOverlayWindow;
        private IntPtr _magnifierWindowHandle = IntPtr.Zero;

        public event EventHandler RequestClose;

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

        private static bool IsWindows7()
        {
            Version osVersion = Environment.OSVersion.Version;
            return osVersion.Major == 6 && osVersion.Minor == 1;
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
            uint targetAffinity = isMainWindow ? WdaExcludeFromCapture : WdaMonitor;

            bool applied = TrySetWindowAffinity(windowHandle, targetAffinity);
            if (!applied)
            {
                applied = TrySetWindowAffinity(windowHandle, WdaMonitor);
            }

            if (isMainWindow && !applied && IsWindows7())
            {
                _useLegacySnapshotMode = true;
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

            SourceInitialized += ScreenMagnifierWindow_SourceInitialized;
            Loaded += ScreenMagnifierWindow_Loaded;
            Closing += ScreenMagnifierWindow_Closing;
            Closed += ScreenMagnifierWindow_Closed;
        }

        private void RefreshZoomLabel()
        {
            if (TxtZoom == null || ZoomSlider == null) return;
            TxtZoom.Text = $"{ZoomSlider.Value:F1}x";
        }

        private void ScreenMagnifierWindow_SourceInitialized(object sender, EventArgs e)
        {
            if (IsWindows7())
            {
                _useLegacySnapshotMode = true;
            }

            _magnifierWindowHandle = new WindowInteropHelper(this).Handle;
            ApplyCaptureExclusion(_magnifierWindowHandle, false);
            ApplyCaptureExclusion(_mainWindowHandle, true);

            if (_useLegacySnapshotMode)
            {
                PrepareLegacySnapshotMode();
            }
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

            RefreshZoomLabel();
            if (_useLegacySnapshotMode)
            {
                ShowLegacyOverlayWindow();
            }
            Topmost = true;
            _captureTimer.Start();
        }

        private void ScreenMagnifierWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ClearCaptureExclusion(_magnifierWindowHandle, false);
            ClearCaptureExclusion(_mainWindowHandle, true);
        }

        private void ScreenMagnifierWindow_Closed(object sender, EventArgs e)
        {
            _captureTimer.Stop();

            CloseLegacyOverlayWindow();
            _legacyScreenBitmap?.Dispose();
            _legacyScreenBitmap = null;
            _magnifierWindowHandle = IntPtr.Zero;

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void PrepareLegacySnapshotMode()
        {
            var virtualScreen = WinForms.SystemInformation.VirtualScreen;
            _virtualScreenBounds = new Rect(virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height);
            _legacyScreenBitmap?.Dispose();
            _legacyScreenBitmap = CaptureMagnifierSourceBitmap(virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height);
        }

        private void ShowLegacyOverlayWindow()
        {
            if (_legacyScreenBitmap == null || _legacyOverlayWindow != null) return;

            IntPtr hBitmap = _legacyScreenBitmap.GetHbitmap();
            BitmapSource source;
            try
            {
                source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
            }
            finally
            {
                DeleteObject(hBitmap);
            }

            var image = new System.Windows.Controls.Image
            {
                Source = source,
                Stretch = Stretch.Fill
            };

            var text = new TextBlock
            {
                Text = "放大镜模式",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 255, 255, 255)),
                FontSize = 42,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0)
            };

            var grid = new Grid();
            grid.Children.Add(image);
            grid.Children.Add(text);

            _legacyOverlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = false,
                AllowsTransparency = false,
                Content = grid,
                Background = System.Windows.Media.Brushes.Black,
                IsHitTestVisible = false
            };

            PresentationSource sourceVisual = PresentationSource.FromVisual(this);
            if (sourceVisual?.CompositionTarget != null)
            {
                Rect dipBounds = Rect.Transform(_virtualScreenBounds, sourceVisual.CompositionTarget.TransformFromDevice);
                _legacyOverlayWindow.Left = dipBounds.Left;
                _legacyOverlayWindow.Top = dipBounds.Top;
                _legacyOverlayWindow.Width = dipBounds.Width;
                _legacyOverlayWindow.Height = dipBounds.Height;
            }
            else
            {
                _legacyOverlayWindow.Left = _virtualScreenBounds.Left;
                _legacyOverlayWindow.Top = _virtualScreenBounds.Top;
                _legacyOverlayWindow.Width = _virtualScreenBounds.Width;
                _legacyOverlayWindow.Height = _virtualScreenBounds.Height;
            }

            _legacyOverlayWindow.Show();
            Activate();
        }

        private void CloseLegacyOverlayWindow()
        {
            if (_legacyOverlayWindow == null) return;
            _legacyOverlayWindow.Close();
            _legacyOverlayWindow = null;
        }

        private Bitmap CaptureMagnifierSourceBitmap(int srcX, int srcY, int srcW, int srcH)
        {
            var bitmap = new Bitmap(srcW, srcH, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr dstHdc = g.GetHdc();
                IntPtr srcHdc = GetDC(IntPtr.Zero);
                if (srcHdc == IntPtr.Zero)
                {
                    g.ReleaseHdc(dstHdc);
                    return bitmap;
                }
                try
                {
                    _ = BitBlt(dstHdc, 0, 0, srcW, srcH, srcHdc, srcX, srcY, SRCCOPY);
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, srcHdc);
                    g.ReleaseHdc(dstHdc);
                }
            }

            return bitmap;
        }

        private Bitmap CaptureFromLegacySnapshot(int srcX, int srcY, int srcW, int srcH)
        {
            if (_legacyScreenBitmap == null)
            {
                return CaptureMagnifierSourceBitmap(srcX, srcY, srcW, srcH);
            }

            int relativeX = srcX - (int)_virtualScreenBounds.Left;
            int relativeY = srcY - (int)_virtualScreenBounds.Top;
            int destX = 0;
            int destY = 0;
            int width = srcW;
            int height = srcH;

            if (relativeX < 0)
            {
                destX = -relativeX;
                width -= destX;
                relativeX = 0;
            }
            if (relativeY < 0)
            {
                destY = -relativeY;
                height -= destY;
                relativeY = 0;
            }

            width = Math.Min(width, _legacyScreenBitmap.Width - relativeX);
            height = Math.Min(height, _legacyScreenBitmap.Height - relativeY);
            width = Math.Max(0, width);
            height = Math.Max(0, height);

            var target = new Bitmap(Math.Max(1, srcW), Math.Max(1, srcH), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.Clear(System.Drawing.Color.Black);
                if (width > 0 && height > 0)
                {
                    g.DrawImage(_legacyScreenBitmap,
                        new Rectangle(destX, destY, width, height),
                        new Rectangle(relativeX, relativeY, width, height),
                        GraphicsUnit.Pixel);
                }
            }

            return target;
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

            using (var bitmap = _useLegacySnapshotMode ? CaptureFromLegacySnapshot(srcX, srcY, srcW, srcH) : CaptureMagnifierSourceBitmap(srcX, srcY, srcW, srcH))
            {
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
                _resizeStartLeft = Left;
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
                Left = _resizeStartLeft + (_resizeStartWidth - newWidth);
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

        private const int SRCCOPY = 0x00CC0020;

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
