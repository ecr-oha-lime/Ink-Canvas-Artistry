using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private enum StraightenInputType
        {
            None,
            Mouse,
            Stylus
        }

        private StraightenInputType _straightenInputType = StraightenInputType.None;
        private bool _isStraightenTracking;
        private bool _isStraightenCandidate;
        private bool _isStraightenTriggered;
        private Point _straightenStrokeStartPoint;
        private Point _straightenLastPoint;
        private Point _straightenLowSpeedStartPoint;
        private long _straightenLastTimestamp;
        private long _straightenLowSpeedStartTimestamp;
        private double _straightenLowSpeedDisplacement;

        private bool _hasPendingStraightenStroke;
        private Point _pendingStraightenStartPoint;
        private Point _pendingStraightenEndPoint;

        private readonly Line _straightenPreviewLine = new Line
        {
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        private bool CanHandleInkStraightening()
        {
            return Settings.InkStraightening != null
                && Settings.InkStraightening.IsEnabled
                && drawingShapeMode == 0
                && inkCanvas.EditingMode == InkCanvasEditingMode.Ink;
        }

        private void inkCanvas_PreviewStylusDown(object sender, StylusDownEventArgs e)
        {
            StartStraightenTracking(StraightenInputType.Stylus, e.GetPosition(inkCanvas), e.Timestamp);
        }

        private void inkCanvas_PreviewStylusMove(object sender, StylusEventArgs e)
        {
            UpdateStraightenTracking(StraightenInputType.Stylus, e.GetPosition(inkCanvas), e.Timestamp);
        }

        private void inkCanvas_PreviewStylusUp(object sender, StylusEventArgs e)
        {
            EndStraightenTracking(StraightenInputType.Stylus);
        }

        private void StartStraightenTracking(StraightenInputType inputType, Point position, long timestamp)
        {
            if (!CanHandleInkStraightening()) return;

            if (_straightenPreviewLine.Parent == null)
            {
                inkCanvas.Children.Add(_straightenPreviewLine);
            }

            _straightenInputType = inputType;
            _isStraightenTracking = true;
            _isStraightenCandidate = false;
            _isStraightenTriggered = false;
            _straightenStrokeStartPoint = position;
            _straightenLastPoint = position;
            _straightenLowSpeedStartPoint = position;
            _straightenLastTimestamp = timestamp;
            _straightenLowSpeedStartTimestamp = 0;
            _straightenLowSpeedDisplacement = 0;
            _hasPendingStraightenStroke = false;
            HideStraightenPreview();
        }

        private void UpdateStraightenTracking(StraightenInputType inputType, Point position, long timestamp)
        {
            if (!_isStraightenTracking || _straightenInputType != inputType || !CanHandleInkStraightening()) return;

            long dt = Math.Max(1, timestamp - _straightenLastTimestamp);
            var moveDistance = GetDistance(_straightenLastPoint, position);
            var speed = moveDistance / dt;

            if (_isStraightenTriggered)
            {
                UpdateStraightenPreview(position);
            }
            else
            {
                if (speed < Settings.InkStraightening.SpeedThresholdPxPerMs)
                {
                    if (!_isStraightenCandidate)
                    {
                        _isStraightenCandidate = true;
                        _straightenLowSpeedStartTimestamp = timestamp;
                        _straightenLowSpeedStartPoint = position;
                        _straightenLowSpeedDisplacement = 0;
                    }
                    else
                    {
                        _straightenLowSpeedDisplacement += moveDistance;
                    }

                    if (_isStraightenCandidate
                        && _straightenLowSpeedDisplacement <= Settings.InkStraightening.DisplacementThresholdPx
                        && timestamp - _straightenLowSpeedStartTimestamp >= Settings.InkStraightening.HoldDurationMs)
                    {
                        _isStraightenTriggered = true;
                        UpdateStraightenPreview(position);
                    }
                }
                else
                {
                    _isStraightenCandidate = false;
                    _straightenLowSpeedDisplacement = 0;
                }
            }

            _straightenLastPoint = position;
            _straightenLastTimestamp = timestamp;
        }

        private void EndStraightenTracking(StraightenInputType inputType)
        {
            if (_straightenInputType != inputType) return;

            if (_isStraightenTriggered)
            {
                _hasPendingStraightenStroke = true;
                _pendingStraightenStartPoint = _straightenStrokeStartPoint;
                _pendingStraightenEndPoint = _straightenLastPoint;
            }

            _straightenInputType = StraightenInputType.None;
            _isStraightenTracking = false;
            _isStraightenCandidate = false;
            _isStraightenTriggered = false;
            _straightenLowSpeedDisplacement = 0;
            HideStraightenPreview();
        }

        private void UpdateStraightenPreview(Point endPoint)
        {
            _straightenPreviewLine.X1 = _straightenStrokeStartPoint.X;
            _straightenPreviewLine.Y1 = _straightenStrokeStartPoint.Y;
            _straightenPreviewLine.X2 = endPoint.X;
            _straightenPreviewLine.Y2 = endPoint.Y;
            _straightenPreviewLine.Stroke = inkCanvas.DefaultDrawingAttributes.ColorBrush;
            _straightenPreviewLine.StrokeThickness = Math.Max(1, inkCanvas.DefaultDrawingAttributes.Width);
            _straightenPreviewLine.Opacity = inkCanvas.DefaultDrawingAttributes.IsHighlighter ? 0.5 : 1;
            _straightenPreviewLine.Visibility = Visibility.Visible;
        }

        private void HideStraightenPreview()
        {
            _straightenPreviewLine.Visibility = Visibility.Collapsed;
        }

        private void TryReplaceCollectedStrokeWithStraightLine(InkCanvasStrokeCollectedEventArgs e)
        {
            if (!_hasPendingStraightenStroke
                || Settings.InkStraightening == null
                || !Settings.InkStraightening.IsEnabled
                || e?.Stroke == null)
            {
                return;
            }

            _hasPendingStraightenStroke = false;
            var start = _pendingStraightenStartPoint;
            var end = _pendingStraightenEndPoint;

            if (GetDistance(start, end) < 1)
            {
                return;
            }

            var pressure = e.Stroke.StylusPoints.Count > 0
                ? e.Stroke.StylusPoints[0].PressureFactor
                : 0.5f;

            var lineStroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(start.X, start.Y, pressure),
                new StylusPoint(end.X, end.Y, pressure)
            })
            {
                DrawingAttributes = e.Stroke.DrawingAttributes.Clone()
            };

            SetNewBackupOfStroke();
            _currentCommitType = CommitReason.ShapeRecognition;
            inkCanvas.Strokes.Remove(e.Stroke);
            inkCanvas.Strokes.Add(lineStroke);
            _currentCommitType = CommitReason.UserInput;
        }

        private void HandleMouseStraighteningOnDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            StartStraightenTracking(StraightenInputType.Mouse, e.GetPosition(inkCanvas), Environment.TickCount64);
        }

        private void HandleMouseStraighteningOnMove(MouseEventArgs e)
        {
            if (!_isStraightenTracking || _straightenInputType != StraightenInputType.Mouse) return;
            UpdateStraightenTracking(StraightenInputType.Mouse, e.GetPosition(inkCanvas), Environment.TickCount64);
        }

        private void HandleMouseStraighteningOnUp(MouseButtonEventArgs e)
        {
            if (e != null && e.ChangedButton != MouseButton.Left) return;
            EndStraightenTracking(StraightenInputType.Mouse);
        }
    }
}
