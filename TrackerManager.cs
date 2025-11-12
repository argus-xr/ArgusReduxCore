// ArgusReduxCore — TrackerManager.cs
// Responsible for routing incoming messages to per-tracker objects based on UID

using ArgusReduxCore.NetworkUDP;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArgusReduxCore
{
    public interface ITrackerManager
    {
        void HandleMessage(INetworkMessage message, System.Net.IPEndPoint remoteEndPoint);
        public event Action<Tracker>? OnTrackerAdded;
        public event Action<Tracker>? OnTrackerRemoved;
        public event Action<System.Net.IPEndPoint, ulong>? OnEndpointReplaced;
    }

    public class TrackerManager : ITrackerManager
    {
        private readonly ConcurrentDictionary<ulong, Tracker> _trackers = new();
        private readonly ConcurrentDictionary<System.Net.IPEndPoint, ulong> _endpointToUid = new();
        private readonly IUDPNetworkService _networkService;

        public event Action<Tracker>? OnTrackerAdded;
        public event Action<Tracker>? OnTrackerRemoved;
        public event Action<System.Net.IPEndPoint, ulong>? OnEndpointReplaced;

        public TrackerManager(IUDPNetworkService networkService)
        {
            _networkService = networkService;
            _networkService.OnPacketReceived += HandleMessage;
            _networkService.OnEndpointDisconnected += HandleEndpointDisconnected;
        }

        public void HandleMessage(INetworkMessage message, System.Net.IPEndPoint remoteEndPoint)
        {
            if (message is DiscoveryMessage discoveryMessage)
            {
                // Check if this UID is already associated with a different endpoint
                var existingEndpoint = _endpointToUid.FirstOrDefault(x => x.Value == discoveryMessage.Uid && !x.Key.Equals(remoteEndPoint)).Key;

                if (existingEndpoint != null)
                {
                    // An old endpoint is claiming this UID, so disconnect it
                    HandleEndpointDisconnected(existingEndpoint);
                    OnEndpointReplaced?.Invoke(existingEndpoint, discoveryMessage.Uid);
                }

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
            else if (message is ImageChunkMessage imageChunkMessage)
            {
                if (_endpointToUid.TryGetValue(remoteEndPoint, out var uid))
                {
                    if (_trackers.TryGetValue(uid, out var tracker))
                    {
                        tracker.HandleImageChunk(imageChunkMessage);
                    }
                }
            }
        }
        private void HandleEndpointDisconnected(System.Net.IPEndPoint remoteEndPoint)
        {
            if (_endpointToUid.TryRemove(remoteEndPoint, out var uid))
            {
                if (_trackers.TryRemove(uid, out var tracker))
                {
                    OnTrackerRemoved?.Invoke(tracker);
                }
            }
        }
    }
    public class ImageReconstructor
    {
        private readonly ConcurrentDictionary<uint, byte[]> _chunks = new();
        private readonly uint _frameId;
        private readonly uint _totalSize;
        private readonly Action<byte[]> _onComplete;
        private readonly Action _onTimeout;
        private readonly Task _timeoutTask;
        private readonly System.Threading.CancellationTokenSource _cancellationTokenSource = new();

        public ImageReconstructor(uint frameId, uint totalSize, Action<byte[]> onComplete, Action onTimeout)
        {
            _frameId = frameId;
            _totalSize = totalSize;
            _onComplete = onComplete;
            _onTimeout = onTimeout;

            _timeoutTask = Task.Delay(2000, _cancellationTokenSource.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    _onTimeout();
                }
            });
        }

        public void AddChunk(ImageChunkMessage chunk)
        {
            if (chunk.Header.FrameID != _frameId || chunk.ImageChunkBytes == null)
            {
                return;
            }

            _chunks[chunk.Header.StartByte] = chunk.ImageChunkBytes;

            if (_chunks.Values.Sum(c => c.Length) >= _totalSize) // This could get confused by duplicate chunks?
            {
                _cancellationTokenSource.Cancel();
                ReconstructAndFireComplete();
            }
        }

        private void ReconstructAndFireComplete()
        {
            var sortedChunks = _chunks.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).ToArray();
            var frameData = new byte[_totalSize];
            int offset = 0;
            foreach (var chunk in sortedChunks)
            {
                Buffer.BlockCopy(chunk, 0, frameData, offset, chunk.Length);
                offset += chunk.Length;
            }
            _onComplete(frameData);
        }
    }

    public class Tracker
    {
        public ulong ID { get; }
        public event Action<SensorDataMessage>? OnUpdated;
        public event Action<byte[]>? OnFrameReceived;

        private readonly object _lock = new();
        private List<SensorDataMessage> _history = new();
        private readonly ConcurrentDictionary<uint, ImageReconstructor> _imageReconstructors = new();
        private readonly ConcurrentDictionary<uint, uint> _frameTotalSizes = new();
        private readonly ConcurrentDictionary<uint, ConcurrentBag<ImageChunkMessage>> _unprocessedChunks = new();
        private readonly ConcurrentDictionary<uint, System.Threading.CancellationTokenSource> _unprocessedChunkCts = new();

        public Tracker(ulong id)
        {
            ID = id;
        }

        public void HandlePacket(SensorDataMessage packet)
        {
            lock (_lock)
            {
                _history.Add(packet);
                _frameTotalSizes[packet.Header.FrameID] = packet.Header.ImageSize;
            }
            OnUpdated?.Invoke(packet);

            if (_unprocessedChunks.TryRemove(packet.Header.FrameID, out var chunks))
            {
                if (_unprocessedChunkCts.TryRemove(packet.Header.FrameID, out var cts))
                {
                    cts.Cancel();
                }

                var reconstructor = _imageReconstructors.GetOrAdd(packet.Header.FrameID, (frameId) =>
                {
                    return new ImageReconstructor(frameId, packet.Header.ImageSize,
                        (frameData) =>
                        {
                            OnFrameReceived?.Invoke(frameData);
                            _imageReconstructors.TryRemove(frameId, out _);
                            _frameTotalSizes.TryRemove(frameId, out _);
                        },
                        () =>
                        {
                            _imageReconstructors.TryRemove(frameId, out _);
                            _frameTotalSizes.TryRemove(frameId, out _);
                        });
                });

                foreach (var chunk in chunks)
                {
                    reconstructor.AddChunk(chunk);
                }
            }
        }
        public void HandleImageChunk(ImageChunkMessage chunk)
        {
            if (!_frameTotalSizes.TryGetValue(chunk.Header.FrameID, out var totalSize))
            {
                var chunkBag = _unprocessedChunks.GetOrAdd(chunk.Header.FrameID, (id) => {
                    var cts = new System.Threading.CancellationTokenSource();
                    _unprocessedChunkCts[id] = cts;
                    Task.Delay(2000, cts.Token).ContinueWith(t => {
                        if (!t.IsCanceled)
                        {
                            _unprocessedChunks.TryRemove(id, out _);
                            _unprocessedChunkCts.TryRemove(id, out _);
                        }
                    });
                    return new ConcurrentBag<ImageChunkMessage>();
                });
                chunkBag.Add(chunk);
                return;
            }
            var reconstructor = _imageReconstructors.GetOrAdd(chunk.Header.FrameID, (frameId) =>
            {
                return new ImageReconstructor(frameId, totalSize,
                    (frameData) =>
                    {
                        OnFrameReceived?.Invoke(frameData);
                        _imageReconstructors.TryRemove(frameId, out _);
                        _frameTotalSizes.TryRemove(frameId, out _);
                    },
                    () =>
                    {
                        _imageReconstructors.TryRemove(frameId, out _);
                        _frameTotalSizes.TryRemove(frameId, out _);
                    });
            });

            reconstructor.AddChunk(chunk);
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
