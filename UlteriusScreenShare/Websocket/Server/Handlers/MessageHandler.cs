#region

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using UlteriusScreenShare.Security;
using vtortola.WebSockets;

#endregion

namespace UlteriusScreenShare.Websocket.Server.Handlers
{
    internal class MessageHandler
    {
        public static MessageQueueManager MessageQueueManager = new MessageQueueManager();
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
                        var packet = new Packet(authClient, encryptedData, Packet.MessageType.Binary);
                       MessageQueueManager.SendQueue.Add(packet);
                        // PushBinary(authClient.Client, encryptedData);
                        return;
                    }
                }
            }
            catch (Exception e)
            {
              Console.WriteLine(e.StackTrace);
            }
            if (authClient != null)
            {
                var packet = new Packet(authClient, json, Packet.MessageType.Text);
               MessageQueueManager.SendQueue.Add(packet);
            }
        }
    }
}