using Ink_Canvas.Helpers;
using Ink_Canvas.Windows;
using System;
using System.Windows;
using System.Windows.Interop;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        private ScreenMagnifierWindow magnifierWindow;

        /// <summary>
        /// 浮动工具栏“放大镜”按钮点击：仅在普通屏幕模式下切换放大镜窗口，并收起工具菜单。
        /// </summary>
        private void SymbolIconMagnifier_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                return;
            }

            AnimationsHelper.HideWithSlideAndFade(BorderTools);
            AnimationsHelper.HideWithSlideAndFade(BoardBorderTools);

            if (magnifierWindow == null)
            {
                OpenMagnifierWindow();
            }
            else
            {
                CloseMagnifierWindow();
            }
        }

        /// <summary>
        /// 创建并显示放大镜窗口，同时在首帧截图阶段临时隐藏 FloatingBar，避免被纳入截图源。
        /// </summary>
        private void OpenMagnifierWindow()
        {
            if (magnifierWindow != null) return;

            IntPtr mainHandle = new WindowInteropHelper(this).Handle;
            Visibility floatingBarVisibility = ViewboxFloatingBar.Visibility;
            magnifierWindow = new ScreenMagnifierWindow(mainHandle)
            {
                Owner = this
            };
            magnifierWindow.RequestClose += MagnifierWindow_RequestClose;
            ViewboxFloatingBar.Visibility = Visibility.Hidden;
            magnifierWindow.Show();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ViewboxFloatingBar.Visibility = floatingBarVisibility;
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 放大镜窗口主动关闭时的回调：解除事件绑定并清空引用。
        /// </summary>
        private void MagnifierWindow_RequestClose(object sender, EventArgs e)
        {
            if (magnifierWindow != null)
            {
                magnifierWindow.RequestClose -= MagnifierWindow_RequestClose;
            }

            magnifierWindow = null;
        }

        /// <summary>
        /// 关闭放大镜窗口并释放引用。
        /// </summary>
        private void CloseMagnifierWindow()
        {
            if (magnifierWindow == null) return;

            magnifierWindow.RequestClose -= MagnifierWindow_RequestClose;
            if (magnifierWindow.IsVisible)
            {
                magnifierWindow.Close();
            }

            magnifierWindow = null;
        }

        /// <summary>
        /// 根据当前模式控制放大镜入口按钮可见性：仅在普通屏幕模式显示。
        /// </summary>
        private void UpdateMagnifierToolButtonVisibility()
        {
            if (BtnToolsMagnifier == null) return;
            BtnToolsMagnifier.Visibility = currentMode == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
