using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace Ink_Canvas.Windows
{
    public enum SelectionScreenshotMode
    {
        Rectangle,
        Freehand
    }

    public enum SelectionScreenshotAction
    {
        None,
        SaveToDesktop,
        AddToBoard,
        Cancel
    }

    public partial class SelectionScreenshotWindow : Window
    {
        private readonly Bitmap _fullScreenshot;
        private readonly Rectangle _virtualScreenBounds;
        private readonly Func<bool, Bitmap> _screenshotProvider;
        private readonly Action<bool> _hideInkPreviewChanged;
        private SelectionScreenshotMode _mode = SelectionScreenshotMode.Rectangle;
        private bool _isSelecting;
        private Point _startPoint;
        private readonly List<Point> _freehandPoints = new List<Point>();

        public SelectionScreenshotAction ActionResult { get; private set; } = SelectionScreenshotAction.Cancel;
        public Bitmap CapturedBitmap { get; private set; }

        public SelectionScreenshotWindow(Bitmap screenshot, Rectangle virtualScreenBounds, Func<bool, Bitmap> screenshotProvider, Action<bool> hideInkPreviewChanged)
        {
            InitializeComponent();
            _fullScreenshot = screenshot;
            _virtualScreenBounds = virtualScreenBounds;
            _screenshotProvider = screenshotProvider;
            _hideInkPreviewChanged = hideInkPreviewChanged;
            UpdateModeVisualState();
        }


        protected override void OnClosed(EventArgs e)
        {
            _hideInkPreviewChanged?.Invoke(false);
            _fullScreenshot?.Dispose();
            base.OnClosed(e);
        }

        private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BeginSelection(e.GetPosition(RootGrid));
            RootGrid.CaptureMouse();
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting) return;
            UpdateSelection(e.GetPosition(RootGrid));
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndSelection();
            RootGrid.ReleaseMouseCapture();
        }

        private void RootGrid_TouchDown(object sender, TouchEventArgs e)
        {
            BeginSelection(e.GetTouchPoint(RootGrid).Position);
            e.Handled = true;
        }

        private void RootGrid_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_isSelecting) return;
            UpdateSelection(e.GetTouchPoint(RootGrid).Position);
            e.Handled = true;
        }

        private void RootGrid_TouchUp(object sender, TouchEventArgs e)
        {
            EndSelection();
            e.Handled = true;
        }

        private void BeginSelection(Point pos)
        {
            _isSelecting = true;
            _startPoint = pos;

            if (_mode == SelectionScreenshotMode.Rectangle)
            {
                SelectionRect.Visibility = Visibility.Visible;
                SelectionPath.Visibility = Visibility.Collapsed;
                UpdateRectVisual(pos, pos);
            }
            else
            {
                _freehandPoints.Clear();
                _freehandPoints.Add(pos);
                SelectionPath.Visibility = Visibility.Visible;
                SelectionRect.Visibility = Visibility.Collapsed;
                UpdatePathVisual();
            }
        }

        private void UpdateSelection(Point pos)
        {
            if (_mode == SelectionScreenshotMode.Rectangle)
            {
                UpdateRectVisual(_startPoint, pos);
            }
            else if (_freehandPoints.Count == 0 || Distance(_freehandPoints[_freehandPoints.Count - 1], pos) >= 2)
            {
                _freehandPoints.Add(pos);
                UpdatePathVisual();
            }
        }

        private void EndSelection()
        {
            _isSelecting = false;
        }

        private void BtnRectMode_Click(object sender, RoutedEventArgs e)
        {
            _mode = SelectionScreenshotMode.Rectangle;
            UpdateModeVisualState();
        }

        private void BtnFreeMode_Click(object sender, RoutedEventArgs e)
        {
            _mode = SelectionScreenshotMode.Freehand;
            UpdateModeVisualState();
        }

        private void BtnCamera_Click(object sender, RoutedEventArgs e)
        {
            HintTextBlock.Text = "摄像头截图（占位）";
        }

        private void ToggleHideInk_Checked(object sender, RoutedEventArgs e)
        {
            HintTextBlock.Text = "已开启：截图时临时隐藏墨迹";
            _hideInkPreviewChanged?.Invoke(true);
        }

        private void ToggleHideInk_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateHintText();
            _hideInkPreviewChanged?.Invoke(false);
        }

        private void BtnSaveDesktop_Click(object sender, RoutedEventArgs e)
        {
            var bitmap = BuildCaptureBitmap();
            if (bitmap == null)
            {
                HintTextBlock.Text = "请先选择有效区域";
                return;
            }

            CapturedBitmap = bitmap;
            ActionResult = SelectionScreenshotAction.SaveToDesktop;
            DialogResult = true;
            Close();
        }

        private void BtnAddToBoard_Click(object sender, RoutedEventArgs e)
        {
            var bitmap = BuildCaptureBitmap();
            if (bitmap == null)
            {
                HintTextBlock.Text = "请先选择有效区域";
                return;
            }

            CapturedBitmap = bitmap;
            ActionResult = SelectionScreenshotAction.AddToBoard;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ActionResult = SelectionScreenshotAction.Cancel;
            DialogResult = false;
            Close();
        }

        private void UpdateRectVisual(Point p1, Point p2)
        {
            double x = Math.Min(p1.X, p2.X);
            double y = Math.Min(p1.Y, p2.Y);
            double w = Math.Abs(p1.X - p2.X);
            double h = Math.Abs(p1.Y - p2.Y);

            System.Windows.Controls.Canvas.SetLeft(SelectionRect, x);
            System.Windows.Controls.Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void UpdatePathVisual()
        {
            if (_freehandPoints.Count < 2)
            {
                SelectionPath.Data = null;
                return;
            }

            var figure = new PathFigure { StartPoint = _freehandPoints[0], IsClosed = true, IsFilled = true };
            for (int i = 1; i < _freehandPoints.Count; i++)
            {
                figure.Segments.Add(new LineSegment(_freehandPoints[i], true));
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            SelectionPath.Data = geometry;
        }

        private Bitmap BuildCaptureBitmap()
        {
            bool hideInk = ToggleHideInk.IsChecked == true;
            Bitmap screenshot = null;
            try
            {
                if (hideInk)
                {
                    // 重新截图前先临时隐藏选区窗口视觉层，避免半透明遮罩被截入结果
                    double oldOpacity = Opacity;
                    bool oldIsHitTestVisible = IsHitTestVisible;
                    try
                    {
                        Opacity = 0;
                        IsHitTestVisible = false;
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                        screenshot = _screenshotProvider(true);
                    }
                    finally
                    {
                        Opacity = oldOpacity;
                        IsHitTestVisible = oldIsHitTestVisible;
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    }
                }
                else
                {
                    screenshot = (Bitmap)_fullScreenshot.Clone();
                }

                return _mode == SelectionScreenshotMode.Rectangle
                    ? CaptureRectangle(screenshot)
                    : CaptureFreehand(screenshot);
            }
            finally
            {
                screenshot?.Dispose();
            }
        }

        private Bitmap CaptureRectangle(Bitmap screenshot)
        {
            if (SelectionRect.Width < 5 || SelectionRect.Height < 5) return null;

            var left = System.Windows.Controls.Canvas.GetLeft(SelectionRect);
            var top = System.Windows.Controls.Canvas.GetTop(SelectionRect);
            var deviceRect = BuildDeviceRect(new Rect(left, top, SelectionRect.Width, SelectionRect.Height), screenshot);
            if (deviceRect.Width < 5 || deviceRect.Height < 5) return null;

            var result = new Bitmap((int)deviceRect.Width, (int)deviceRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(screenshot,
                    new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                    new System.Drawing.Rectangle((int)deviceRect.X, (int)deviceRect.Y, (int)deviceRect.Width, (int)deviceRect.Height),
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        private Bitmap CaptureFreehand(Bitmap screenshot)
        {
            if (_freehandPoints.Count < 3 || SelectionPath.Data == null) return null;

            var sourcePoints = BuildDevicePoints(_freehandPoints);
            if (sourcePoints.Count < 3) return null;

            var bounds = BuildBounds(sourcePoints, screenshot);
            if (bounds.Width < 5 || bounds.Height < 5) return null;

            var result = new Bitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    var polygon = new List<System.Drawing.PointF>();
                    foreach (var p in sourcePoints)
                    {
                        polygon.Add(new System.Drawing.PointF((float)(p.X - bounds.X), (float)(p.Y - bounds.Y)));
                    }
                    if (polygon.Count < 3) return null;
                    gp.AddPolygon(polygon.ToArray());
                    g.SetClip(gp);
                    g.DrawImage(screenshot,
                        new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                        new System.Drawing.Rectangle((int)bounds.X, (int)bounds.Y, result.Width, result.Height),
                        GraphicsUnit.Pixel);
                }
            }

            return result;
        }

        private Rect BuildDeviceRect(Rect uiRect, Bitmap screenshot)
        {
            var p1 = MapUiPointToScreenshot(new Point(uiRect.Left, uiRect.Top));
            var p2 = MapUiPointToScreenshot(new Point(uiRect.Right, uiRect.Bottom));
            return ClampToBitmap(new Rect(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y), Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y)), screenshot);
        }

        private List<Point> BuildDevicePoints(List<Point> points)
        {
            var result = new List<Point>(points.Count);
            foreach (var point in points)
            {
                result.Add(MapUiPointToScreenshot(point));
            }
            return result;
        }

        private Point MapUiPointToScreenshot(Point uiPoint)
        {
            // 先将窗口内坐标转换为屏幕坐标，再映射到虚拟屏幕位图坐标
            Point screenPoint = PointToScreen(uiPoint);
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                screenPoint = source.CompositionTarget.TransformToDevice.Transform(screenPoint);
            }

            return new Point(screenPoint.X - _virtualScreenBounds.X, screenPoint.Y - _virtualScreenBounds.Y);
        }

        private Rect BuildBounds(List<Point> points, Bitmap screenshot)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (var point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            return ClampToBitmap(new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)), screenshot);
        }

        private Rect ClampToBitmap(Rect rect, Bitmap screenshot)
        {
            double x = Math.Max(0, rect.X);
            double y = Math.Max(0, rect.Y);
            double right = Math.Min(screenshot.Width, rect.X + rect.Width);
            double bottom = Math.Min(screenshot.Height, rect.Y + rect.Height);
            return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        private void UpdateModeVisualState()
        {
            UpdateHintText();
            _hideInkPreviewChanged?.Invoke(false);
            BtnRectMode.Opacity = _mode == SelectionScreenshotMode.Rectangle ? 1 : 0.75;
            BtnFreeMode.Opacity = _mode == SelectionScreenshotMode.Freehand ? 1 : 0.75;

            SelectionRect.Visibility = Visibility.Collapsed;
            SelectionPath.Visibility = Visibility.Collapsed;
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            SelectionPath.Data = null;
            _freehandPoints.Clear();
        }

        private void UpdateHintText()
        {
            HintTextBlock.Text = _mode == SelectionScreenshotMode.Rectangle
                ? "拖拽进行矩形选区"
                : "拖动绘制自由选区";
        }

        private static double Distance(Point p1, Point p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
