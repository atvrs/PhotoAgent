namespace PhotoAgent
{
    public class Config
    {
        public int previewWidth { get; set; }
        public int previewHeight { get; set; }

        public int captureTimeoutSec { get; set; }

        public double logFileMaxMb { get; set; }
        public double logTotalMaxMb { get; set; }

        public int archiveRetentionDays { get; set; }
        public double archiveMaxGb { get; set; }

        public bool alwaysOnTop { get; set; }

        public string camera1Name { get; set; }
        public string camera2Name { get; set; }
        public string lomoCameraId { get; set; }

        public string documentCameraId { get; set; }
    }
}