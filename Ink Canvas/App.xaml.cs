using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Runtime.InteropServices;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Process-wide mutex used to ensure a single app instance by default.
        /// </summary>
        System.Threading.Mutex mutex;

        /// <summary>
        /// Startup arguments captured during application initialization.
        /// </summary>
        public static string[] StartArgs = null;

        /// <summary>
        /// Root directory used for logs, settings, and runtime assets.
        /// </summary>
        public static string RootPath = Environment.GetEnvironmentVariable("APPDATA") + "\\Ink Canvas\\";

        /// <summary>
        /// Initializes application-level event handlers.
        /// </summary>
        public App()
        {
            this.Startup += new StartupEventHandler(App_Startup);
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        /// <summary>
        /// Handles uncaught UI-thread exceptions, records diagnostics, and suppresses crash termination.
        /// </summary>
        /// <param name="sender">Exception event source.</param>
        /// <param name="e">Exception details and handling flag.</param>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Ink_Canvas.MainWindow.ShowNewMessage("抱歉，出现未预期的异常，可能导致 Ink Canvas 画板运行不稳定。\n建议保存墨迹后重启应用。", true);
            LogHelper.NewLog(e.Exception.ToString());
            e.Handled = true;
        }

        /// <summary>
        /// Initializes runtime state, enforces single-instance launch, and stores startup arguments.
        /// </summary>
        /// <param name="sender">Startup event source.</param>
        /// <param name="e">Startup event data containing command-line arguments.</param>
        void App_Startup(object sender, StartupEventArgs e)
        {
            /*if (!StoreHelper.IsStoreApp) */RootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

            LogHelper.NewLog(string.Format("Ink Canvas Starting (Version: {0})", Assembly.GetExecutingAssembly().GetName().Version.ToString()));

            bool ret;
            mutex = new System.Threading.Mutex(true, "Ink_Canvas_Artistry", out ret);

            if (!ret && !e.Args.Contains("-m")) //-m multiple
            {
                LogHelper.NewLog("Detected existing instance; requesting whiteboard activation");
                TriggerExistingInstanceToWhiteboard();
                LogHelper.NewLog("Ink Canvas automatically closed");
                Environment.Exit(0);
            }

            StartArgs = e.Args;
        }

        /// <summary>
        /// 通过模拟 <c>Alt + B</c> 全局快捷键，通知已运行实例切换到白板模式。
        /// </summary>
        /// <remarks>
        /// 该方法仅在检测到已有实例时调用，用于替代“已有一个程序实例正在运行”的提示。
        /// Alt+B 对应已注册的全局快捷键 <c>HotKey_Board</c>，其行为是进入白板而非屏幕批注模式。
        /// </remarks>
        private void TriggerExistingInstanceToWhiteboard()
        {
            const byte VK_MENU = 0x12;
            const byte VK_B = 0x42;
            const uint KEYEVENTF_KEYUP = 0x0002;

            try
            {
                keybd_event(VK_MENU, 0, 0, 0);
                keybd_event(VK_B, 0, 0, 0);
                keybd_event(VK_B, 0, KEYEVENTF_KEYUP, 0);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
            }
            catch (Exception ex)
            {
                LogHelper.NewLog($"Failed to notify existing instance to switch whiteboard mode: {ex}");
            }
        }

        /// <summary>
        /// 触发单个虚拟键按下/抬起事件。
        /// </summary>
        /// <param name="bVk">虚拟键码。</param>
        /// <param name="bScan">硬件扫描码（此处固定传 0）。</param>
        /// <param name="dwFlags">事件标记，抬起时传 <c>0x0002</c>。</param>
        /// <param name="dwExtraInfo">附加信息指针（此处固定传 0）。</param>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        /// <summary>
        /// Applies custom mouse-wheel scrolling behavior for wrapped <see cref="ScrollViewerEx"/> controls.
        /// </summary>
        /// <param name="sender">Scroll viewer receiving the wheel event.</param>
        /// <param name="e">Mouse wheel event data.</param>
        private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (System.Windows.Forms.SystemInformation.MouseWheelScrollLines == -1)
                    e.Handled = false;
                else
                    try
                    {
                        ScrollViewerEx SenderScrollViewer = (ScrollViewerEx)sender;
                        SenderScrollViewer.ScrollToVerticalOffset(SenderScrollViewer.VerticalOffset - e.Delta * 10 * System.Windows.Forms.SystemInformation.MouseWheelScrollLines / (double)120);
                        e.Handled = true;
                    }
                    catch {  }
            }
            catch {  }
        }
    }
}
