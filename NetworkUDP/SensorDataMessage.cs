using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ArgusReduxCore.NetworkUDP
{
    [MessageType(MessageType.SensorData)]
    public class SensorDataMessage : INetworkMessage
    {
        public MessageType MessageType => MessageType.SensorData;

        public PacketHeader Header;
        public List<IMUSample> IMUData = new();
        public byte[]? JpegImageBytes;

        public void Read(Stream stream)
        {
            if (stream.Length < PacketHeader.Size)
            {
                Console.WriteLine("Warning: Insufficient data for PacketHeader.");
                return;
            }

            // Read the header
            byte[] headerBytes = new byte[PacketHeader.Size];
            stream.Read(headerBytes, 0, PacketHeader.Size);
            Header = MemoryMarshal.Read<PacketHeader>(headerBytes.AsSpan());

            // Read the IMU samples
            for (int i = 0; i < Header.ImuCount; i++)
            {
                if (stream.Position + IMUSample.Size > stream.Length)
                {
                    Console.WriteLine($"Warning: Insufficient data for IMUSample {i}.");
                    break;
                }
                byte[] sampleBytes = new byte[IMUSample.Size];
                stream.Read(sampleBytes, 0, IMUSample.Size);
                var sample = MemoryMarshal.Read<IMUSample>(sampleBytes.AsSpan());
                IMUData.Add(sample);
            }

            // Read the JPEG image
            if (Header.ImageSize > 0)
            {
                if (stream.Position + Header.ImageSize > stream.Length)
                {
                    Console.WriteLine("Warning: Insufficient data for JPEG image.");
                    JpegImageBytes = null;
                    return;
                }
                JpegImageBytes = new byte[Header.ImageSize];
                stream.Read(JpegImageBytes, 0, (int)Header.ImageSize);
            }
            else
            {
                JpegImageBytes = null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PacketHeader
    {
        public const int Size = 15;

        public uint CameraTimestampStart;
        public uint CameraTimestampEnd;
        public ushort BatteryMv;
        public byte ImuCount;
        public uint ImageSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IMUSample
    {
        public const int Size = 10;

        public uint TimestampUs;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public short[] Accel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public short[] Gyro;
    }
}
