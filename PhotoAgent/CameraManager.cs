using System.Collections.Generic;
using AForge.Video.DirectShow;
using AForge.Video;

namespace PhotoAgent
{
    public static class CameraManager
    {
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