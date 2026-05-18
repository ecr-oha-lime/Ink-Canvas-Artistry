using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Threading;

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
        /// Named event used to notify the running instance to enter whiteboard mode.
        /// </summary>
        private EventWaitHandle switchToWhiteboardEvent;

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
                using (var existingInstanceSignal = EventWaitHandle.OpenExisting("Ink_Canvas_Artistry_SwitchToWhiteboard"))
                {
                    existingInstanceSignal.Set();
                }
                LogHelper.NewLog("Ink Canvas automatically closed");
                Environment.Exit(0);
            }

            RegisterWhiteboardSwitchSignal();
            StartArgs = e.Args;
        }

        /// <summary>
        /// 注册跨进程白板切换信号监听，使后续启动的实例可唤起当前实例进入白板模式。
        /// </summary>
        private void RegisterWhiteboardSwitchSignal()
        {
            switchToWhiteboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Ink_Canvas_Artistry_SwitchToWhiteboard");
            ThreadPool.RegisterWaitForSingleObject(switchToWhiteboardEvent, (_, __) =>
            {
                Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (Current?.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.EnsureWhiteboardModeFromExternalRequest();
                    }
                }));
            }, null, -1, false);
        }

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
