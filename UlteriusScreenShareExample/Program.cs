using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using UlteriusScreenShare;
using MapFlags = SharpDX.DXGI.MapFlags;
using Resource = SharpDX.DXGI.Resource;

namespace UlteriusScreenShareExample
{
    class Program
    {
        public static SecureString ConvertToSecureString(string password)
        {
            if (password == null)
                throw new ArgumentNullException("password");

            unsafe
            {
                fixed (char* passwordChars = password)
                {
                    var securePassword = new SecureString(passwordChars, password.Length);
                    securePassword.MakeReadOnly();
                    return securePassword;
                }
            }
        }
        private static bool _quitFlag;
        static void Main(string[] args)
        {
           
            Console.CancelKeyPress += delegate { _quitFlag = true; };
            var server = new ScreenShareServer("Server", ConvertToSecureString("pass"), IPAddress.Any, 5555);
            server.Start();
            while (!_quitFlag)
            {
                Thread.Sleep(1);
            }
        }


    }
}
