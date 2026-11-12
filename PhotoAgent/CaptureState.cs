using System;

namespace PhotoAgent
{
    public static class CaptureState
    {
        private static readonly object SyncRoot = new object();
        private static string requestId;
        private static DateTime started;
        private static bool timedOut;

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
                return true;
            }
        }

        public static void MarkTimedOut()
        {
            lock (SyncRoot) { timedOut = true; }
        }

        public static void End()
        {
            lock (SyncRoot) { requestId = null; }
        }
    }
}
