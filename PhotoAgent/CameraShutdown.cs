using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AForge.Video;

namespace PhotoAgent
{
    internal static class CameraShutdown
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Task> Pending =
            new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);

        public static bool IsStopping(string cameraId)
        {
            lock (SyncRoot)
            {
                Task task;
                if (!Pending.TryGetValue(cameraId, out task))
                    return false;
                if (!task.IsCompleted)
                    return true;

                Pending.Remove(cameraId);
                return false;
            }
        }

        public static void Stop(string cameraId, IVideoSource source)
        {
            lock (SyncRoot)
            {
                Task previous;
                if (!Pending.TryGetValue(cameraId, out previous))
                    previous = Task.FromResult(0);

                Pending[cameraId] = previous.ContinueWith(_ =>
                {
                    try
                    {
                        source.SignalToStop();
                        source.WaitForStop();
                        LogManager.Write("Camera released: " + cameraId);
                    }
                    catch (Exception ex)
                    {
                        LogManager.Write("Camera stop error: " + ex.Message);
                    }
                }, TaskScheduler.Default);
            }
        }
    }
}
