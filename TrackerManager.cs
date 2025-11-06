// ArgusReduxCore — TrackerManager.cs
// Responsible for routing incoming messages to per-tracker objects based on UID

using ArgusReduxCore.NetworkUDP;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ArgusReduxCore
{
	public interface ITrackerManager
	{
		void HandleMessage(INetworkMessage message, System.Net.IPEndPoint remoteEndPoint);
        public event Action<Tracker>? OnTrackerAdded;
    }

	public class TrackerManager : ITrackerManager
	{
		private readonly ConcurrentDictionary<ulong, Tracker> _trackers = new();
		private readonly ConcurrentDictionary<System.Net.IPEndPoint, ulong> _endpointToUid = new();
		private readonly IUDPNetworkService _networkService;

		public event Action<Tracker>? OnTrackerAdded;

		public TrackerManager(IUDPNetworkService networkService)
		{
			_networkService = networkService;
			_networkService.OnPacketReceived += HandleMessage;
		}

		public void HandleMessage(INetworkMessage message, System.Net.IPEndPoint remoteEndPoint)
		{
			if (message is DiscoveryMessage discoveryMessage)
			{
				_endpointToUid[remoteEndPoint] = discoveryMessage.Uid;
				_networkService.SendSimpleMessage(MessageType.Hello, remoteEndPoint);

				_trackers.GetOrAdd(discoveryMessage.Uid, uid =>
				{
					var t = new Tracker(uid);
					OnTrackerAdded?.Invoke(t);
					return t;
				});
			}
			else if (message is SensorDataMessage trackerPacket)
			{
				if (_endpointToUid.TryGetValue(remoteEndPoint, out var uid))
				{
					if (_trackers.TryGetValue(uid, out var tracker))
					{
						tracker.HandlePacket(trackerPacket);
					}
				}
			}
		}
	}

	public class Tracker
	{
		public ulong ID { get; }
		public event Action<SensorDataMessage>? OnUpdated;

		private readonly object _lock = new();
		private List<SensorDataMessage> _history = new();

		public Tracker(ulong id)
		{
			ID = id;
		}

		public void HandlePacket(SensorDataMessage packet)
		{
			lock (_lock)
			{
				_history.Add(packet);
			}
			OnUpdated?.Invoke(packet);
		}

		public IReadOnlyList<SensorDataMessage> GetHistory()
		{
			lock (_lock)
			{
				return _history.AsReadOnly();
			}
		}
	}
}
