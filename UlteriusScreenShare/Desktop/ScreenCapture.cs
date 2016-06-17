#region

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using UlteriusScreenShare.Websocket.Server;
using UlteriusScreenShare.Win32Api;

#endregion

namespace UlteriusScreenShare.Desktop
{
    internal class ScreenCapture
    {
        private readonly ImageCodecInfo _encoder = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        private readonly EncoderParameters _encParams = new EncoderParameters(1)
        {
            Param = {[0] = new EncoderParameter(Encoder.Quality, 75L)}
        };

        public Bitmap LastFrame;

        public ScreenCapture()
        {
            var backgroundWorker = new BackgroundWorker
            {
                WorkerSupportsCancellation = false
            };
            backgroundWorker.DoWork += BackgroundWorkerOnDoWork;
            backgroundWorker.RunWorkerAsync();
        }

        public bool IsWindows7 => Environment.OSVersion.Version.Major == 6 &
                                  Environment.OSVersion.Version.Minor == 1;

        private void BackgroundWorkerOnDoWork(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker) sender;
            while (!worker.CancellationPending)
            {
                var clients = ConnectionHandler.Clients;
                if (clients.Count > 0)
                {
                    var frame = CaptureDesktop();
                    if (frame != null)
                    {
                        LastFrame = frame;
                        var now = DateTime.Now;
                        Console.WriteLine("Frame Taken " + now);

                        foreach (var client in clients)
                        {
                            //send the frame to each client
                            if (client.Value.AesShook && client.Value.Authenticated)
                            {
                                using (var frameStream = new MemoryStream())
                                {
                                    frame.Save(frameStream, _encoder, _encParams);
                                    var encryptedData = MessageHandler.EncryptFrame(frameStream.ToArray(), client.Value);
                                    if (encryptedData.Length == 0)
                                    {
                                        return;
                                    }
                                    MessageHandler.PushBinary(client.Value.Client, encryptedData);
                                    now = DateTime.Now;
                                    Console.WriteLine("Frame Sent " + now);
                                }
                            }
                        }
                    }
                }
            }
        }

        private Bitmap CaptureDesktop()
        {
            var desktopContextHeight = IntPtr.Zero;
            Bitmap screenImage = null;
            try
            {
                desktopContextHeight = Win32.GetDC(Win32.GetDesktopWindow());
                var gdiDesktopContext = Gdi.CreateCompatibleDC(desktopContextHeight);
                Size screenSize;
                screenSize.Width = Win32.GetSystemMetrics(0);
                screenSize.Height = Win32.GetSystemMetrics(1);
                var gdiBitmap = Gdi.CreateCompatibleBitmap(desktopContextHeight, screenSize.Width, screenSize.Height);
                if (gdiBitmap != IntPtr.Zero)
                {
                    var oldGdi = Gdi.SelectObject(gdiDesktopContext, gdiBitmap);
                    Gdi.BitBlt(gdiDesktopContext, 0, 0, screenSize.Width, screenSize.Height, desktopContextHeight,
                        0, 0, Gdi.Srccopy);
                    Gdi.SelectObject(gdiDesktopContext, oldGdi);
                    screenImage = Image.FromHbitmap(gdiBitmap);
                    Gdi.DeleteObject(gdiBitmap);
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message, ex);
            }
            finally
            {
                if (desktopContextHeight != IntPtr.Zero)
                {
                    Win32.ReleaseDC(Win32.GetDesktopWindow(), desktopContextHeight);
                }
            }
            return screenImage;
        }


        public struct Size
        {
            public int Width;
            public int Height;
        }
    }
}