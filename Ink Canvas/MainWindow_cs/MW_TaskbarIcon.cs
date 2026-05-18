using Ink_Canvas.Helpers;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 托盘图标实例，负责承载右键菜单命令。
        /// </summary>
        private Forms.NotifyIcon taskbarNotifyIcon;

        /// <summary>
        /// 初始化托盘图标与右键菜单（退出、重启、重置显示位置）。
        /// </summary>
        private void InitializeTaskbarIcon()
        {
            if (taskbarNotifyIcon != null) return;

            taskbarNotifyIcon = new Forms.NotifyIcon
            {
                Text = "Ink Canvas Artistry",
                Visible = true,
                Icon = LoadTaskbarIcon()
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("重置显示位置", null, (_, __) => Dispatcher.Invoke(ResetFloatingBarPosition));
            contextMenu.Items.Add("重启", null, (_, __) => Dispatcher.Invoke(RestartApplicationFromTaskbar));
            contextMenu.Items.Add("退出", null, (_, __) => Dispatcher.Invoke(ExitApplicationFromTaskbar));

            taskbarNotifyIcon.ContextMenuStrip = contextMenu;
            taskbarNotifyIcon.DoubleClick += (_, __) => Dispatcher.Invoke(ResetFloatingBarPosition);
        }

        /// <summary>
        /// 释放托盘图标资源，避免应用退出后残留无效图标。
        /// </summary>
        private void DisposeTaskbarIcon()
        {
            if (taskbarNotifyIcon == null) return;
            taskbarNotifyIcon.Visible = false;
            taskbarNotifyIcon.Dispose();
            taskbarNotifyIcon = null;
        }

        /// <summary>
        /// 托盘命令：退出应用。
        /// </summary>
        private void ExitApplicationFromTaskbar()
        {
            CloseIsFromButton = true;
            Close();
        }

        /// <summary>
        /// 托盘命令：重启应用。
        /// </summary>
        private void RestartApplicationFromTaskbar()
        {
            Process.Start(Forms.Application.ExecutablePath, "-m");
            CloseIsFromButton = true;
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 加载托盘图标：优先使用当前可执行文件关联图标，确保与软件图标保持一致。
        /// </summary>
        private System.Drawing.Icon LoadTaskbarIcon()
        {
            try
            {
                System.Drawing.Icon executableIcon = System.Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath);
                if (executableIcon != null)
                {
                    return executableIcon;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"LoadTaskbarIcon failed: {ex}", LogHelper.LogType.Error);
            }

            return SystemIcons.Application;
        }
    }
}
