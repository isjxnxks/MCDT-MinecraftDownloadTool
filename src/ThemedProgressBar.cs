using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCDT
{
    public sealed class ThemedProgressBar : ProgressBar, IThemeAwareControl
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyThemeColors();
        }

        public void ApplyThemeColors()
        {
            if (!IsHandleCreated)
                return;
            try
            {
                SetWindowTheme(Handle, "", "");
                SendMessage(Handle, 0x2001, IntPtr.Zero,
                    new IntPtr(ColorTranslator.ToWin32(Theme.SurfaceAlt)));
                SendMessage(Handle, 0x0409, IntPtr.Zero,
                    new IntPtr(ColorTranslator.ToWin32(Theme.Accent)));
            }
            catch
            {
            }
        }
    }
}
