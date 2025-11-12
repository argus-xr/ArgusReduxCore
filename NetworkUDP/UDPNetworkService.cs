using ArgusReduxCore.NetworkUDP;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Collections.Concurrent;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

namespace ArgusReduxCore
{
    public interface IUDPNetworkService
    {
        public delegate void PacketReceivedHandler(INetworkMessage message, IPEndPoint remoteEndPoint);
        event PacketReceivedHandler? OnPacketReceived;
        event Action<IPEndPoint>? OnEndpointDisconnected;

        public void StartListening();
        void SendSimpleMessage(MessageType type, IPEndPoint endpoint);
    }

    public class UDPNetworkService : IUDPNetworkService
    {
        private readonly ILogger<UDPNetworkService>? _logger;

        private readonly UdpClient _udpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private const int _port = 4210;
        private const string ReplyMessage = "ARGUS_REPLY";
        private const int MaxUdpPacketSize = 512;
        private readonly ConcurrentDictionary<IPEndPoint, DateTime> _endpointLastMessageTime = new();
        private readonly ConcurrentDictionary<IPEndPoint, DateTime> _endpointLastReceivedTime = new();

        public event IUDPNetworkService.PacketReceivedHandler? OnPacketReceived;
        public event Action<IPEndPoint>? OnEndpointDisconnected;

        // Dictionary to store message parsers based on MessageType
        private readonly Dictionary<MessageType, Func<Stream, INetworkMessage?>> _messageParsers = new();

        public UDPNetworkService(ILogger<UDPNetworkService>? logger)
        {
            _udpClient = new UdpClient(_port);
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();

            // Discover and register message parsers
            RegisterMessageParsers();
            StartHeartbeatTask();
        }

        private void RegisterMessageParsers()
        {
            // Find all types in the current assembly that implement INetworkMessage
            var messageTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => typeof(INetworkMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in messageTypes)
            {
                // Check for the MessageTypeAttribute on the class
                var messageTypeAttribute = type.GetCustomAttribute<MessageTypeAttribute>();
                if (messageTypeAttribute != null)
                {
                    // Create a delegate that creates an instance and calls Read
                    _messageParsers[messageTypeAttribute.MessageType] = (stream) =>
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(type) as INetworkMessage;
                            if (instance != null)
                            {
                                instance.Read(stream); // Directly call the Read method
                                return instance;
                            }
                            else
                            {
                                _logger?.LogError($"Failed to create instance of type {type.Name}");
                                return null;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, $"Error creating or reading instance of type {type.Name}");
                            return null;
                        }
                    };
                }
                else
                {
                    _logger?.LogWarning($"Type {type.Name} implements INetworkMessage but has no MessageTypeAttribute.");
                }
            }
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
                    var content = buffer.AsSpan(1, buffer.Length - 2).ToArray();
                    var receivedCrc = buffer[buffer.Length - 1];

                    var calculatedCrc = CalculateCrc8(buffer.AsSpan(0, buffer.Length - 1));

                    if (receivedCrc != calculatedCrc)
                    {
                        _logger?.LogWarning($"CRC mismatch. Received: {receivedCrc}, Calculated: {calculatedCrc}");
                        continue;
                    }

                    _logger?.LogDebug("Received data: {Data}", BitConverter.ToString(buffer));

                    INetworkMessage? message = null;
                    if (_messageParsers.ContainsKey(messageType))
                    {
                        using var stream = new MemoryStream(content);
                        message = _messageParsers[messageType](stream);
                    }
                    else
                    {
                        _logger?.LogWarning($"Unknown message type: {messageType}");
                    }

                    if (message != null)
                    {
                        OnPacketReceived?.Invoke(message, result.RemoteEndPoint);
                        _endpointLastReceivedTime[result.RemoteEndPoint] = DateTime.UtcNow;
                    }
                }
            });
        }

        private void StartHeartbeatTask()
        {
            Task.Run(async () =>
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    await Task.Delay(1000); // Check every second

                    // Check for outgoing heartbeats
                    foreach (var endpoint in _endpointLastMessageTime.Keys)
                    {
                        if (_endpointLastMessageTime.TryGetValue(endpoint, out var lastTime))
                        {
                            if (DateTime.UtcNow - lastTime > TimeSpan.FromSeconds(2))
                            {
                                SendSimpleMessage(MessageType.Heartbeat, endpoint);
                            }
                        }
                    }

                    // Check for disconnected endpoints
                    foreach (var endpoint in _endpointLastReceivedTime.Keys)
                    {
                        if (_endpointLastReceivedTime.TryGetValue(endpoint, out var lastReceivedTime))
                        {
                            if (DateTime.UtcNow - lastReceivedTime > TimeSpan.FromSeconds(10))
                            {
                                _logger?.LogInformation($"Endpoint {endpoint} disconnected due to inactivity.");
                                _endpointLastReceivedTime.TryRemove(endpoint, out _);
                                _endpointLastMessageTime.TryRemove(endpoint, out _);
                                OnEndpointDisconnected?.Invoke(endpoint);
                            }
                        }
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
            // Apparently thread-safe, according to Gemini.
            try
            {
                var message = new byte[2];
                message[0] = (byte)type;
                message[1] = CalculateCrc8(message.AsSpan(0, 1)); // A bit silly, but whatever works.
                _udpClient.Send(message, message.Length, endpoint);
                _endpointLastMessageTime[endpoint] = DateTime.UtcNow;
            }
            catch (SocketException ex)
            {
                _logger?.LogError(ex, "Failed to send UDP message");
            }
        }
    }

	public interface INetworkMessage
	{
        ushort Length { get; }
		MessageType MessageType { get; }
		void Read(Stream stream);
	}

    // Attribute to mark message types
    [AttributeUsage(AttributeTargets.Class)]
    public class MessageTypeAttribute : Attribute
    {
        public MessageType MessageType { get; }

        public MessageTypeAttribute(MessageType messageType)
        {
            MessageType = messageType;
        }
    }
}
