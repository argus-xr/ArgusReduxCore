// ArgusReduxCore — NetworkMessageReceiver.cs
// .NET 8.0 compatible
// Receives UDP packets and converts them to high-level C# message objects

using ArgusReduxCore.NetworkUDP;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace ArgusReduxCore
{
	public interface IUDPNetworkService
	{
		public delegate void PacketReceivedHandler(INetworkMessage message);
		event PacketReceivedHandler? OnPacketReceived;

		public void StartListening();
	}

	public class UDPNetworkService : IUDPNetworkService
	{
		private readonly ILogger<UDPNetworkService>? _logger;

		private readonly UdpClient _udpClient;
		private CancellationTokenSource _cancellationTokenSource;
		private const int _port = 4210;
		private const string ReplyMessage = "ARGUS_REPLY";
		private const int MaxUdpPacketSize = 512;

		public event IUDPNetworkService.PacketReceivedHandler? OnPacketReceived;

		public UDPNetworkService(ILogger<UDPNetworkService>? logger)
		{
			_udpClient = new UdpClient(_port);
			_logger = logger;
			_cancellationTokenSource = new CancellationTokenSource();
		}

		public void StartListening()
		{
			_logger?.LogInformation($"Starting UDP listener on port {_port}");
			Task.Run(async () =>
			{
				while (!_cancellationTokenSource.IsCancellationRequested)
				{
					var result = await _udpClient.ReceiveAsync();
					var buffer = result.Buffer;

					if (buffer.Length < 2) // Need at least 2 bytes: type and crc
					{
						_logger?.LogWarning("Received packet too short to be valid.");
						continue;
					}

					var messageType = (MessageType)buffer[0];
					var content = new ReadOnlyMemory<byte>(buffer, 1, buffer.Length - 2);
					var receivedCrc = buffer[buffer.Length - 1];

					var calculatedCrc = CalculateCrc8(buffer.AsSpan(0, buffer.Length - 1));

					if (receivedCrc != calculatedCrc)
					{
						_logger?.LogWarning($"CRC mismatch. Received: {receivedCrc}, Calculated: {calculatedCrc}");
						continue;
					}

					_logger?.LogDebug("Received data: {Data}", BitConverter.ToString(buffer));

					INetworkMessage? message = null;
					switch (messageType)
					{
						case MessageType.Discovery:
							SendSimpleMessage(MessageType.Hello, result.RemoteEndPoint);
							break;
						case MessageType.SensorData:
							message = SensorDataMessage.Parse(content.Span);
							break;
						// Add other message types here as needed
						default:
							_logger?.LogWarning($"Unknown message type: {messageType}");
							break;
					}

					if (message != null)
					{
						OnPacketReceived?.Invoke(message);
					}
				}
			});
		}

		private static byte CalculateCrc8(ReadOnlySpan<byte> data)
		{
			// Simple CRC-8 implementation (polynomial 0xD5)
			byte crc = 0;
			foreach (byte b in data)
			{
				crc ^= b;
				for (int i = 0; i < 8; i++)
				{
					if ((crc & 0x80) != 0)
					{
						crc = (byte)((crc << 1) ^ 0xD5);
					}
					else
					{
						crc <<= 1;
					}
				}
			}
			return crc;
		}

		public void SendSimpleMessage(MessageType type, IPEndPoint endpoint)
		{
			try
			{
				var message = new byte[2];
				message[0] = (byte)type;
				message[1] = CalculateCrc8(message.AsSpan(0, 1)); // A bit silly, but whatever works.
				_udpClient.Send(message, message.Length, endpoint);
			}
			catch (SocketException ex)
			{
				_logger?.LogError(ex, "Failed to send UDP message");
			}
		}
	}

	public interface INetworkMessage
	{
		MessageType MessageType { get; }
	}
}
