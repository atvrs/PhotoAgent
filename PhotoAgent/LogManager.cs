using System;
using System.IO;
using System.Linq;

namespace PhotoAgent
{
    public static class LogManager
    {
        private static readonly object SyncRoot =
            new object();

        private static readonly string LogFolder =
            @"C:\psa_photos\logs";
        public static void RunMaintenance()
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(
                        LogFolder
                    );

                    CleanupTotalSize();
                }
            }
            catch
            {
            }
        }
        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogFolder);

                    string baseName =
                        DateTime.Now.ToString("yyyy-MM");

                    string currentLog =
                        Path.Combine(
                            LogFolder,
                            baseName + ".log"
                        );

                    RotateIfNeeded(currentLog);

                    string line =
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss"
                        )
                        + " | "
                        + message;

                    File.AppendAllText(
                        currentLog,
                        line + Environment.NewLine
                    );

                    CleanupTotalSize();
                }
            }
            catch
            {
            }
        }

        private static void RotateIfNeeded(
            string currentLog)
        {
            long maxSizeBytes =
     (long)(
         ConfigManager.Current.logFileMaxMb
         * 1024d
         * 1024d
     );

            if (!File.Exists(currentLog))
                return;

            FileInfo info =
                new FileInfo(currentLog);

            if (info.Length < maxSizeBytes)
                return;

            string baseName =
                Path.GetFileNameWithoutExtension(
                    currentLog
                );

            string nextFile =
                GetNextArchiveName(baseName);

            File.Move(
                currentLog,
                nextFile
            );
        }

        private static string GetNextArchiveName(
            string baseName)
        {
            int index = 1;

            while (true)
            {
                string file =
                    Path.Combine(
                        LogFolder,
                        $"{baseName}_{index:D3}.log"
                    );

                if (!File.Exists(file))
                    return file;

                index++;
            }
        }

        private static void CleanupTotalSize()
        {
            long limitBytes =
     (long)(
         ConfigManager.Current.logTotalMaxMb
         * 1024d
         * 1024d
     );

            DirectoryInfo dir =
                new DirectoryInfo(LogFolder);

            var files =
                dir.GetFiles("*.log")
                   .OrderBy(f => f.CreationTimeUtc)
                   .ToList();

            long totalSize =
                files.Sum(f => f.Length);

            foreach (var file in files)
            {
                if (totalSize <= limitBytes)
                    break;

                try
                {
                    long size = file.Length;

                    file.Delete();

                    totalSize -= size;
                }
                catch
                {
                }
            }
        }
    }
}