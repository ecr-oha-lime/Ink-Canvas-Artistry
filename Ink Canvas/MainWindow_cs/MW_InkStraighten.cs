using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private bool isInkStraightenTracking = false;
        private bool isInkStraightenActivated = false;
        private bool isInkStraightenLowSpeed = false;
        private Point inkStraightenStartPoint;
        private Point inkStraightenCurrentPoint;
        private Point inkStraightenLowSpeedAnchorPoint;
        private DateTime inkStraightenLowSpeedStartAt = DateTime.MinValue;
        private DateTime inkStraightenLastSampleAt = DateTime.MinValue;
        private Line inkStraightenPreviewLine;

        private bool isInkStraightenPendingApply = false;
        private Point inkStraightenPendingStartPoint;
        private Point inkStraightenPendingEndPoint;

        private bool IsInkStraightenAvailable()
        {
            return Settings?.InkToShape != null
                   && Settings.InkToShape.IsInkStraightenEnabled
                   && currentMode == 0
                   && drawingShapeMode == 0
                   && inkCanvas.EditingMode == InkCanvasEditingMode.Ink;
        }

        private void BeginInkStraightenTracking(Point point)
        {
            ResetInkStraightenTracking();
            if (!IsInkStraightenAvailable()) return;

            isInkStraightenTracking = true;
            inkStraightenStartPoint = point;
            inkStraightenCurrentPoint = point;
            inkStraightenLastSampleAt = DateTime.UtcNow;
        }

        private void UpdateInkStraightenTracking(Point point)
        {
            if (!isInkStraightenTracking) return;

            var now = DateTime.UtcNow;
            var deltaMs = (now - inkStraightenLastSampleAt).TotalMilliseconds;
            if (deltaMs <= 0) return;

            var distance = GetDistance(inkStraightenCurrentPoint, point);
            var speed = distance / deltaMs;

            inkStraightenCurrentPoint = point;
            inkStraightenLastSampleAt = now;

            if (isInkStraightenActivated)
            {
                UpdateInkStraightenPreviewLine(inkStraightenStartPoint, inkStraightenCurrentPoint);
                return;
            }

            var speedThreshold = Math.Max(0.0001, Settings.InkToShape.InkStraightenSpeedThreshold);
            var displacementThreshold = Math.Max(0.1, Settings.InkToShape.InkStraightenDisplacementThreshold);
            var holdDurationMs = Math.Max(100, Settings.InkToShape.InkStraightenHoldDurationMs);

            if (speed < speedThreshold)
            {
                if (!isInkStraightenLowSpeed)
                {
                    isInkStraightenLowSpeed = true;
                    inkStraightenLowSpeedAnchorPoint = point;
                    inkStraightenLowSpeedStartAt = now;
                }

                if (GetDistance(inkStraightenLowSpeedAnchorPoint, point) <= displacementThreshold
                    && (now - inkStraightenLowSpeedStartAt).TotalMilliseconds >= holdDurationMs)
                {
                    isInkStraightenActivated = true;
                    UpdateInkStraightenPreviewLine(inkStraightenStartPoint, inkStraightenCurrentPoint);
                }
                else if (GetDistance(inkStraightenLowSpeedAnchorPoint, point) > displacementThreshold)
                {
                    isInkStraightenLowSpeed = false;
                }
            }
            else
            {
                isInkStraightenLowSpeed = false;
            }
        }

        private void EndInkStraightenTracking(Point point)
        {
            if (isInkStraightenTracking && isInkStraightenActivated)
            {
                isInkStraightenPendingApply = true;
                inkStraightenPendingStartPoint = inkStraightenStartPoint;
                inkStraightenPendingEndPoint = point;
            }
            ResetInkStraightenTracking();
        }

        private void ResetInkStraightenTracking()
        {
            isInkStraightenTracking = false;
            isInkStraightenActivated = false;
            isInkStraightenLowSpeed = false;
            inkStraightenLowSpeedStartAt = DateTime.MinValue;
            inkStraightenLastSampleAt = DateTime.MinValue;
            RemoveInkStraightenPreviewLine();
        }

        private void UpdateInkStraightenPreviewLine(Point startPoint, Point endPoint)
        {
            if (inkStraightenPreviewLine == null)
            {
                inkStraightenPreviewLine = new Line
                {
                    IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(inkCanvas.DefaultDrawingAttributes.Color),
                    StrokeThickness = Math.Max(1, inkCanvas.DefaultDrawingAttributes.Width)
                };
                inkCanvas.Children.Add(inkStraightenPreviewLine);
            }

            inkStraightenPreviewLine.StrokeThickness = Math.Max(1, inkCanvas.DefaultDrawingAttributes.Width);
            inkStraightenPreviewLine.X1 = startPoint.X;
            inkStraightenPreviewLine.Y1 = startPoint.Y;
            inkStraightenPreviewLine.X2 = endPoint.X;
            inkStraightenPreviewLine.Y2 = endPoint.Y;
        }

        private void RemoveInkStraightenPreviewLine()
        {
            if (inkStraightenPreviewLine == null) return;
            inkCanvas.Children.Remove(inkStraightenPreviewLine);
            inkStraightenPreviewLine = null;
        }

        private void TryApplyInkStraighten(Stroke stroke)
        {
            if (!isInkStraightenPendingApply || stroke == null)
            {
                isInkStraightenPendingApply = false;
                return;
            }

            if (stroke.StylusPoints.Count == 0)
            {
                isInkStraightenPendingApply = false;
                return;
            }

            var firstPoint = stroke.StylusPoints[0].ToPoint();
            if (GetDistance(firstPoint, inkStraightenPendingStartPoint) > 20)
            {
                isInkStraightenPendingApply = false;
                return;
            }

            float pressure = stroke.StylusPoints[0].PressureFactor;
            stroke.StylusPoints = new StylusPointCollection
            {
                new StylusPoint(inkStraightenPendingStartPoint.X, inkStraightenPendingStartPoint.Y, pressure),
                new StylusPoint(inkStraightenPendingEndPoint.X, inkStraightenPendingEndPoint.Y, pressure)
            };

            isInkStraightenPendingApply = false;
        }
    }
}
