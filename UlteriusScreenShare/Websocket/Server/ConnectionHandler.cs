#region

using System;
using System.Collections.Concurrent;
using System.Security;
using UlteriusScreenShare.Desktop;
using UlteriusScreenShare.Security;
using vtortola.WebSockets;

#endregion

namespace UlteriusScreenShare.Websocket.Server
{
    internal class ConnectionHandler
    {
        public static ConcurrentDictionary<string, AuthClient> Clients;
        private readonly CommandHandler _commandHandler;
        private readonly SecureString _password;
        private readonly WebSocketEventListener _server;
        private string _serverName;
       

        public ConnectionHandler(string serverName, SecureString password, WebSocketEventListener server)
        {
            _serverName = serverName;
            _password = password;
            Clients = new ConcurrentDictionary<string, AuthClient>();
            _commandHandler = new CommandHandler(this);
            _server = server;
            _server.OnConnect += HandleConnect;
            _server.OnDisconnect += HandleDisconnect;
            _server.OnPlainTextMessage += HandlePlainTextMessage;
            _server.OnEncryptedMessage += HandleEncryptedMessage;
            _server.OnError += HandleError;
        }

        private void HandleEncryptedMessage(WebSocket websocket, byte[] message)
        {
            try
            {
                AuthClient client;

                if (Clients.TryGetValue(websocket.GetHashCode().ToString(), out client))
                {
                    if (message != null && message.Length > 0)
                    {
                        var packet = MessageHandler.DecryptMessage(message, client);
                        if (packet != null)
                        {
                            if (!client.Authenticated && client.AesShook)
                            {
                                AuthenticationHandler.Authenticate(_password.ConvertToUnsecureString(), packet, client);
                            }
                            else if (client.Authenticated && client.AesShook)
                            {
                                _commandHandler.ProcessCommand(client, packet);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                

            }
        }


        private void HandleError(WebSocket websocket, Exception error)
        {
            Console.WriteLine($"Error occured on {websocket.GetHashCode()}: {error.Message}|{error.StackTrace}");
        }

        private void HandlePlainTextMessage(WebSocket websocket, string message)
        {
            try
            {
                AuthClient client;
                if (Clients.TryGetValue(websocket.GetHashCode().ToString(), out client))
                {
                    if (!client.AesShook)
                    {
                        AuthenticationHandler.AesHandshake(message, client);
                    }
                }
            }
            catch (Exception)
            {

                
            }
        }

        private void HandleDisconnect(WebSocket websocket)
        {
            AuthClient client;
            if (Clients.TryRemove(websocket.GetHashCode().ToString(), out client))
            {
                Console.WriteLine("Client removed");
            }
        }

        public void Handshake(AuthClient authClient)
        {
            var handshake = new
            {
                message = "Handshake established",
                publicKey = Rsa.SecureStringToString(authClient.PublicKey)
            };
            MessageHandler.SendMessage("connectedToScreenShare", handshake, authClient);
        }

        private void HandleConnect(WebSocket websocket)
        {
            var authClient = new AuthClient(websocket);
            var rsa = new Rsa();
            rsa.GenerateKeyPairs();
            authClient.PublicKey = rsa.PublicKey;
            authClient.PrivateKey = rsa.PrivateKey;
            if (Clients.TryAdd(websocket.GetHashCode().ToString(), authClient))
            {
                Handshake(authClient);
                Console.WriteLine("New Client Connected");
            }
        }
    }
}