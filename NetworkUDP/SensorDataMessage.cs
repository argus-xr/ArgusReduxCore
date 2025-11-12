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

        public SensorDataHeader Header;
        public List<IMUSample> IMUData = new();

        public ushort Length { get; private set; }

        public void Read(Stream stream)
        {
            Length = (ushort) stream.Length;
            if (stream.Length < SensorDataHeader.Size)
            {
                Console.WriteLine("Warning: Insufficient data for PacketHeader.");
                return;
            }

            // Read the header
            byte[] headerBytes = new byte[SensorDataHeader.Size];
            stream.Read(headerBytes, 0, SensorDataHeader.Size);
            Header = MemoryMarshal.Read<SensorDataHeader>(headerBytes.AsSpan());

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
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SensorDataHeader
    {
        public const int Size = 19;

        public uint FrameID;
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
