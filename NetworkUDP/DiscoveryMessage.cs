using System.IO;

namespace ArgusReduxCore.NetworkUDP
{
    [MessageType(MessageType.Discovery)]
    public class DiscoveryMessage : INetworkMessage
    {
        public MessageType MessageType => MessageType.Discovery;

        public void Read(Stream stream)
        {
            // No data to read for a discovery message
        }
    }
}
