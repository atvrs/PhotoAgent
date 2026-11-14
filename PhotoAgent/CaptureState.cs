using System;

namespace PhotoAgent
{
    public static class CaptureState
    {
        private static readonly object SyncRoot = new object();
        private static string requestId;
        private static DateTime started;
        private static bool timedOut;
        private static DateTime? cameraWaitStarted;

        public static bool IsBusy
        {
            get { lock (SyncRoot) { return requestId != null; } }
        }

        public static string RequestId
        {
            get { lock (SyncRoot) { return requestId; } }
        }

        public static DateTime Started
        {
            get { lock (SyncRoot) { return started; } }
        }

        public static bool TimedOut
        {
            get { lock (SyncRoot) { return timedOut; } }
        }

        public static bool TryBegin(string id)
        {
            lock (SyncRoot)
            {
                if (requestId != null)
                    return false;

                requestId = id;
                started = DateTime.UtcNow;
                timedOut = false;
                cameraWaitStarted = null;
                return true;
            }
        }

        public static void MarkTimedOut()
        {
            lock (SyncRoot) { timedOut = true; }
        }

        public static void End()
        {
            lock (SyncRoot)
            {
                requestId = null;
                cameraWaitStarted = null;
            }
        }

        public static bool IsWaitingForCamera
        {
            get { lock (SyncRoot) { return cameraWaitStarted.HasValue; } }
        }

        public static void SetWaitingForCamera(bool waiting)
        {
            lock (SyncRoot)
            {
                if (requestId == null)
                    return;

                if (waiting && !cameraWaitStarted.HasValue)
                    cameraWaitStarted = DateTime.UtcNow;
                else if (!waiting && cameraWaitStarted.HasValue)
                {
                    started += DateTime.UtcNow - cameraWaitStarted.Value;
                    cameraWaitStarted = null;
                }
            }
        }

        public static bool IsTimeoutDue(int timeoutSeconds)
        {
            lock (SyncRoot)
            {
                return requestId != null && !timedOut && !cameraWaitStarted.HasValue
                    && (DateTime.UtcNow - started).TotalSeconds > timeoutSeconds;
            }
        }
    }
}
