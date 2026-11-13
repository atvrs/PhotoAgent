using System;
using System.Collections.Generic;
using System.Linq;
using AForge.Video.DirectShow;
using AForge.Video;

namespace PhotoAgent
{
    public static class CameraManager
    {
        public static bool IsConnected(string cameraId)
        {
            return GetCameras().Any(camera => string.Equals(
                camera.MonikerString, cameraId, StringComparison.OrdinalIgnoreCase));
        }

        public static IVideoSource CreatePreviewDevice(string cameraId)
        {
            VideoCaptureDevice device = CreateDevice(cameraId);
            VideoCapabilities best = device.VideoCapabilities
                .OrderByDescending(capability =>
                    (long)capability.FrameSize.Width * capability.FrameSize.Height)
                .FirstOrDefault();
            if (best != null)
                device.VideoResolution = best;

            return device;
        }

        public static VideoCaptureDevice CreateDevice(
    string monikerString)
        {
            return new VideoCaptureDevice(
                monikerString
            );
        }
        public static List<CameraInfo> GetCameras()
        {
            List<CameraInfo> result =
                new List<CameraInfo>();

            FilterInfoCollection cameras =
                new FilterInfoCollection(
                    FilterCategory.VideoInputDevice
                );

            foreach (FilterInfo camera in cameras)
            {
                result.Add(
                    new CameraInfo
                    {
                        Name =
                            camera.Name,

                        MonikerString =
                            camera.MonikerString
                    });
            }

            return result;
        }
    }
}
