using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ArgusReduxCore.NetworkUDP
{
    [MessageType(MessageType.ImageChunk)]
    public class ImageChunkMessage : INetworkMessage
    {
        public MessageType MessageType => MessageType.ImageChunk;

        public ImageChunkHeader Header;
        public byte[]? ImageChunkBytes;

        public ushort Length { get; private set; }

        public void Read(Stream stream)
        {
            Length = (ushort)stream.Length;
            if (stream.Length < ImageChunkHeader.Size)
            {
                Console.WriteLine("Warning: Insufficient data for PacketHeader.");
                return;
            }

            // Read the header
            byte[] headerBytes = new byte[ImageChunkHeader.Size];
            stream.Read(headerBytes, 0, ImageChunkHeader.Size);
            Header = MemoryMarshal.Read<ImageChunkHeader>(headerBytes.AsSpan());

            // Read the image chunk itself
            if (Header.Length > 0)
            {
                if (stream.Position + Header.Length > stream.Length)
                {
                    Console.WriteLine("Warning: Insufficient data for image chunk.");
                    ImageChunkBytes = null;
                    return;
                }
                ImageChunkBytes = new byte[Header.Length];
                stream.Read(ImageChunkBytes, 0, (int)Header.Length);
            }
            else
            {
                ImageChunkBytes = null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ImageChunkHeader
    {
        public const int Size = 12;

        public uint FrameID;
        public uint StartByte;
        public uint Length;
    }
}
