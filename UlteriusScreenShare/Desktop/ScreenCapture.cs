#region

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Ionic.Zlib;
using UlteriusScreenShare.Websocket.Server;
using UlteriusScreenShare.Websocket.Server.Handlers;
using UlteriusScreenShare.Win32Api;

#endregion

namespace UlteriusScreenShare.Desktop
{
    //Big thanks to Bob Cravens
    internal class ScreenCapture
    {
        private static int _numByteFullScreen = 1;


        private Graphics _graphics;
        private Bitmap _newBitmap = new Bitmap(1, 1);
        private Bitmap _prevBitmap;


        public ScreenCapture()
        {
            var junk = new Bitmap(10, 10);
            _graphics = Graphics.FromImage(junk);

            Thread t = new Thread(ScreenService);
            t.Start();
        }

        public double PercentOfImage { get; set; }

        public bool IsWindows7 => Environment.OSVersion.Version.Major == 6 &
                                  Environment.OSVersion.Version.Minor == 1;

        public static byte[] PackScreenCaptureData(Image image, Rectangle bounds)
        {
            byte[] results;
            using (var screenStream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(screenStream))
                {
                    //write the id of the frame
                   binaryWriter.Write(Guid.NewGuid().ToByteArray());
                    //write the x and y coords of the 
                    binaryWriter.Write(bounds.X);
                    binaryWriter.Write(bounds.Y);
                    //write the rect data
                    binaryWriter.Write(bounds.Top);
                    binaryWriter.Write(bounds.Bottom);
                    binaryWriter.Write(bounds.Left);
                    binaryWriter.Write(bounds.Right);
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, ImageFormat.Jpeg);

                        var imgData = ms.ToArray();
                        var compressed = ZlibStream.CompressBuffer(imgData);
                        //write the image
                        binaryWriter.Write(compressed);
                    }
                }
                results = screenStream.ToArray();
            }
            return results;
        }


        private void ScreenService()
        {
            var bounds = Rectangle.Empty;
            while (true)
            {
                var clients = ConnectionHandler.Clients;
                if (clients.Count > 0)
                {
                    var image = LocalScreen(ref bounds);
                    if (_numByteFullScreen == 1)
                    {
                        // Initialize the screen size (used for performance metrics)
                        //
                        _numByteFullScreen = bounds.Width * bounds.Height * 4;
                    }
                    if (bounds != Rectangle.Empty && image != null)
                    {
                        var data = PackScreenCaptureData(image, bounds);


                        if (data != null && data.Length > 0)
                        {
                            foreach (var client in clients)
                            {
                                // var packet = new Packet(client.Value, data, Packet.MessageType.Binary);
                               // MessageHandler.MessageQueueManager.SendQueue.Add(packet);
                                if (client.Value.AesShook && client.Value.Authenticated)
                                {
                                    var encryptedData = MessageHandler.EncryptFrame(data, client.Value);
                                    if (encryptedData.Length == 0)
                                    {
                                        return;
                                    }
                                    var packet = new Packet(client.Value, encryptedData, Packet.MessageType.Binary);
                                    MessageHandler.MessageQueueManager.SendQueue.Add(packet);
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Sleeping no clients");
                    Thread.Sleep(5000);
                }
            }
        }


        public Bitmap LocalScreen(ref Rectangle bounds)
        {
            Bitmap diff = null;

            // Capture a new screenshot.
            //
            lock (_newBitmap)
            {
                _newBitmap = CaptureDesktop();

                // If we have a previous screenshot, only send back
                //	a subset that is the minimum rectangular area
                //	that encompasses all the changed pixels.
                //
                if (_prevBitmap != null)
                {
                    // Get the bounding box.
                    //
                    bounds = GetBoundingBoxForChanges();
                    if (bounds == Rectangle.Empty)
                    {
                        // Nothing has changed.
                        //
                        PercentOfImage = 0.0;
                    }
                    else
                    {
                        // Get the minimum rectangular area
                        //
                        diff = new Bitmap(bounds.Width, bounds.Height);
                        _graphics = Graphics.FromImage(diff);
                        _graphics.DrawImage(_newBitmap, 0, 0, bounds, GraphicsUnit.Pixel);

                        // Set the current bitmap as the previous to prepare
                        //	for the next screen capture.
                        //
                        _prevBitmap = _newBitmap;

                        lock (_newBitmap)
                        {
                            PercentOfImage = 100.0 * (diff.Height * diff.Width) / (_newBitmap.Height * _newBitmap.Width);
                        }
                    }
                }
                // We don't have a previous screen capture. Therefore
                //	we need to send back the whole screen this time.
                //
                else
                {
                    // Set the previous bitmap to the current to prepare
                    //	for the next screen capture.
                    //
                    _prevBitmap = _newBitmap;
                    diff = _newBitmap;

                    // Create a bounding rectangle.
                    //
                    bounds = new Rectangle(0, 0, _newBitmap.Width, _newBitmap.Height);

                    PercentOfImage = 100.0;
                }
            }
            return diff;
        }

        private Rectangle GetBoundingBoxForChanges()
        {
            // The search algorithm starts by looking
            //	for the top and left bounds. The search
            //	starts in the upper-left corner and scans
            //	left to right and then top to bottom. It uses
            //	an adaptive approach on the pixels it
            //	searches. Another pass is looks for the
            //	lower and right bounds. The search starts
            //	in the lower-right corner and scans right
            //	to left and then bottom to top. Again, an
            //	adaptive approach on the search area is used.
            //

            // Notice: The GetPixel member of the Bitmap class
            //	is too slow for this purpose. This is a good
            //	case of using unsafe code to access pointers
            //	to increase the speed.
            //

            // Validate the images are the same shape and type.
            //
            if (_prevBitmap.Width != _newBitmap.Width ||
                _prevBitmap.Height != _newBitmap.Height ||
                _prevBitmap.PixelFormat != _newBitmap.PixelFormat)
            {
                // Not the same shape...can't do the search.
                //
                return Rectangle.Empty;
            }

            // Init the search parameters.
            //
            var width = _newBitmap.Width;
            var height = _newBitmap.Height;
            var left = width;
            var right = 0;
            var top = height;
            var bottom = 0;

            BitmapData newScreenData = null;
            BitmapData previousScreenData = null;
            try
            {
                // Lock the bits into memory.
                //
                newScreenData = _newBitmap.LockBits(
                    new Rectangle(0, 0, _newBitmap.Width, _newBitmap.Height),
                    ImageLockMode.ReadOnly, _newBitmap.PixelFormat);
                previousScreenData = _prevBitmap.LockBits(
                    new Rectangle(0, 0, _prevBitmap.Width, _prevBitmap.Height),
                    ImageLockMode.ReadOnly, _prevBitmap.PixelFormat);

                // The images are ARGB (4 bytes)
                //
                const int numBytesPerPixel = 4;

                // Get the number of integers (4 bytes) in each row
                //	of the image.
                //
                var strideNew = newScreenData.Stride / numBytesPerPixel;
                var stridePrev = previousScreenData.Stride / numBytesPerPixel;

                // Get a pointer to the first pixel.
                //
                // Notice: Another speed up implemented is that I don't
                //	need the ARGB elements. I am only trying to detect
                //	change. So this algorithm reads the 4 bytes as an
                //	integer and compares the two numbers.
                //
                var scanNew0 = newScreenData.Scan0;
                var scanPrev0 = previousScreenData.Scan0;

                // Enter the unsafe code.
                //
                unsafe
                {
                    // Cast the safe pointers into unsafe pointers.
                    //
                    var pNew = (int*)(void*)scanNew0;
                    var pPrev = (int*)(void*)scanPrev0;

                    // First Pass - Find the left and top bounds
                    //	of the minimum bounding rectangle. Adapt the
                    //	number of pixels scanned from left to right so
                    //	we only scan up to the current bound. We also
                    //	initialize the bottom & right. This helps optimize
                    //	the second pass.
                    //
                    // For all rows of pixels (top to bottom)
                    //
                    for (var y = 0; y < _newBitmap.Height; ++y)
                    {
                        // For pixels up to the current bound (left to right)
                        //
                        for (var x = 0; x < left; ++x)
                        {
                            // Use pointer arithmetic to index the
                            //	next pixel in this row.
                            //
                            if ((pNew + x)[0] != (pPrev + x)[0])
                            {
                                // Found a change.
                                //
                                if (x < left)
                                {
                                    left = x;
                                }
                                if (x > right)
                                {
                                    right = x;
                                }
                                if (y < top)
                                {
                                    top = y;
                                }
                                if (y > bottom)
                                {
                                    bottom = y;
                                }
                            }
                        }

                        // Move the pointers to the next row.
                        //
                        pNew += strideNew;
                        pPrev += stridePrev;
                    }

                    // If we did not find any changed pixels
                    //	then no need to do a second pass.
                    //
                    if (left != width)
                    {
                        // Second Pass - The first pass found at
                        //	least one different pixel and has set
                        //	the left & top bounds. In addition, the
                        //	right & bottom bounds have been initialized.
                        //	Adapt the number of pixels scanned from right
                        //	to left so we only scan up to the current bound.
                        //	In addition, there is no need to scan past
                        //	the top bound.
                        //

                        // Set the pointers to the first element of the
                        //	bottom row.
                        //
                        pNew = (int*)(void*)scanNew0;
                        pPrev = (int*)(void*)scanPrev0;
                        pNew += (_newBitmap.Height - 1) * strideNew;
                        pPrev += (_prevBitmap.Height - 1) * stridePrev;

                        // For each row (bottom to top)
                        //
                        for (var y = _newBitmap.Height - 1; y > top; y--)
                        {
                            // For each column (right to left)
                            //
                            for (var x = _newBitmap.Width - 1; x > right; x--)
                            {
                                // Use pointer arithmetic to index the
                                //	next pixel in this row.
                                //
                                if ((pNew + x)[0] != (pPrev + x)[0])
                                {
                                    // Found a change.
                                    //
                                    if (x > right)
                                    {
                                        right = x;
                                    }
                                    if (y > bottom)
                                    {
                                        bottom = y;
                                    }
                                }
                            }

                            // Move up one row.
                            //
                            pNew -= strideNew;
                            pPrev -= stridePrev;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Do something with this info.
            }
            finally
            {
                // Unlock the bits of the image.
                //
                if (newScreenData != null)
                {
                    _newBitmap.UnlockBits(newScreenData);
                }
                if (previousScreenData != null)
                {
                    _prevBitmap.UnlockBits(previousScreenData);
                }
            }

            // Validate we found a bounding box. If not
            //	return an empty rectangle.
            //
            var diffImgWidth = right - left + 1;
            var diffImgHeight = bottom - top + 1;
            if (diffImgHeight < 0 || diffImgWidth < 0)
            {
                // Nothing changed
                return Rectangle.Empty;
            }

            // Return the bounding box.
            //
            return new Rectangle(left, top, diffImgWidth, diffImgHeight);
        }
        public struct Size
        {
            public int Width;
            public int Height;
        }
        public static Bitmap CaptureDesktop()
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
                Console.WriteLine(ex.Message);
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
    }
}