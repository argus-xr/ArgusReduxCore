namespace ArgusReduxCore.NetworkUDP
{
	public enum MessageType : byte
	{
		Unknown = 0x00,
		Discovery = 0x01,
		Heartbeat = 0x02,
		SetupConfig = 0x03,
		SensorData = 0x04,
	}
}
