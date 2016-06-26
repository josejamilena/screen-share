using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UlteriusScreenShare.Websocket.Server
{
    public class Packet
    {
        public enum MessageType
        {
         Binary,
         Text   
        }
        public readonly AuthClient AuthClient;
        public readonly byte[] Data;
        public readonly string Json;
        public readonly MessageType Type;

        public Packet(AuthClient authClient, byte[] data, MessageType type)
        {
            AuthClient = authClient;
            Data = data;
            Type = type;
        }
    
        public Packet (AuthClient authClient, string json, MessageType type)
        {
            AuthClient = authClient;
            Json = json;
            Type = type;
        }        
    }
}
