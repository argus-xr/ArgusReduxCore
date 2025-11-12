using System.IO;

namespace ArgusReduxCore.NetworkUDP
{
    [MessageType(MessageType.Discovery)]
    public class DiscoveryMessage : INetworkMessage
    {
        public MessageType MessageType => MessageType.Discovery;
        public ulong Uid { get; private set; }
        public ushort Length { get; private set; }

        public void Read(Stream stream)
        {
            Length = (ushort)stream.Length;
            using (var reader = new BinaryReader(stream))
            {
                Uid = reader.ReadUInt64();
            }
        }
    }
}
