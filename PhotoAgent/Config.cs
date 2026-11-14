namespace PhotoAgent
{
    public class Config
    {
        public const int DefaultArchiveRetentionMonths = 60;
        public const int DefaultTransferRetentionMonths = 1;
        public const double DefaultTransferMaxMb = 100;

        public Config()
        {
            archiveRetentionMonths = DefaultArchiveRetentionMonths;
            transferRetentionMonths = DefaultTransferRetentionMonths;
            transferMaxMb = DefaultTransferMaxMb;
        }

        public int previewWidth { get; set; }
        public int previewHeight { get; set; }

        public int captureTimeoutSec { get; set; }

        public double logFileMaxMb { get; set; }
        public double logTotalMaxMb { get; set; }

        public int archiveRetentionMonths { get; set; }
        public double archiveMaxGb { get; set; }
        public int transferRetentionMonths { get; set; }
        public double transferMaxMb { get; set; }

        public bool alwaysOnTop { get; set; }

        public string camera1Name { get; set; }
        public string camera2Name { get; set; }
        public string lomoCameraId { get; set; }

        public string documentCameraId { get; set; }
    }
}
