using System.Runtime.InteropServices;

namespace ArgusReduxCore.NetworkUDP
{
    public class SensorDataMessage : INetworkMessage
    {
        public MessageType MessageType => MessageType.SensorData;

        public PacketHeader Header;
        public List<IMUSample> IMUData = new();
        public byte[]? JpegImageBytes;

        public static SensorDataMessage? Parse(ReadOnlySpan<byte> data)
        {
            if (data.Length < PacketHeader.Size)
                return null;

            var packet = new SensorDataMessage();

            // Use MemoryMarshal to directly access the struct from the byte span
            packet.Header = MemoryMarshal.Read<PacketHeader>(data);

            int offset = PacketHeader.Size;
            for (int i = 0; i < packet.Header.ImuCount; i++)
            {
                if (offset + IMUSample.Size > data.Length)
                    break;
                // Use MemoryMarshal to directly access the struct from the byte span
                var sample = MemoryMarshal.Read<IMUSample>(data.Slice(offset));
                packet.IMUData.Add(sample);
                offset += IMUSample.Size;
            }

            if (packet.Header.ImageSize > 0 && offset + (int)packet.Header.ImageSize <= data.Length)
            {
                packet.JpegImageBytes = data.Slice(offset, (int)packet.Header.ImageSize).ToArray();
            }

            return packet;
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
