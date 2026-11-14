using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
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
            Load(ConfigFile);
        }

        internal static void Load(string configFile)
        {
            if (!File.Exists(configFile))
            {
                CreateDefault();
                WriteConfig(configFile, new JavaScriptSerializer().Serialize(_config));
                if (configFile == ConfigFile)
                    ShortcutManager.CreateShortcutIfNotExist();

                return;
            }

            string json =
                File.ReadAllText(
                    configFile
                );

            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            var values = serializer.Deserialize<Dictionary<string, object>>(json);
            if (values == null)
                throw new InvalidDataException("Configuration must be a JSON object");

            object months;
            bool changed = false;
            if (!values.TryGetValue("archiveRetentionMonths", out months)
                || !(months is int) || (int)months <= 0)
            {
                values["archiveRetentionMonths"] = Config.DefaultArchiveRetentionMonths;
                changed = true;
            }
            changed = values.Remove("archiveRetentionDays") || changed;
            if (changed)
                json = serializer.Serialize(values);
            _config = serializer.Deserialize<Config>(json);
            if (changed)
            {
                WriteConfig(configFile, json);
                LogManager.Write("Archive configuration migrated to months: "
                    + _config.archiveRetentionMonths);
            }
        }

        public static void Save()
        {
            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            string json =
                serializer.Serialize(
                    _config
                );

            WriteConfig(ConfigFile, json);
            // Автоматический вызов создания ярлыка
            ShortcutManager.CreateShortcutIfNotExist();
        }

        private static void WriteConfig(string configFile, string json)
        {
            string temporaryFile = configFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryFile, json);
                if (File.Exists(configFile))
                    File.Replace(temporaryFile, configFile, configFile + ".bak");
                else
                    File.Move(temporaryFile, configFile);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                    File.Delete(temporaryFile);
            }
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

                    archiveRetentionMonths = Config.DefaultArchiveRetentionMonths,
                    archiveMaxGb = 100,

                    alwaysOnTop = true,

                    camera1Name = "",
                    camera2Name = "",

                    lomoCameraId = "",
                    documentCameraId = ""
                };
        }
    }
    public static class ShortcutManager
    {
        /// <summary>
        /// Создает ярлык для текущего запущенного .exe файла в папке C:\psa_photos, если он еще не создан.
        /// </summary>
        public static void CreateShortcutIfNotExist()
        {
            string targetFolder = @"C:\psa_photos";
            string shortcutPath = Path.Combine(targetFolder, "PhotoAgent.lnk");

            // Проверяем, существует ли папка назначения и сам ярлык
            if (!Directory.Exists(targetFolder) || File.Exists(shortcutPath))
            {
                return;
            }

            try
            {
                // Получаем путь к текущему запущенному исполняемому файлу (.exe)
                string currentExePath = Assembly.GetExecutingAssembly().Location;

                // Используем COM-объект WScript.Shell через reflection (динамическое/позднее связывание)
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                object shell = Activator.CreateInstance(shellType);

                // Вызываем метод CreateShortcut
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });

                if (shortcut != null)
                {
                    Type shortcutType = shortcut.GetType();

                    // Задаем путь к целевому .exe файлу
                    shortcutType.InvokeMember("TargetPath",
                        BindingFlags.SetProperty, null, shortcut, new object[] { currentExePath });

                    // Задаем рабочую директорию (папку, откуда запускается приложение)
                    string workingDir = Path.GetDirectoryName(currentExePath);
                    shortcutType.InvokeMember("WorkingDirectory",
                        BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });

                    // Сохраняем созданный ярлык
                    shortcutType.InvokeMember("Save",
                        BindingFlags.InvokeMethod, null, shortcut, null);
                }
            }
            catch (Exception ex)
            {
                // Записываем ошибку в лог через существующий в проекте LogManager
                // LogManager.LogError("Не удалось создать ярлык: " + ex.Message);
            }
        }
    }
}
