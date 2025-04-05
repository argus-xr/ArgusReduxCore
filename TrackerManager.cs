// ArgusReduxCore — TrackerManager.cs
// Responsible for routing incoming messages to per-tracker objects based on UID

using ArgusReduxCore.NetworkUDP;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ArgusReduxCore
{
	public class TrackerManager
	{
		private readonly ConcurrentDictionary<ulong, Tracker> _trackers = new();

		public event Action<Tracker>? OnTrackerAdded;

		public void HandleMessage(INetworkMessage message)
		{
			if (message is SensorDataMessage trackerPacket)
			{
				// Temporary ID logic (replace with UID-based logic when available)
				ulong key = 1;

				var tracker = _trackers.GetOrAdd(key, _ => {
					var t = new Tracker(key);
					OnTrackerAdded?.Invoke(t);
					return t;
				});

				tracker.HandlePacket(trackerPacket);
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
