﻿#region

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ionic.Zlib;
using UlteriusScreenShare.Websocket.Server;
using UlteriusScreenShare.Websocket.Server.Handlers;

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

            var t = new Task(ScreenService);
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


        private async void ScreenService()
        {
            var bounds = Rectangle.Empty;
            while (true)
            {
                try
                {
                    var clients = ConnectionHandler.Clients;
                    if (clients.Count > 0)
                    {
                        var image = LocalScreen(ref bounds);
                        if (_numByteFullScreen == 1)
                        {
                            // Initialize the screen size (used for performance metrics)
                            //
                            _numByteFullScreen = bounds.Width*bounds.Height*4;
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
                        // Console.WriteLine("Sleeping no clients");
                        await Task.Delay(5000);
                    }
                }
                catch (Exception e)
                {
                    await Task.Delay(5000);
                    Console.WriteLine(e.Message + " " + e.StackTrace);
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
                var image = CaptureDesktop();
                if (image == null)
                {
                    return null;
                }
                _newBitmap = image;

                // If we have a previous screenshot, only send back
                //	a subset that is the minimum rectangular area
                //	that encompasses all the changed pixels.
                //
                if (_prevBitmap != null)
                {
                    // Get the bounding box.
                    //
                    bounds = GetBoundingBoxForChanges(ref _prevBitmap, ref _newBitmap);
                    if (bounds != Rectangle.Empty)
                    {
                        // Get the minimum rectangular area
                        //
                        //diff = new Bitmap(bounds.Width, bounds.Height);
                        diff = _newBitmap.Clone(bounds, _newBitmap.PixelFormat);

                        // Set the current bitmap as the previous to prepare
                        //	for the next screen capture.
                        //
                        _prevBitmap = _newBitmap;
                    }
                }
                // We don't have a previous screen capture. Therefore
                //	we need to send back the whole screen this time.
                //
                else
                {
                    // Create a bounding rectangle.
                    //
                    bounds = new Rectangle(0, 0, _newBitmap.Width, _newBitmap.Height);

                    // Set the previous bitmap to the current to prepare
                    //	for the next screen capture.
                    //
                    _prevBitmap = _newBitmap;
                    diff = _newBitmap.Clone(bounds, _newBitmap.PixelFormat);
                }
            }
            return diff;
        }

        private Rectangle GetBoundingBoxForChanges(ref Bitmap _prevBitmap, ref Bitmap _newBitmap)
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

            BitmapData bmNewData = null;
            BitmapData bmPrevData = null;
            try
            {
                // Lock the bits into memory.
                //
                bmNewData = _newBitmap.LockBits(
                    new Rectangle(0, 0, _newBitmap.Width, _newBitmap.Height),
                    ImageLockMode.ReadOnly, _newBitmap.PixelFormat);
                bmPrevData = _prevBitmap.LockBits(
                    new Rectangle(0, 0, _prevBitmap.Width, _prevBitmap.Height),
                    ImageLockMode.ReadOnly, _prevBitmap.PixelFormat);

                // The images are ARGB (4 bytes)
                //
                const int numBytesPerPixel = 4;

                // Get the number of integers (4 bytes) in each row
                //	of the image.
                //
                var strideNew = bmNewData.Stride/numBytesPerPixel;
                var stridePrev = bmPrevData.Stride/numBytesPerPixel;

                // Get a pointer to the first pixel.
                //
                // Notice: Another speed up implemented is that I don't
                //	need the ARGB elements. I am only trying to detect
                //	change. So this algorithm reads the 4 bytes as an
                //	integer and compares the two numbers.
                //
                var scanNew0 = bmNewData.Scan0;
                var scanPrev0 = bmPrevData.Scan0;

                // Enter the unsafe code.
                //
                unsafe
                {
                    // Cast the safe pointers into unsafe pointers.
                    //

                    var pNew = (int*) scanNew0.ToPointer();
                    var pPrev = (int*) scanPrev0.ToPointer();
                    for (var y = 0; y < _newBitmap.Height; ++y)
                    {
                        // For pixels up to the current bound (left to right)
                        //
                        for (var x = 0; x < left; ++x)
                        {
                            // Use pointer arithmetic to index the
                            //	next pixel in this row.
                            //
                            var test1 = (pNew + x)[0];
                            var test2 = (pPrev + x)[0];
                            var b1 = test1 & 0xff;
                            var g1 = (test1 & 0xff00) >> 8;
                            var r1 = (test1 & 0xff0000) >> 16;
                            var a1 = (test1 & 0xff000000) >> 24;

                            var b2 = test2 & 0xff;
                            var g2 = (test2 & 0xff00) >> 8;
                            var r2 = (test2 & 0xff0000) >> 16;
                            var a2 = (test2 & 0xff000000) >> 24;
                            if (b1 != b2 || g1 != g2 || r1 != r2 || a1 != a2)
                            {
                                if (left > x)
                                    left = x;
                                if (top > y)
                                    top = y;
                            }
                        }

                        // Move the pointers to the next row.
                        //
                        pNew += strideNew;
                        pPrev += stridePrev;
                    }

                    pNew = (int*) scanNew0.ToPointer();
                    pPrev = (int*) scanPrev0.ToPointer();
                    pNew += (_newBitmap.Height - 1)*strideNew;
                    pPrev += (_prevBitmap.Height - 1)*stridePrev;

                    for (var y = _newBitmap.Height - 1; y > top; y--)
                    {
                        for (var x = _newBitmap.Width - 1; x > left; x--)
                        {
                            var test1 = (pNew + x)[0];
                            var test2 = (pPrev + x)[0];
                            var b1 = test1 & 0xff;
                            var g1 = (test1 & 0xff00) >> 8;
                            var r1 = (test1 & 0xff0000) >> 16;
                            var a1 = (test1 & 0xff000000) >> 24;

                            var b2 = test2 & 0xff;
                            var g2 = (test2 & 0xff00) >> 8;
                            var r2 = (test2 & 0xff0000) >> 16;
                            var a2 = (test2 & 0xff000000) >> 24;
                            if (b1 != b2 || g1 != g2 || r1 != r2 || a1 != a2)
                            {
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

                        pNew -= strideNew;
                        pPrev -= stridePrev;
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
                if (bmNewData != null)
                {
                    _newBitmap.UnlockBits(bmNewData);
                }
                if (bmPrevData != null)
                {
                    _prevBitmap.UnlockBits(bmPrevData);
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


        public static Bitmap CaptureDesktop()
        {
            try
            {
                var desktopBmp = new Bitmap(
                      Screen.PrimaryScreen.Bounds.Width,
                      Screen.PrimaryScreen.Bounds.Height);

                var g = Graphics.FromImage(desktopBmp);

                g.CopyFromScreen(0, 0, 0, 0,
                    new System.Drawing.Size(
                        Screen.PrimaryScreen.Bounds.Width,
                        Screen.PrimaryScreen.Bounds.Height));
                g.Dispose();
                return desktopBmp;
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
            }
            return null;
        }

        public struct Size
        {
            public int Width;
            public int Height;
        }
    }
}