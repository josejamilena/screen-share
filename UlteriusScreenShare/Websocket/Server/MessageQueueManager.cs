#region

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using vtortola.WebSockets;

#endregion

namespace UlteriusScreenShare.Websocket.Server
{
    public class MessageQueueManager
    {
        public BlockingCollection<Packet> SendQueue = new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());

        public MessageQueueManager()
        {
            var backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += BackgroundWorkerOnDoWork;
            backgroundWorker.RunWorkerAsync();
        }

        private async void BackgroundWorkerOnDoWork(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                var packet = SendQueue.Take();
                if (packet.Type == Packet.MessageType.Binary)
                {
                    await SendBinaryPacket(packet);
                }
                else if (packet.Type == Packet.MessageType.Text)
                {
                    await SendJsonPacket(packet);
                }
                Console.WriteLine($"Packet Sent: {DateTime.Now}");
            }
        }

        private async Task SendJsonPacket(Packet packet)
        {
            var json = packet.Json;
            var client = packet.AuthClient.Client;
            if (client.IsConnected)
            {
                try
                {
                    using (var msg = client.CreateMessageWriter(WebSocketMessageType.Text))
                    using (var writer = new StreamWriter(msg, Encoding.UTF8))
                    {
                        await writer.WriteAsync(json);
                        await writer.FlushAsync();
                    }
                }
                catch (Exception e)
                {

                    Console.WriteLine(e.Message);
                }
            }
        }

        private async Task SendBinaryPacket(Packet packet)
        {
            var authClient = packet.AuthClient;
            if (authClient != null && authClient.Client.IsConnected)
            {
                try
                {
                    using (var memoryStream = new MemoryStream(packet.Data))
                    using (var messageWriter = authClient.Client.CreateMessageWriter(WebSocketMessageType.Binary))
                        await memoryStream.CopyToAsync(messageWriter);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }
    }
}