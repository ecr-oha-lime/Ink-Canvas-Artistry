using Ink_Canvas.Windows;
using System;
using System.Windows;
using System.Windows.Interop;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        private ScreenMagnifierWindow magnifierWindow;

        private void SymbolIconMagnifier_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                return;
            }

            if (magnifierWindow == null)
            {
                OpenMagnifierWindow();
            }
            else
            {
                CloseMagnifierWindow();
            }
        }

        private void OpenMagnifierWindow()
        {
            if (magnifierWindow != null) return;

            IntPtr mainHandle = new WindowInteropHelper(this).Handle;
            magnifierWindow = new ScreenMagnifierWindow(mainHandle)
            {
                Owner = this
            };
            magnifierWindow.RequestClose += MagnifierWindow_RequestClose;
            magnifierWindow.Show();
        }

        private void MagnifierWindow_RequestClose(object sender, EventArgs e)
        {
            if (magnifierWindow != null)
            {
                magnifierWindow.RequestClose -= MagnifierWindow_RequestClose;
            }

            magnifierWindow = null;
        }

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

        private void UpdateMagnifierToolButtonVisibility()
        {
            if (BtnToolsMagnifier == null) return;
            BtnToolsMagnifier.Visibility = currentMode == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
