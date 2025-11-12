# EKF for Asynchronous Sensor Fusion

This document explains how to use an Extended Kalman Filter (EKF) to fuse asynchronous data streams, specifically high-frequency IMU data and lower-frequency VSLAM pose estimates.

The core EKF equations operate in discrete time steps. The key to handling asynchronous data is the logic built *around* the filter.

All sensor data is expected to be timestamped at the source (e.g., the ESP32) to ensure a consistent clock.

## EKF Prediction and Correction Steps

The fusion process is split into two main steps that run at different frequencies.

### 1. Prediction Step (High Frequency)

This step uses a motion model driven by the IMU.

-   **Trigger:** Runs every time a new IMU measurement is received.
-   **Input:** The IMU's acceleration and angular velocity.
-   **Action:** The EKF's state (pose, velocity) is updated based on the motion model. This predicts where the sensor should be.
-   **Result:** This prediction is very responsive but will drift over time due to IMU noise.

### 2. Correction Step (Low Frequency, Asynchronous)

This step uses an external measurement from the VSLAM algorithm to correct the drift from the prediction step.

-   **Trigger:** Runs whenever a new VSLAM pose estimate is available.
-   **Input:** The VSLAM's pose estimate (position and orientation).
-   **Action:** The VSLAM pose is used to correct the state that has been predicted by the IMU.

## Handling Asynchronicity

Because the VSLAM pose arrives asynchronously, a special synchronization step is required before the correction can be applied.

The process is as follows:

1.  A new VSLAM pose with timestamp `T_vslam` is received.
2.  The fusion logic checks the timestamp of its last update, `T_last_update`.
3.  The logic then processes all IMU measurements that have arrived between `T_last_update` and `T_vslam`. For each of these intermediate IMU measurements, it runs the **Prediction Step**.
4.  After this "fast-forward" process, the EKF's state is now predicted up to time `T_vslam`.
5.  With the state synchronized to the measurement time, the **Correction Step** is run using the VSLAM pose.

This architecture allows the high-frequency IMU to provide a real-time pose estimate, while the slower VSLAM data periodically anchors the estimate to reality and eliminates drift.
