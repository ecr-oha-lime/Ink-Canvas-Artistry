using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        private const int MousePointerId = -1;

        private class InkStraightenSession
        {
            public Point StartPoint;
            public Point LastPoint;
            public DateTime LastTimestamp;
            public bool IsLowSpeedCandidate;
            public DateTime LowSpeedStartTimestamp;
            public Point LowSpeedAnchorPoint;
            public double LowSpeedDisplacement;
            public bool IsTriggered;
            public bool IsCommitted;
            public bool IsInputSuppressed;
            public Line PreviewLine;
            public Point LatestPoint;
        }

        /// <summary>
        /// 当前活动的墨迹拉直会话集合（按指针 id 管理）。
        /// </summary>
        private readonly Dictionary<int, InkStraightenSession> _inkStraightenSessions = new Dictionary<int, InkStraightenSession>();

        /// <summary>
        /// 判断输入事件是否由触控笔提升而来。
        /// </summary>
        private static bool IsPromotedFromStylus(MouseEventArgs e)
        {
            return e?.StylusDevice != null;
        }

        /// <summary>
        /// 判断当前是否满足墨迹拉直可用条件。
        /// </summary>
        private bool IsInkStraightenAvailable()
        {
            bool isInkInputMode = inkCanvas.EditingMode == InkCanvasEditingMode.Ink;
            bool isMultiTouchInkInputMode = isInMultiTouchMode && inkCanvas.EditingMode == InkCanvasEditingMode.None;

            return Settings?.InkStraighten != null
                   && Settings.InkStraighten.IsInkStraightenEnabled
                   && (isInkInputMode || isMultiTouchInkInputMode)
                   && drawingShapeMode == 0
                   && !forceEraser;
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private double GetInputScaleFactor()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return Math.Max(1.0, dpi.DpiScaleX);
        }

        private int ClampInkStraightenHoldDurationMs()
        {
            return Math.Max(200, Math.Min(2000, Settings.InkStraighten.HoldDurationMs));
        }

        /// <summary>
        /// 启动墨迹拉直会话。
        /// </summary>
        private void StartInkStraightenSession(int pointerId, Point startPoint)
        {
            if (!IsInkStraightenAvailable())
            {
                return;
            }

            var now = DateTime.UtcNow;
            _inkStraightenSessions[pointerId] = new InkStraightenSession
            {
                StartPoint = startPoint,
                LastPoint = startPoint,
                LatestPoint = startPoint,
                LastTimestamp = now,
                IsLowSpeedCandidate = true,
                LowSpeedStartTimestamp = now,
                LowSpeedAnchorPoint = startPoint,
                LowSpeedDisplacement = 0
            };
        }

        /// <summary>
        /// 更新墨迹拉直会话状态。
        /// </summary>
        private void UpdateInkStraightenSession(int pointerId, Point currentPoint)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session))
            {
                return;
            }
            if (!session.IsTriggered && !IsInkStraightenAvailable())
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            double deltaMs = Math.Max(1, (now - session.LastTimestamp).TotalMilliseconds);
            double deltaDistance = Distance(currentPoint, session.LastPoint) * GetInputScaleFactor();
            double speed = deltaDistance / deltaMs;

            session.LatestPoint = currentPoint;

            if (!session.IsTriggered)
            {
                if (speed < Settings.InkStraighten.SpeedThresholdPxPerMs)
                {
                    if (!session.IsLowSpeedCandidate)
                    {
                        session.IsLowSpeedCandidate = true;
                        session.LowSpeedStartTimestamp = now;
                        session.LowSpeedAnchorPoint = currentPoint;
                        session.LowSpeedDisplacement = 0;
                    }
                    else
                    {
                        session.LowSpeedDisplacement =
                            Distance(currentPoint, session.LowSpeedAnchorPoint) * GetInputScaleFactor();
                    }

                    if (session.LowSpeedDisplacement <= Settings.InkStraighten.DisplacementThresholdPx
                        && (now - session.LowSpeedStartTimestamp).TotalMilliseconds >= ClampInkStraightenHoldDurationMs())
                    {
                        session.IsTriggered = true;
                        HideMultiTouchRawStrokePreview(pointerId);
                        if (!session.IsInputSuppressed && inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                        {
                            inkCanvas.EditingMode = InkCanvasEditingMode.None;
                            session.IsInputSuppressed = true;
                        }
                        UpdateInkStraightenPreview(session, currentPoint);
                    }
                    else if (session.LowSpeedDisplacement > Settings.InkStraighten.DisplacementThresholdPx)
                    {
                        session.IsLowSpeedCandidate = false;
                    }
                }
                else
                {
                    session.IsLowSpeedCandidate = false;
                }
            }
            else
            {
                UpdateInkStraightenPreview(session, currentPoint);
            }

            session.LastPoint = currentPoint;
            session.LastTimestamp = now;
        }

        /// <summary>
        /// 在多指书写下触发拉直后，立即隐藏原始自由曲线预览。
        /// </summary>
        private void HideMultiTouchRawStrokePreview(int pointerId)
        {
            if (!isInMultiTouchMode)
            {
                return;
            }

            var visualCanvas = GetVisualCanvas(pointerId);
            if (visualCanvas != null && inkCanvas.Children.Contains(visualCanvas))
            {
                inkCanvas.Children.Remove(visualCanvas);
            }
        }

        /// <summary>
        /// 更新墨迹拉直预览效果。
        /// </summary>
        private void UpdateInkStraightenPreview(InkStraightenSession session, Point currentPoint)
        {
            if (session.PreviewLine == null)
            {
                var drawingAttributes = inkCanvas.DefaultDrawingAttributes;
                session.PreviewLine = new Line
                {
                    IsHitTestVisible = false,
                    Stroke = new SolidColorBrush(drawingAttributes.Color),
                    StrokeThickness = Math.Max(1, drawingAttributes.Width),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                Panel.SetZIndex(session.PreviewLine, 9999);
                Main_Grid.Children.Add(session.PreviewLine);
            }
            session.PreviewLine.X1 = session.StartPoint.X;
            session.PreviewLine.Y1 = session.StartPoint.Y;
            session.PreviewLine.X2 = currentPoint.X;
            session.PreviewLine.Y2 = currentPoint.Y;
        }

        /// <summary>
        /// 结束指定墨迹拉直会话并清理预览。
        /// </summary>
        private void EndInkStraightenSession(int pointerId)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session))
            {
                return;
            }

            if (session.IsCommitted)
            {
                if (session.IsInputSuppressed && inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                _inkStraightenSessions.Remove(pointerId);
                return;
            }

            if (session.PreviewLine != null && Main_Grid.Children.Contains(session.PreviewLine))
            {
                Main_Grid.Children.Remove(session.PreviewLine);
                session.PreviewLine = null;
            }

            bool shouldDeferCommitToStrokeCollected = session.IsTriggered
                                                      && isInMultiTouchMode
                                                      && inkCanvas.EditingMode == InkCanvasEditingMode.None;

            if (session.IsTriggered)
            {
                if (!shouldDeferCommitToStrokeCollected)
                {
                    var straightStroke = new Stroke(new StylusPointCollection
                    {
                        new StylusPoint(session.StartPoint.X, session.StartPoint.Y, 0.5f),
                        new StylusPoint(session.LatestPoint.X, session.LatestPoint.Y, 0.5f)
                    })
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };

                    _currentCommitType = CommitReason.UserInput;
                    inkCanvas.Strokes.Add(straightStroke);
                    session.IsCommitted = true;
                }
            }

            if (session.IsInputSuppressed && inkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }

            if (!shouldDeferCommitToStrokeCollected)
            {
                _inkStraightenSessions.Remove(pointerId);
            }
        }

        /// <summary>
        /// 尝试将活动拉直结果应用到新笔迹。
        /// </summary>
        private bool TryApplyActiveInkStraighten(Stroke rawStroke)
        {
            if (rawStroke == null || rawStroke.StylusPoints.Count == 0)
            {
                return false;
            }

            Point rawStartPoint = rawStroke.StylusPoints.First().ToPoint();
            InkStraightenSession matchedSession = null;
            double minDistance = double.MaxValue;
            foreach (var session in _inkStraightenSessions.Values)
            {
                if (!session.IsTriggered || session.IsCommitted)
                {
                    continue;
                }

                double distance = Distance(rawStartPoint, session.StartPoint);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    matchedSession = session;
                }
            }

            if (matchedSession == null || minDistance > 48)
            {
                return false;
            }

            if (matchedSession.PreviewLine != null && Main_Grid.Children.Contains(matchedSession.PreviewLine))
            {
                Main_Grid.Children.Remove(matchedSession.PreviewLine);
                matchedSession.PreviewLine = null;
            }

            var straightStroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(matchedSession.StartPoint.X, matchedSession.StartPoint.Y, rawStroke.StylusPoints.First().PressureFactor),
                new StylusPoint(matchedSession.LatestPoint.X, matchedSession.LatestPoint.Y, rawStroke.StylusPoints.Last().PressureFactor)
            })
            {
                DrawingAttributes = rawStroke.DrawingAttributes.Clone()
            };

            SetNewBackupOfStroke();
            var previousReplaceCommitType = _currentCommitType;
            try
            {
                _currentCommitType = CommitReason.ShapeRecognition;
                inkCanvas.Strokes.Remove(rawStroke);
                inkCanvas.Strokes.Add(straightStroke);
            }
            finally
            {
                _currentCommitType = previousReplaceCommitType;
            }
            matchedSession.IsCommitted = true;
            var matchedSessionPair = _inkStraightenSessions.FirstOrDefault(item => ReferenceEquals(item.Value, matchedSession));
            if (matchedSessionPair.Value != null)
            {
                _inkStraightenSessions.Remove(matchedSessionPair.Key);
            }
            return true;
        }

        private void inkCanvas_StylusDownForStraighten(object sender, StylusDownEventArgs e)
        {
            StartInkStraightenSession(e.StylusDevice.Id, e.GetPosition(inkCanvas));
        }

        private void inkCanvas_StylusMoveForStraighten(object sender, StylusEventArgs e)
        {
            UpdateInkStraightenSession(e.StylusDevice.Id, e.GetPosition(inkCanvas));
        }

        private void inkCanvas_StylusUpForStraighten(object sender, StylusEventArgs e)
        {
            EndInkStraightenSession(e.StylusDevice.Id);
        }

        private void HandleMouseStraightenDown(MouseButtonEventArgs e)
        {
            if (IsPromotedFromStylus(e)) return;
            if (e?.ChangedButton == MouseButton.Left)
            {
                StartInkStraightenSession(MousePointerId, e.GetPosition(inkCanvas));
            }
        }

        private void HandleMouseStraightenMove(MouseEventArgs e)
        {
            if (IsPromotedFromStylus(e)) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateInkStraightenSession(MousePointerId, e.GetPosition(inkCanvas));
            }
        }

        private void HandleMouseStraightenUp(MouseButtonEventArgs e)
        {
            if (e != null && IsPromotedFromStylus(e)) return;
            if (e == null || e.ChangedButton == MouseButton.Left)
            {
                EndInkStraightenSession(MousePointerId);
            }
        }

        private bool TryApplyPendingInkStraighten(Stroke rawStroke)
        {
            return TryApplyActiveInkStraighten(rawStroke);
        }

        /// <summary>
        /// 针对多指书写延迟提交场景：在触点抬起时直接提交拉直笔迹，避免原始曲线闪现。
        /// </summary>
        private bool TryCommitDeferredInkStraightenByPointer(int pointerId, Stroke rawStroke)
        {
            if (!_inkStraightenSessions.TryGetValue(pointerId, out var session)
                || !session.IsTriggered
                || session.IsCommitted)
            {
                return false;
            }

            if (session.PreviewLine != null && Main_Grid.Children.Contains(session.PreviewLine))
            {
                Main_Grid.Children.Remove(session.PreviewLine);
                session.PreviewLine = null;
            }

            float startPressure = rawStroke?.StylusPoints?.Count > 0 ? rawStroke.StylusPoints.First().PressureFactor : 0.5f;
            float endPressure = rawStroke?.StylusPoints?.Count > 0 ? rawStroke.StylusPoints.Last().PressureFactor : 0.5f;
            var straightStroke = new Stroke(new StylusPointCollection
            {
                new StylusPoint(session.StartPoint.X, session.StartPoint.Y, startPressure),
                new StylusPoint(session.LatestPoint.X, session.LatestPoint.Y, endPressure)
            })
            {
                DrawingAttributes = rawStroke?.DrawingAttributes?.Clone() ?? inkCanvas.DefaultDrawingAttributes.Clone()
            };

            SetNewBackupOfStroke();
            var previousCommitType = _currentCommitType;
            try
            {
                _currentCommitType = CommitReason.ShapeRecognition;
                inkCanvas.Strokes.Add(straightStroke);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }

            session.IsCommitted = true;
            if (session.IsInputSuppressed && inkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
            _inkStraightenSessions.Remove(pointerId);
            return true;
        }

        private void Window_PreviewMouseUpForStraighten(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                EndInkStraightenSession(MousePointerId);
            }
        }

        private void Window_PreviewStylusUpForStraighten(object sender, StylusEventArgs e)
        {
            EndInkStraightenSession(e.StylusDevice.Id);
        }
    }
}
