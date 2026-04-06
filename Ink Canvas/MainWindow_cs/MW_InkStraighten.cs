using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private const int MousePointerId = -1;

        private class InkStraightenTrackingState
        {
            public bool IsTracking { get; set; }
            public bool IsActivated { get; set; }
            public bool IsLowSpeed { get; set; }
            public Point StartPoint { get; set; }
            public Point CurrentPoint { get; set; }
            public Point LowSpeedAnchorPoint { get; set; }
            public DateTime LowSpeedStartAt { get; set; } = DateTime.MinValue;
            public DateTime LastSampleAt { get; set; } = DateTime.MinValue;
            public Line PreviewLine { get; set; }
        }

        private class InkStraightenPendingApplyState
        {
            public Point StartPoint { get; set; }
            public Point EndPoint { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        private readonly Dictionary<int, InkStraightenTrackingState> inkStraightenTrackingStates = new Dictionary<int, InkStraightenTrackingState>();
        private readonly List<InkStraightenPendingApplyState> inkStraightenPendingStates = new List<InkStraightenPendingApplyState>();
        private readonly HashSet<int> activeTouchPointerIds = new HashSet<int>();

        private bool IsInkStraightenAvailable()
        {
            return Settings?.InkToShape != null
                   && Settings.InkToShape.IsInkStraightenEnabled
                   && currentMode == 0
                   && drawingShapeMode == 0
                   && inkCanvas.EditingMode == InkCanvasEditingMode.Ink;
        }

        private InkStraightenTrackingState GetTrackingState(int pointerId)
        {
            if (inkStraightenTrackingStates.TryGetValue(pointerId, out var state))
            {
                return state;
            }

            state = new InkStraightenTrackingState();
            inkStraightenTrackingStates[pointerId] = state;
            return state;
        }

        private void BeginInkStraightenTracking(int pointerId, Point point, bool isTouch = false)
        {
            if (isTouch)
            {
                activeTouchPointerIds.Add(pointerId);
            }
            else if (pointerId == MousePointerId && activeTouchPointerIds.Count > 0)
            {
                return;
            }

            if (!IsInkStraightenAvailable())
            {
                ResetInkStraightenTracking(pointerId);
                return;
            }

            var state = GetTrackingState(pointerId);
            ResetInkStraightenTracking(pointerId);

            state.IsTracking = true;
            state.StartPoint = point;
            state.CurrentPoint = point;
            state.LastSampleAt = DateTime.UtcNow;
        }

        private void UpdateInkStraightenTracking(int pointerId, Point point)
        {
            if (!inkStraightenTrackingStates.TryGetValue(pointerId, out var state) || !state.IsTracking)
            {
                return;
            }

            if (!IsInkStraightenAvailable())
            {
                ResetInkStraightenTracking(pointerId);
                return;
            }

            var now = DateTime.UtcNow;
            var deltaMs = (now - state.LastSampleAt).TotalMilliseconds;
            if (deltaMs <= 0) return;

            var distance = GetDistance(state.CurrentPoint, point);
            var speed = distance / deltaMs;

            state.CurrentPoint = point;
            state.LastSampleAt = now;

            if (state.IsActivated)
            {
                UpdateInkStraightenPreviewLine(state, state.StartPoint, state.CurrentPoint);
                return;
            }

            EvaluateLowSpeedHold(state, point, speed, now);
        }

        private void EvaluateLowSpeedHold(InkStraightenTrackingState state, Point point, double speed, DateTime now)
        {
            var speedThreshold = Math.Max(0.0001, Settings.InkToShape.InkStraightenSpeedThreshold);
            var displacementThreshold = Math.Max(0.1, Settings.InkToShape.InkStraightenDisplacementThreshold);
            var holdDurationMs = Math.Max(100, Settings.InkToShape.InkStraightenHoldDurationMs);

            if (speed < speedThreshold)
            {
                if (!state.IsLowSpeed)
                {
                    state.IsLowSpeed = true;
                    state.LowSpeedAnchorPoint = point;
                    state.LowSpeedStartAt = now;
                }

                if (GetDistance(state.LowSpeedAnchorPoint, point) <= displacementThreshold
                    && (now - state.LowSpeedStartAt).TotalMilliseconds >= holdDurationMs)
                {
                    state.IsActivated = true;
                    UpdateInkStraightenPreviewLine(state, state.StartPoint, state.CurrentPoint);
                }
                else if (GetDistance(state.LowSpeedAnchorPoint, point) > displacementThreshold)
                {
                    state.IsLowSpeed = false;
                }
            }
            else
            {
                state.IsLowSpeed = false;
            }
        }

        private void EndInkStraightenTracking(int pointerId, Point point, bool isTouch = false)
        {
            if (isTouch)
            {
                activeTouchPointerIds.Remove(pointerId);
            }

            if (!inkStraightenTrackingStates.TryGetValue(pointerId, out var state) || !state.IsTracking)
            {
                return;
            }

            if (IsInkStraightenAvailable() && !state.IsActivated && state.IsLowSpeed)
            {
                EvaluateLowSpeedHold(state, point, 0, DateTime.UtcNow);
            }

            if (state.IsTracking && state.IsActivated)
            {
                inkStraightenPendingStates.Add(new InkStraightenPendingApplyState
                {
                    StartPoint = state.StartPoint,
                    EndPoint = point
                });
            }

            ResetInkStraightenTracking(pointerId);
        }

        private void ResetInkStraightenTracking(int pointerId)
        {
            if (!inkStraightenTrackingStates.TryGetValue(pointerId, out var state)) return;

            if (state.PreviewLine != null)
            {
                inkCanvas.Children.Remove(state.PreviewLine);
                state.PreviewLine = null;
            }

            state.IsTracking = false;
            state.IsActivated = false;
            state.IsLowSpeed = false;
            state.LowSpeedStartAt = DateTime.MinValue;
            state.LastSampleAt = DateTime.MinValue;
        }

        private void UpdateInkStraightenPreviewLine(InkStraightenTrackingState state, Point startPoint, Point endPoint)
        {
            if (state.PreviewLine == null)
            {
                state.PreviewLine = new Line
                {
                    IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(inkCanvas.DefaultDrawingAttributes.Color),
                    StrokeThickness = Math.Max(1, inkCanvas.DefaultDrawingAttributes.Width)
                };
                inkCanvas.Children.Add(state.PreviewLine);
            }

            state.PreviewLine.StrokeThickness = Math.Max(1, inkCanvas.DefaultDrawingAttributes.Width);
            state.PreviewLine.X1 = startPoint.X;
            state.PreviewLine.Y1 = startPoint.Y;
            state.PreviewLine.X2 = endPoint.X;
            state.PreviewLine.Y2 = endPoint.Y;
        }

        private void TryApplyInkStraighten(Stroke stroke)
        {
            if (stroke == null || stroke.StylusPoints.Count == 0)
            {
                inkStraightenPendingStates.Clear();
                return;
            }

            var firstPoint = stroke.StylusPoints[0].ToPoint();
            var now = DateTime.UtcNow;
            inkStraightenPendingStates.RemoveAll(x => (now - x.CreatedAt).TotalSeconds > 5);

            var pending = inkStraightenPendingStates
                .OrderBy(x => GetDistance(firstPoint, x.StartPoint))
                .FirstOrDefault();

            if (pending == null || GetDistance(firstPoint, pending.StartPoint) > 20)
            {
                return;
            }

            float pressure = stroke.StylusPoints[0].PressureFactor;
            stroke.StylusPoints = new StylusPointCollection
            {
                new StylusPoint(pending.StartPoint.X, pending.StartPoint.Y, pressure),
                new StylusPoint(pending.EndPoint.X, pending.EndPoint.Y, pressure)
            };

            inkStraightenPendingStates.Remove(pending);
        }

        private void inkCanvas_StraightenStylusDown(object sender, StylusDownEventArgs e)
        {
            if (e.StylusDevice?.TabletDevice?.Type != TabletDeviceType.Stylus) return;
            BeginInkStraightenTracking(e.StylusDevice.Id, e.GetPosition(inkCanvas));
        }

        private void inkCanvas_StraightenStylusMove(object sender, StylusEventArgs e)
        {
            if (e.StylusDevice?.TabletDevice?.Type != TabletDeviceType.Stylus) return;
            UpdateInkStraightenTracking(e.StylusDevice.Id, e.GetPosition(inkCanvas));
        }

        private void inkCanvas_StraightenStylusUp(object sender, StylusEventArgs e)
        {
            if (e.StylusDevice?.TabletDevice?.Type != TabletDeviceType.Stylus) return;
            EndInkStraightenTracking(e.StylusDevice.Id, e.GetPosition(inkCanvas));
        }
    }
}
