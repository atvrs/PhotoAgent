using System.IO;
using System.Web.Script.Serialization;

namespace PhotoAgent
{
    public static class ConfigManager
    {
        private static readonly string ConfigFile =
            @"C:\psa_photos\config.json";

        private static Config _config;

        public static Config Current
        {
            get
            {
                if (_config == null)
                {
                    Load();
                }

                return _config;
            }
        }

        public static void Load()
        {
            if (!File.Exists(ConfigFile))
            {
                CreateDefault();
                Save();

                return;
            }

            string json =
                File.ReadAllText(
                    ConfigFile
                );

            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            _config =
                serializer.Deserialize<Config>(
                    json
                );
        }

        public static void Save()
        {
            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            string json =
                serializer.Serialize(
                    _config
                );

            File.WriteAllText(
                ConfigFile,
                json
            );
        }

        private static void CreateDefault()
        {
            _config =
                new Config
                {
                    previewWidth = 640,
                    previewHeight = 480,

                    captureTimeoutSec = 120,

                    logFileMaxMb = 1,
                    logTotalMaxMb = 100,

                    archiveRetentionDays = 365,
                    archiveMaxGb = 100,

                    alwaysOnTop = true,

                    camera1Name = "",
                    camera2Name = "",

                    lomoCameraId = "",
                    documentCameraId = ""
                };
        }
    }
}