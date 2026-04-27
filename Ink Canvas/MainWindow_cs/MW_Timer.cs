using Ink_Canvas.Helpers;
using System;
using System.Diagnostics;
using System.Timers;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        /// <summary>检测 PPT 运行状态定时器。</summary>
        Timer timerCheckPPT = new Timer();
        /// <summary>自动清理进程定时器。</summary>
        Timer timerKillProcess = new Timer();
        /// <summary>自动收纳浮窗定时器。</summary>
        Timer timerCheckAutoFold = new Timer();
        /// <summary>检测到的可用最新版本号。</summary>
        string AvailableLatestVersion = null;
        /// <summary>静默更新时段检查定时器。</summary>
        Timer timerCheckAutoUpdateWithSilence = new Timer();
        bool isHidingSubPanelsWhenInking = false; // 避免书写时触发二次关闭二级菜单导致动画不连续

        /// <summary>
        /// 初始化与启动主定时器参数及事件绑定。
        /// </summary>
        private void InitTimers()
        {
            timerCheckPPT.Elapsed += TimerCheckPPT_Elapsed;
            timerCheckPPT.Interval = 1000;
            timerKillProcess.Elapsed += TimerKillProcess_Elapsed;
            timerKillProcess.Interval = 5000;
            timerCheckAutoFold.Elapsed += timerCheckAutoFold_Elapsed;
            timerCheckAutoFold.Interval = 1500;
            timerCheckAutoUpdateWithSilence.Elapsed += timerCheckAutoUpdateWithSilence_Elapsed;
            timerCheckAutoUpdateWithSilence.Interval = 1000 * 60 * 60;
        }

        /// <summary>
        /// 定时清理指定第三方进程（按自动化设置执行）。
        /// </summary>
        private void TimerKillProcess_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                // 希沃相关： easinote swenserver RemoteProcess EasiNote.MediaHttpService smartnote.cloud EasiUpdate smartnote EasiUpdate3 EasiUpdate3Protect SeewoP2P CefSharp.BrowserSubprocess SeewoUploadService
                string arg = "/F";
                if (Settings.Automation.IsAutoKillPptService)
                {
                    Process[] processes = Process.GetProcessesByName("PPTService");
                    if (processes.Length > 0)
                    {
                        arg += " /IM PPTService.exe";
                    }
                    processes = Process.GetProcessesByName("SeewoIwbAssistant");
                    if (processes.Length > 0)
                    {
                        arg += " /IM SeewoIwbAssistant.exe" + " /IM Sia.Guard.exe";
                    }
                }
                if (Settings.Automation.IsAutoKillEasiNote)
                {
                    Process[] processes = Process.GetProcessesByName("EasiNote");
                    if (processes.Length > 0)
                    {
                        arg += " /IM EasiNote.exe";
                    }
                }
                if (arg != "/F")
                {
                    Process p = new Process();
                    p.StartInfo = new ProcessStartInfo("taskkill", arg);
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.Start();

                    if (arg.Contains("EasiNote"))
                    {
                        BtnSwitch_Click(null, null);
                        MessageBox.Show("“希沃白板 5”已自动关闭");
                    }
                }
            }
            catch { }
        }


        bool foldFloatingBarByUser = false, // 保持收纳操作不受自动收纳的控制
            unfoldFloatingBarByUser = false; // 允许用户在希沃软件内进行展开操作
        volatile string previousForegroundProcessName = "";

        /// <summary>
        /// 自动收纳/展开浮动栏逻辑轮询。
        /// </summary>
        private void timerCheckAutoFold_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (isFloatingBarChangingHideMode) return;
            try
            {
                string windowProcessName = ForegroundWindowInfo.ProcessName();
                string windowTitle = ForegroundWindowInfo.WindowTitle();
                bool isForegroundProcessChanged = !string.Equals(previousForegroundProcessName, windowProcessName, StringComparison.OrdinalIgnoreCase);
                previousForegroundProcessName = windowProcessName;
                if (isForegroundProcessChanged)
                {
                    unfoldFloatingBarByUser = false;
                }
                //LogHelper.WriteLogToFile("windowTitle | " + windowTitle + " | windowProcessName | " + windowProcessName);

                bool isCurrentProcessMatchedByCustomForegroundExe = IsProcessMatchedByAutoFoldList(windowProcessName);

                if (Settings.Automation.IsAutoFoldInEasiNote && windowProcessName == "EasiNote" // 希沃白板
                    && (!(windowTitle.Length == 0 && ForegroundWindowInfo.WindowRect().Height < 500) || !Settings.Automation.IsAutoFoldInEasiNoteIgnoreDesktopAnno)
                    || Settings.Automation.IsAutoFoldInEasiCamera && windowProcessName == "EasiCamera" // 希沃视频展台
                    || Settings.Automation.IsAutoFoldInEasiNote3C && windowProcessName == "EasiNote" // 希沃轻白板
                    || Settings.Automation.IsAutoFoldInSeewoPincoTeacher && (windowProcessName == "BoardService" || windowProcessName == "seewoPincoTeacher") // 希沃品课
                    || Settings.Automation.IsAutoFoldInHiteCamera && windowProcessName == "HiteCamera" // 鸿合视频展台
                    || Settings.Automation.IsAutoFoldInHiteTouchPro && windowProcessName == "HiteTouchPro" // 鸿合白板
                    || Settings.Automation.IsAutoFoldInWxBoardMain && windowProcessName == "WxBoardMain" // 文香白板
                    || Settings.Automation.IsAutoFoldInMSWhiteboard && (windowProcessName == "MicrosoftWhiteboard" || windowProcessName == "msedgewebview2") // 微软白板
                    || Settings.Automation.IsAutoFoldInOldZyBoard && // 中原旧白板
                    (WinTabWindowsChecker.IsWindowExisted("WhiteBoard - DrawingWindow")
                    || WinTabWindowsChecker.IsWindowExisted("InstantAnnotationWindow"))
                    || isCurrentProcessMatchedByCustomForegroundExe)
                {
                    if (!unfoldFloatingBarByUser && !isFloatingBarFolded)
                    {
                        FoldFloatingBar_Click(null, null);
                    }
                }
                else if (WinTabWindowsChecker.IsWindowExisted("幻灯片放映", false))
                { // 处于幻灯片放映状态
                    if (!Settings.Automation.IsAutoFoldInPPTSlideShow && isFloatingBarFolded && !foldFloatingBarByUser)
                    {
                        UnFoldFloatingBar_MouseUp(null, null);
                    }
                }
                else
                {
                    if (isFloatingBarFolded && !foldFloatingBarByUser)
                    {
                        UnFoldFloatingBar_MouseUp(null, null);
                    }
                    unfoldFloatingBarByUser = false;
                }
            }
            catch { }
        }

        private bool IsProcessMatchedByAutoFoldList(string windowProcessName)
        {
            if (string.IsNullOrWhiteSpace(windowProcessName)) return false;
            string autoFoldByForegroundExeNames = Settings.Automation.AutoFoldByForegroundExeNames;
            if (string.IsNullOrWhiteSpace(autoFoldByForegroundExeNames)) return false;

            string normalizedProcessName = NormalizeProcessName(windowProcessName);
            string[] processNameRules = autoFoldByForegroundExeNames.Split(',');

            for (int i = 0; i < processNameRules.Length; i++)
            {
                string rule = NormalizeProcessName(processNameRules[i]);
                if (rule.Length == 0) continue;
                if (string.Equals(normalizedProcessName, rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string NormalizeProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return "";
            processName = processName.Trim();
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }
            return processName;
        }

        /// <summary>
        /// 静默时段更新轮询：满足条件时触发静默安装。
        /// </summary>
        private void timerCheckAutoUpdateWithSilence_Elapsed(object sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if ((!Topmost) || (inkCanvas.Strokes.Count > 0)) return;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
                }
            });
            try
            {
                if (AutoUpdateWithSilenceTimeComboBox.CheckIsInSilencePeriod(Settings.Startup.AutoUpdateWithSilenceStartTime, Settings.Startup.AutoUpdateWithSilenceEndTime))
                {
                    AutoUpdateHelper.InstallNewVersionApp(AvailableLatestVersion, true);
                    timerCheckAutoUpdateWithSilence.Stop();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(ex.ToString(), LogHelper.LogType.Error);
            }
        }
    }
}
