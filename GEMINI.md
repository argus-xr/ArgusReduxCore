# ArgusReduxCore

This project is the core component of the Argus VR tracking system. It runs as a server on a local PC and is responsible for receiving and processing data from one or more VR trackers.

## Features

*   **UDP Network Communication:** The server uses UDP to communicate with the trackers. It can receive and parse different types of messages, including discovery messages and sensor data messages.
*   **Tracker Management:** The server can manage multiple trackers simultaneously. It creates a new `Tracker` object for each new tracker that is discovered.
*   **Sensor Data Processing:** The server receives sensor data from the trackers, which includes IMU samples (accelerometer and gyroscope) and an optional JPEG image. This data is stored in a history for each tracker.
*   **Extensible Message System:** The message system is designed to be extensible. New message types can be added by creating a new class that implements the `INetworkMessage` interface and has a `MessageTypeAttribute`.

## Architecture

The project is divided into the following main components:

*   **`ArgusCoreService`:** The entry point of the application. It is responsible for setting up the dependency injection container and configuring the core services.
*   **`UDPNetworkService`:** Handles all UDP communication. It listens for incoming packets, parses them, and raises an event when a new message is received.
*   **`TrackerManager`:** Manages the collection of `Tracker` objects. It routes incoming messages to the appropriate tracker.
*   **`Tracker`:** Represents a single VR tracker. It stores the history of sensor data received from the tracker.
*   **`NetworkUDP` Messages:** A collection of classes that represent the different types of messages that can be sent and received over the network.

## How it Works

1.  The `ArgusCoreService` is started, which in turn starts the `UDPNetworkService`.
2.  The `UDPNetworkService` listens for incoming UDP packets on port 4210.
3.  When a tracker is powered on, it sends a `DiscoveryMessage` to the server.
4.  The `UDPNetworkService` receives the `DiscoveryMessage` and the `TrackerManager` creates a new `Tracker` object for the new tracker.
5.  The tracker then starts sending `SensorDataMessage` packets to the server.
6.  The `UDPNetworkService` receives the `SensorDataMessage` packets and routes them to the appropriate `Tracker` object via the `TrackerManager`.
7.  The `Tracker` object stores the sensor data and raises an `OnUpdated` event.
8.  A separate component (e.g., a visualizer) can subscribe to the `OnTrackerAdded` and `OnUpdated` events to receive real-time updates from the trackers.