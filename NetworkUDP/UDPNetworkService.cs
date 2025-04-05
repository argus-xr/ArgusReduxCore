// ArgusReduxCore — NetworkMessageReceiver.cs
// .NET 8.0 compatible
// Receives UDP packets and converts them to high-level C# message objects

using ArgusReduxCore.NetworkUDP;
using Microsoft.Extensions.Logging;
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
					var packet = SensorDataMessage.Parse(result.Buffer);
					if (packet != null)
					{
						_logger?.LogDebug("Received data: {Data}", BitConverter.ToString(result.Buffer));
						OnPacketReceived?.Invoke(packet);
					}
				}
			});
		}
	}

	public interface INetworkMessage
	{
		MessageType MessageType { get; }
	}
}
