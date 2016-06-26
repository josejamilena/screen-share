#region

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security;
using System.ServiceModel.Channels;
using System.Threading;
using Ionic.Zlib;

using UlteriusScreenShare.Desktop;
using UlteriusScreenShare.Websocket;
using UlteriusScreenShare.Websocket.Server;
using vtortola.WebSockets;

#endregion

namespace UlteriusScreenShare
{
    public class ScreenShareServer
    {
        private readonly ConnectionHandler _connectionHandler;
        private readonly SecureString _password;
        private readonly int _port;
        private readonly WebSocketEventListener _server;
        private readonly string _serverName;
        public ScreenShareServer(string serverName, SecureString password, IPAddress address, int port)
        {
            _port = port;
            _serverName = serverName;
            _password = password;

            var cancellation = new CancellationTokenSource();
            var endpoint = new IPEndPoint(address, _port);
            _server = new WebSocketEventListener(endpoint, new WebSocketListenerOptions
            {
                PingTimeout = TimeSpan.FromSeconds(5),
                NegotiationTimeout = TimeSpan.FromSeconds(5),
                ParallelNegotiations = 16,
                NegotiationQueueCapacity = 256,
                TcpBacklog = 1000,
                BufferManager = BufferManager.CreateBufferManager((8192 + 1024) * 1000, 8192 + 1024)
            });
            _connectionHandler = new ConnectionHandler(_serverName, _password, _server);
          
        }

        public bool PortAvailable()
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
            return tcpConnInfoArray.All(endpoint => endpoint.Port != _port);
        }

        public bool Start()
        {
            try
            {
                if (PortAvailable())
                {
                    _server.Start();
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Stop()
        {
            try
            {
                if (!PortAvailable())
                {
                    _server.Stop();
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}