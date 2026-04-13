using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
        private SelectionScreenshotMode _mode = SelectionScreenshotMode.Rectangle;
        private bool _isSelecting;
        private Point _startPoint;
        private readonly List<Point> _freehandPoints = new List<Point>();

        public SelectionScreenshotAction ActionResult { get; private set; } = SelectionScreenshotAction.Cancel;
        public Bitmap CapturedBitmap { get; private set; }

        public SelectionScreenshotWindow(Bitmap screenshot)
        {
            InitializeComponent();
            _fullScreenshot = screenshot;
            UpdateModeVisualState();
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
            else
            {
                if (_freehandPoints.Count == 0 || Distance(_freehandPoints[_freehandPoints.Count - 1], pos) >= 2)
                {
                    _freehandPoints.Add(pos);
                    UpdatePathVisual();
                }
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
            HintTextBlock.Text = "隐藏墨迹（占位）";
        }

        private void ToggleHideInk_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateHintText();
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

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
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
            if (_mode == SelectionScreenshotMode.Rectangle)
            {
                return CaptureRectangle();
            }

            return CaptureFreehand();
        }

        private Bitmap CaptureRectangle()
        {
            if (SelectionRect.Width < 5 || SelectionRect.Height < 5) return null;

            var rect = new Rect(Canvas.GetLeft(SelectionRect), Canvas.GetTop(SelectionRect), SelectionRect.Width, SelectionRect.Height);
            rect = ClampRectToBitmap(rect);
            if (rect.Width < 5 || rect.Height < 5) return null;

            var result = new Bitmap((int)rect.Width, (int)rect.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(_fullScreenshot,
                    new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                    new System.Drawing.Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height),
                    GraphicsUnit.Pixel);
            }
            return result;
        }

        private Bitmap CaptureFreehand()
        {
            if (_freehandPoints.Count < 3 || SelectionPath.Data == null) return null;

            Rect bounds = SelectionPath.Data.Bounds;
            bounds = ClampRectToBitmap(bounds);
            if (bounds.Width < 5 || bounds.Height < 5) return null;

            var result = new Bitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(System.Drawing.Color.Transparent);
                using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    var points = new List<System.Drawing.PointF>();
                    foreach (var p in _freehandPoints)
                    {
                        points.Add(new System.Drawing.PointF((float)(p.X - bounds.X), (float)(p.Y - bounds.Y)));
                    }
                    if (points.Count < 3) return null;
                    gp.AddPolygon(points.ToArray());
                    g.SetClip(gp);
                    g.DrawImage(_fullScreenshot,
                        new System.Drawing.Rectangle(0, 0, result.Width, result.Height),
                        new System.Drawing.Rectangle((int)bounds.X, (int)bounds.Y, result.Width, result.Height),
                        GraphicsUnit.Pixel);
                }
            }

            return result;
        }

        private Rect ClampRectToBitmap(Rect rect)
        {
            double x = Math.Max(0, rect.X);
            double y = Math.Max(0, rect.Y);
            double right = Math.Min(_fullScreenshot.Width, rect.X + rect.Width);
            double bottom = Math.Min(_fullScreenshot.Height, rect.Y + rect.Height);

            return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        private void UpdateModeVisualState()
        {
            UpdateHintText();

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
