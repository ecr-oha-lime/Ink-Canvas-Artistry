using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        #region Multi-Touch

        /// <summary>
        /// 是否处于多点触控绘制模式。
        /// </summary>
        bool isInMultiTouchMode = false;

        /// <summary>
        /// 多点触控模式开关按钮事件。
        /// </summary>
        private void BorderMultiTouchMode_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isInMultiTouchMode)
            {
                inkCanvas.StylusDown -= MainWindow_StylusDown;
                inkCanvas.StylusMove -= MainWindow_StylusMove;
                inkCanvas.StylusUp -= MainWindow_StylusUp;
                inkCanvas.TouchDown -= MainWindow_TouchDown;
                inkCanvas.TouchDown += Main_Grid_TouchDown;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                isInMultiTouchMode = false;
            }
            else
            {
                inkCanvas.StylusDown += MainWindow_StylusDown;
                inkCanvas.StylusMove += MainWindow_StylusMove;
                inkCanvas.StylusUp += MainWindow_StylusUp;
                inkCanvas.TouchDown += MainWindow_TouchDown;
                inkCanvas.TouchDown -= Main_Grid_TouchDown;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                isInMultiTouchMode = true;
            }
        }

        /// <summary>
        /// 多点触控按下处理：根据触摸宽度决定擦除或书写模式。
        /// </summary>
        private void MainWindow_TouchDown(object sender, TouchEventArgs e)
        {
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            double boundWidth = e.GetTouchPoint(null).Bounds.Width;
            if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen) //启用特殊屏幕且触摸倍数为 0 时禁用橡皮
                && (boundWidth > BoundsWidth))
            {
                if (drawingShapeMode == 0 && forceEraser) return;
                double EraserThresholdValue = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthThresholdValue : Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                if (boundWidth > BoundsWidth * EraserThresholdValue)
                {
                    boundWidth *= (Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthEraserSize : Settings.Advanced.FingerModeBoundsWidthEraserSize);
                    if (Settings.Advanced.IsSpecialScreen) boundWidth *= Settings.Advanced.TouchMultiplier;
                    inkCanvas.EraserShape = new EllipseStylusShape(boundWidth, boundWidth);
                    TouchDownPointsList[e.TouchDevice.Id] = InkCanvasEditingMode.EraseByPoint;
                    inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                }
                else
                {
                    inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
                    inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                }
            }
            else
            {
                inkCanvas.EraserShape = forcePointEraser ? new EllipseStylusShape(50, 50) : new EllipseStylusShape(5, 5);
                TouchDownPointsList[e.TouchDevice.Id] = InkCanvasEditingMode.None;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }
        }

        /// <summary>
        /// 多点触控下触笔按下处理。
        /// </summary>
        private void MainWindow_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            TouchDownPointsList[e.StylusDevice.Id] = InkCanvasEditingMode.None;
        }

        /// <summary>
        /// 多点触控下触笔抬起处理：提交预览笔迹并清理缓存。
        /// </summary>
        private async void MainWindow_StylusUp(object sender, StylusEventArgs e)
        {
            try
            {
                if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus)
                {
                    // 数位板 TabletDeviceType.Stylus
                }
                else
                {
                    try
                    {
                        // 触摸屏 TabletDeviceType.Touch 
                        var strokeVisual = GetStrokeVisual(e.StylusDevice.Id);
                        if (strokeVisual?.Stroke == null || strokeVisual.Stroke.StylusPoints.Count == 0)
                        {
                            inkCanvas.Children.Remove(GetVisualCanvas(e.StylusDevice.Id));
                            return;
                        }
                        bool isCommittedByDeferredStraighten =
                            TryCommitDeferredInkStraightenByPointer(e.StylusDevice.Id, strokeVisual.Stroke);
                        if (!isCommittedByDeferredStraighten)
                        {
                            inkCanvas.Strokes.Add(strokeVisual.Stroke);
                        }
                        await Task.Delay(5); // 避免渲染墨迹完成前预览墨迹被删除导致墨迹闪烁
                        inkCanvas.Children.Remove(GetVisualCanvas(e.StylusDevice.Id));
                        if (!isCommittedByDeferredStraighten)
                        {
                            // 未触发拉直时走原有收笔流程；
                            // 若已由延迟拉直提交，则跳过以避免重复处理和闪烁。
                            inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(strokeVisual.Stroke));
                        }
                    }
                    catch(Exception ex) {
                        LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
            try
            {
                StrokeVisualList.Remove(e.StylusDevice.Id);
                VisualCanvasList.Remove(e.StylusDevice.Id);
                TouchDownPointsList.Remove(e.StylusDevice.Id);
                if (StrokeVisualList.Count == 0 || VisualCanvasList.Count == 0 || TouchDownPointsList.Count == 0)
                {
                    StrokeVisualList.Clear();
                    VisualCanvasList.Clear();
                    TouchDownPointsList.Clear();
                }
            }
            catch { }
        }

        /// <summary>
        /// 多点触控下触笔移动处理：更新预览笔迹。
        /// </summary>
        private void MainWindow_StylusMove(object sender, StylusEventArgs e)
        {
            try
            {
                if (GetTouchDownPointsList(e.StylusDevice.Id) != InkCanvasEditingMode.None) return;
                try
                {
                    if (e.StylusDevice.StylusButtons[1].StylusButtonState == StylusButtonState.Down) return;
                }
                catch { }
                var strokeVisual = GetStrokeVisual(e.StylusDevice.Id);
                var stylusPointCollection = e.GetStylusPoints(this);
                foreach (var stylusPoint in stylusPointCollection)
                {
                    strokeVisual.Add(new StylusPoint(stylusPoint.X, stylusPoint.Y, stylusPoint.PressureFactor));
                }
                strokeVisual.Redraw();
            }
            catch { }
        }

        /// <summary>
        /// 获取或创建指定触点的笔迹可视对象。
        /// </summary>
        private StrokeVisual GetStrokeVisual(int id)
        {
            if (StrokeVisualList.TryGetValue(id, out var visual))
            {
                return visual;
            }

            var strokeVisual = new StrokeVisual(inkCanvas.DefaultDrawingAttributes.Clone());
            StrokeVisualList[id] = strokeVisual;
            StrokeVisualList[id] = strokeVisual;
            var visualCanvas = new VisualCanvas(strokeVisual);
            VisualCanvasList[id] = visualCanvas;
            inkCanvas.Children.Add(visualCanvas);

            return strokeVisual;
        }

        /// <summary>
        /// 获取指定触点对应的可视容器。
        /// </summary>
        private VisualCanvas GetVisualCanvas(int id)
        {
            if (VisualCanvasList.TryGetValue(id, out var visualCanvas))
            {
                return visualCanvas;
            }
            return null;
        }

        /// <summary>
        /// 获取指定触点记录的编辑模式。
        /// </summary>
        private InkCanvasEditingMode GetTouchDownPointsList(int id)
        {
            if (TouchDownPointsList.TryGetValue(id, out var inkCanvasEditingMode))
            {
                return inkCanvasEditingMode;
            }
            return inkCanvas.EditingMode;
        }

        /// <summary>
        /// 触点 id 与按下时编辑模式映射。
        /// </summary>
        private Dictionary<int, InkCanvasEditingMode> TouchDownPointsList { get; } = new Dictionary<int, InkCanvasEditingMode>();
        /// <summary>
        /// 触点 id 与笔迹可视对象映射。
        /// </summary>
        private Dictionary<int, StrokeVisual> StrokeVisualList { get; } = new Dictionary<int, StrokeVisual>();
        /// <summary>
        /// 触点 id 与可视容器映射。
        /// </summary>
        private Dictionary<int, VisualCanvas> VisualCanvasList { get; } = new Dictionary<int, VisualCanvas>();

        #endregion

        int lastTouchDownTime = 0, lastTouchUpTime = 0;

        Point iniP = new Point(0, 0);
        bool isLastTouchEraser = false;
        private bool forcePointEraser = true;

        private void Main_Grid_TouchDown(object sender, TouchEventArgs e)
        {
            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            if (NeedUpdateIniP())
            {
                iniP = e.GetTouchPoint(inkCanvas).Position;
            }
            if (drawingShapeMode == 9 && isFirstTouchCuboid == false)
            {
                MouseTouchMove(iniP);
            }
            inkCanvas.Opacity = 1;
            double boundsWidth = GetTouchBoundWidth(e);
            if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen) //启用特殊屏幕且触摸倍数为 0 时禁用橡皮
                && (boundsWidth > BoundsWidth))
            {
                isLastTouchEraser = true;
                if (drawingShapeMode == 0 && forceEraser) return;
                double EraserThresholdValue = Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthThresholdValue : Settings.Advanced.FingerModeBoundsWidthThresholdValue;
                if (boundsWidth > BoundsWidth * EraserThresholdValue)
                {
                    boundsWidth *= (Settings.Startup.IsEnableNibMode ? Settings.Advanced.NibModeBoundsWidthEraserSize : Settings.Advanced.FingerModeBoundsWidthEraserSize);
                    if (Settings.Advanced.IsSpecialScreen) boundsWidth *= Settings.Advanced.TouchMultiplier;
                    inkCanvas.EraserShape = new EllipseStylusShape(boundsWidth, boundsWidth);
                    inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                }
                else
                {
                    if (BtnPPTSlideShowEnd.Visibility == Visibility.Visible && inkCanvas.Strokes.Count == 0 && Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl)
                    {
                        isLastTouchEraser = false;
                        inkCanvas.EditingMode = InkCanvasEditingMode.GestureOnly;
                        inkCanvas.Opacity = 0.1;
                    }
                    else
                    {
                        inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
                        inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    }
                }
            }
            else
            {
                isLastTouchEraser = false;
                inkCanvas.EraserShape = forcePointEraser ? new EllipseStylusShape(50, 50) : new EllipseStylusShape(5, 5);
                if (forceEraser) return;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
        }

        public double GetTouchBoundWidth(TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            if (!Settings.Advanced.IsQuadIR) return args.Width;
            else return Math.Sqrt(args.Width * args.Height); //四边红外
        }

        //记录触摸设备ID
        private List<int> dec = new List<int>();
        //中心点
        Point centerPoint;
        InkCanvasEditingMode lastInkCanvasEditingMode = InkCanvasEditingMode.Ink;
        bool isSingleFingerDragMode = false;
        int lastTouchDownStrokeCount = 0;

        private void inkCanvas_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            dec.Add(e.TouchDevice.Id);
            //设备1个的时候，记录中心点
            if (dec.Count == 1)
            {
                TouchPoint touchPoint = e.GetTouchPoint(inkCanvas);
                centerPoint = touchPoint.Position;

                // 仅记录数量，避免在落笔瞬间深拷贝大量墨迹导致卡顿
                lastTouchDownStrokeCount = inkCanvas.Strokes.Count;
            }
            //设备两个及两个以上，将画笔功能关闭
            if (dec.Count > 1 || isSingleFingerDragMode || !Settings.Gesture.IsEnableTwoFingerGesture)
            {
                if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
                if (inkCanvas.EditingMode != InkCanvasEditingMode.None && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
                {
                    lastInkCanvasEditingMode = inkCanvas.EditingMode;
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
            }
        }

        private void inkCanvas_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            //手势完成后切回之前的状态
            if (dec.Count > 1)
            {
                if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = lastInkCanvasEditingMode;
                }
            }
            dec.Remove(e.TouchDevice.Id);
            inkCanvas.Opacity = 1;
            if (dec.Count == 0)
            {
                // 双指手势结束后立即提交变换历史，避免切页时遗漏本次位移/缩放结果
                ToCommitStrokeManipulationHistoryAfterMouseUp();

                if (lastTouchDownStrokeCount != inkCanvas.Strokes.Count &&
                    !(drawingShapeMode == 9 && !isFirstTouchCuboid))
                {
                    // 延迟到抬手后再备份，降低写入过程中的主线程压力
                    int whiteboardIndex = CurrentWhiteboardIndex;
                    if (currentMode == 0)
                    {
                        whiteboardIndex = 0;
                    }
                    lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
                    strokeCollections[whiteboardIndex] = lastTouchDownStrokeCollection;
                }
            }
        }
        private void inkCanvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.Mode = ManipulationModes.All;
        }

        private void inkCanvas_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {

        }

        private void Main_Grid_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            if (e.Manipulators.Count() == 0)
            {
                if (forceEraser) return;
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
        }

        private void Main_Grid_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
            if ((dec.Count >= 2 && (Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode || BtnPPTSlideShowEnd.Visibility != Visibility.Visible)) || isSingleFingerDragMode)
            {
                Matrix m = new Matrix();
                ManipulationDelta md = e.DeltaManipulation;
                // Translation
                Vector trans = md.Translation;
                // Rotate, Scale
                if (Settings.Gesture.IsEnableTwoFingerGestureTranslateOrRotation)
                {
                    double rotate = md.Rotation;
                    Vector scale = md.Scale;
                    Point center = GetMatrixTransformCenterPoint(e.ManipulationOrigin, e.Source as FrameworkElement);
                    if (Settings.Gesture.IsEnableTwoFingerZoom)
                        m.ScaleAt(scale.X, scale.Y, center.X, center.Y);
                    if (Settings.Gesture.IsEnableTwoFingerRotation)
                        m.RotateAt(rotate, center.X, center.Y);
                    if (Settings.Gesture.IsEnableTwoFingerTranslate)
                        m.Translate(trans.X, trans.Y);
                    // handle Elements
                    List<UIElement> elements = InkCanvasElementsHelper.GetAllElements(inkCanvas);
                    foreach (UIElement element in elements)
                    {
                        if (Settings.Gesture.IsEnableTwoFingerTranslate)
                        {
                            ApplyElementMatrixTransform(element, m);
                        }
                        else
                        {
                            ApplyElementMatrixTransform(element, m);
                        }
                    }
                }
                // handle strokes
                if (Settings.Gesture.IsEnableTwoFingerZoom)
                {
                    foreach (Stroke stroke in inkCanvas.Strokes)
                    {
                        stroke.Transform(m, false);
                        try
                        {
                            stroke.DrawingAttributes.Width *= md.Scale.X;
                            stroke.DrawingAttributes.Height *= md.Scale.Y;
                        }
                        catch { }
                    };
                }
                else
                {
                    foreach (Stroke stroke in inkCanvas.Strokes)
                    {
                        stroke.Transform(m, false);
                    };
                }
                foreach (Circle circle in circles)
                {
                    circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(), circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                    circle.Centroid = new Point(
                        (circle.Stroke.StylusPoints[0].X + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                        (circle.Stroke.StylusPoints[0].Y + circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2
                    );
                }
            }
        }
    }
}
