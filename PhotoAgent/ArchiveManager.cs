using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PhotoAgent
{
    public static class ArchiveManager
    {
        private const string ArchiveRoot = @"C:\psa_photos\archive";

        public static void ArchiveDocument(string documentNumber)
        {
            string sourceFolder = Path.Combine(@"C:\psa_photos\photos", documentNumber);
            if (!Directory.Exists(sourceFolder))
                return;
            string archiveFolder = Path.Combine(GetMonthFolder(), documentNumber);
            if (Directory.Exists(archiveFolder))
                Directory.Delete(archiveFolder, true);
            CopyDirectory(sourceFolder, archiveFolder);
            LogManager.Write("Archived: " + documentNumber);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        public static void RunMaintenance()
        {
            RunMaintenance(ArchiveRoot, ConfigManager.Current, DateTime.Now);
        }

        internal static void RunMaintenance(string archiveRoot, Config config, DateTime now)
        {
            try
            {
                Directory.CreateDirectory(archiveRoot);
                int months = config.archiveRetentionMonths > 0
                    ? config.archiveRetentionMonths : Config.DefaultArchiveRetentionMonths;
                DateTime currentMonth = new DateTime(now.Year, now.Month, 1);
                int elapsedMonths = (now.Year - 1) * 12 + now.Month - 1;
                DateTime oldestMonth = months - 1 >= elapsedMonths
                    ? DateTime.MinValue : currentMonth.AddMonths(1 - months);

                foreach (DirectoryInfo folder in MonthFolders(archiveRoot))
                {
                    if (ParseMonth(folder.Name).Value < oldestMonth)
                        DeleteMonth(folder, "Archive deleted by age: ");
                }
                CleanupBySize(archiveRoot, config.archiveMaxGb);
            }
            catch (Exception ex)
            {
                LogManager.Write("Archive maintenance error: " + ex.Message);
            }
        }

        private static DirectoryInfo[] MonthFolders(string archiveRoot)
        {
            return new DirectoryInfo(archiveRoot).GetDirectories()
                .Where(folder => (folder.Attributes & FileAttributes.ReparsePoint) == 0
                    && ParseMonth(folder.Name).HasValue)
                .OrderBy(folder => ParseMonth(folder.Name).Value)
                .ToArray();
        }

        private static DateTime? ParseMonth(string name)
        {
            DateTime month;
            if (DateTime.TryParseExact(name, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out month))
                return month;
            return null;
        }

        private static void CleanupBySize(string archiveRoot, double maxGb)
        {
            if (maxGb <= 0 || double.IsNaN(maxGb) || double.IsInfinity(maxGb))
                return;
            double requestedBytes = maxGb * 1024d * 1024d * 1024d;
            long limitBytes = requestedBytes >= long.MaxValue
                ? long.MaxValue : (long)requestedBytes;
            var folders = MonthFolders(archiveRoot);
            long totalSize = folders.Sum(folder => GetDirectorySize(folder));
            foreach (DirectoryInfo folder in folders)
            {
                if (totalSize <= limitBytes)
                    break;
                long size = GetDirectorySize(folder);
                if (DeleteMonth(folder, "Archive deleted by size: "))
                    totalSize -= size;
            }
        }

        private static bool DeleteMonth(DirectoryInfo folder, string reason)
        {
            try
            {
                folder.Delete(true);
                LogManager.Write(reason + folder.Name);
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Write("Archive deletion failed: " + folder.Name + "; " + ex.Message);
                return false;
            }
        }

        private static long GetDirectorySize(DirectoryInfo dir)
        {
            try
            {
                return dir.GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch (Exception ex)
            {
                LogManager.Write("Archive size unavailable: " + dir.Name + "; " + ex.Message);
                return 0;
            }
        }

        public static string GetMonthFolder()
        {
            string folder = Path.Combine(ArchiveRoot,
                DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
