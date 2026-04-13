using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 保存全屏截图到自动保存目录。
        /// </summary>
        private void SaveScreenshot(bool isHideNotification, string fileName = null)
        {
            var bitmap = GetScreenshotBitmap();
            string savePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - Screenshots";
            if (fileName == null) fileName = DateTime.Now.ToString("u").Replace(":", "-");
            if (Settings.Automation.IsSaveScreenshotsInDateFolders)
            {
                savePath += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
            }
            savePath += @"\" + fileName + ".png";
            if (!Directory.Exists(Path.GetDirectoryName(savePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            }
            bitmap.Save(savePath, ImageFormat.Png);
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
            {
                SaveInkCanvasFile(false, false);
            }
            if (!isHideNotification)
            {
                ShowNotificationAsync("截图成功保存至 " + savePath);
            }
        }

        /// <summary>
        /// 保存全屏截图到桌面。
        /// </summary>
        private void SaveScreenShotToDesktop()
        {
            SaveBitmapToDesktop(GetScreenshotBitmap(), true);
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot) SaveInkCanvasFile(false, false);
        }

        /// <summary>
        /// 将指定位图保存到桌面。
        /// </summary>
        private string SaveBitmapToDesktop(Bitmap bitmap, bool showNotification)
        {
            string savePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string fileName = DateTime.Now.ToString("u").Replace(':', '-') + ".png";
            string fullPath = Path.Combine(savePath, fileName);
            bitmap.Save(fullPath, ImageFormat.Png);
            if (showNotification)
            {
                ShowNotificationAsync("截图成功保存至【桌面" + @"\" + fileName + "】");
            }
            return fullPath;
        }

        /// <summary>
        /// 保存 PPT 相关截图到专用目录。
        /// </summary>
        private void SavePPTScreenshot(string fileName)
        {
            var bitmap = GetScreenshotBitmap();
            string savePath = Settings.Automation.AutoSavedStrokesLocation + @"\Auto Saved - PPT Screenshots";
            if (Settings.Automation.IsSaveScreenshotsInDateFolders)
            {
                savePath += @"\" + DateTime.Now.ToString("yyyy-MM-dd");
            }
            if (fileName == null) fileName = DateTime.Now.ToString("u").Replace(":", "-");
            savePath += @"\" + fileName + ".png";
            if (!Directory.Exists(Path.GetDirectoryName(savePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(savePath));
            }
            bitmap.Save(savePath, ImageFormat.Png);
            if (Settings.Automation.IsAutoSaveStrokesAtScreenshot)
            {
                SaveInkCanvasFile(false, false);
            }
        }

        /// <summary>
        /// 捕获当前虚拟屏幕并返回位图对象。
        /// </summary>
        private Bitmap GetScreenshotBitmap()
        {
            Rectangle rc = System.Windows.Forms.SystemInformation.VirtualScreen;
            var bitmap = new Bitmap(rc.Width, rc.Height, PixelFormat.Format32bppArgb);
            using (Graphics memoryGrahics = Graphics.FromImage(bitmap))
            {
                memoryGrahics.CopyFromScreen(rc.X, rc.Y, 0, 0, rc.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }
    }
}
