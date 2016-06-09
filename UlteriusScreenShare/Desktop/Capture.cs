#region

using System;
using System.Drawing;
using UlteriusScreenShare.Win32Api;

#endregion

namespace UlteriusScreenShare.Desktop
{
    internal class Capture
    {
        public static bool IsWindows7 => Environment.OSVersion.Version.Major == 6 &
                                         Environment.OSVersion.Version.Minor == 1;

        public static Bitmap CaptureDesktop()
        {
            SIZE size;
            var hDc = Win32.GetDC(Win32.GetDesktopWindow());
            var hMemDc = Gdi.CreateCompatibleDC(hDc);

            size.Cx = Win32.GetSystemMetrics
                (Win32.SmCxscreen);

            size.Cy = Win32.GetSystemMetrics
                (Win32.SmCyscreen);

            var hBitmap = Gdi.CreateCompatibleBitmap(hDc, size.Cx/2, size.Cy);

            if (hBitmap == IntPtr.Zero) return null;
            var hOld = Gdi.SelectObject
                (hMemDc, hBitmap);

            Gdi.BitBlt(hMemDc, 0, 0, size.Cx/2, size.Cy, hDc,
                0, 0, Gdi.Srccopy);

            Gdi.SelectObject(hMemDc, hOld);
            Gdi.DeleteDC(hMemDc);
            Win32.ReleaseDC(Win32.GetDesktopWindow(), hDc);
            var frame = Image.FromHbitmap(hBitmap);
            Gdi.DeleteObject(hBitmap);
            GC.Collect();
            return frame;
        }


        public struct SIZE
        {
            public int Cx;
            public int Cy;
        }
    }
}