#region

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using UlteriusScreenShare.Security;
using vtortola.WebSockets;

#endregion

namespace UlteriusScreenShare.Websocket.Server
{
    internal class MessageHandler
    {
        public static string DecryptMessage(byte[] message, AuthClient client)
        {
            if (client != null)
            {
                var keybytes = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesKey));
                var iv = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesIv));
                
                return UAes.Decrypt(message, keybytes, iv);
            }
            return null;
        }

        public static byte[] EncryptFrame(byte[] data, AuthClient client)
        {
            if (client == null) return null;
            var keyBytes = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesKey));
            var keyIv = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesIv));
            return UAes.EncryptFile(data, keyBytes, keyIv);
        }

        public static byte[] DecryptFrame(byte[] data, AuthClient client)
        {
            if (client == null) return null;
            var keyBytes = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesKey));
            var keyIv = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(client.AesIv));
            return UAes.DecryptFile(data, keyBytes, keyIv);
        }


        public static void SendMessage(string endpoint, object data, AuthClient authClient)
        {
            var serializer = new JavaScriptSerializer {MaxJsonLength = int.MaxValue};
            var json = serializer.Serialize(new
            {
                endpoint,
                results = data
            });
            try
            {
                if (authClient != null)
                {
                    if (authClient.AesShook)
                    {
                        var keyBytes = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(authClient.AesKey));
                        var keyIv = Encoding.UTF8.GetBytes(Rsa.SecureStringToString(authClient.AesIv));
                        var encryptedData = UAes.Encrypt(json, keyBytes, keyIv);
                        PushBinary(authClient.Client, encryptedData);
                        Console.WriteLine("Message Encrypted");
                        return;
                    }
                }
            }
            catch (Exception)
            {
                //TODO Handle
            }
            Push(authClient.Client, json);
        }

        public static void PushBinary(WebSocket client, byte[] data)
        {
            try
            {
                using (var messageWriter = client.CreateMessageWriter(WebSocketMessageType.Binary))
                {
                    using (var stream = new MemoryStream(data))
                    {
                        stream.CopyTo(messageWriter);
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private static void Push(WebSocket client, string json)
        {
            try
            {
                client.WriteStringAsync(json, CancellationToken.None);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}