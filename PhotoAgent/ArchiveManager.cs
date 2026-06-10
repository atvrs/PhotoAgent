using System;
using System.IO;
using System.Linq;

namespace PhotoAgent
{
    public static class ArchiveManager
    {
        private static readonly string ArchiveRoot =
            @"C:\psa_photos\archive";


        public static void ArchiveDocument(
    string documentNumber)
        {
            string sourceFolder =
                Path.Combine(
                    @"C:\psa_photos\photos",
                    documentNumber
                );

            if (!Directory.Exists(
                sourceFolder))
            {
                return;
            }

            string archiveFolder =
                Path.Combine(
                    GetMonthFolder(),
                    documentNumber
                );

            if (Directory.Exists(
                archiveFolder))
            {
                Directory.Delete(
                    archiveFolder,
                    true
                );
            }

            CopyDirectory(
                sourceFolder,
                archiveFolder
            );

            LogManager.Write(
                "Archived: "
                + documentNumber
            );
        }

        private static void CopyDirectory(
            string source,
            string target)
        {
            Directory.CreateDirectory(
                target
            );

            foreach (string file in
                Directory.GetFiles(source))
            {
                string targetFile =
                    Path.Combine(
                        target,
                        Path.GetFileName(file)
                    );

                File.Copy(
                    file,
                    targetFile,
                    true
                );
            }

            foreach (string dir in
                Directory.GetDirectories(source))
            {
                string targetDir =
                    Path.Combine(
                        target,
                        Path.GetFileName(dir)
                    );

                CopyDirectory(
                    dir,
                    targetDir
                );
            }
        }
        public static void RunMaintenance()
        {
            try
            {
                Directory.CreateDirectory(
                    ArchiveRoot
                );

                CleanupOldFolders();

                CleanupBySize();
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "Archive maintenance error: "
                    + ex.Message
                );
            }
        }

        private static void CleanupOldFolders()
        {
            DateTime limitDate =
                DateTime.Now.AddDays(
                    -ConfigManager.Current.archiveRetentionDays
                );

            foreach (string folder in
                Directory.GetDirectories(
                    ArchiveRoot
                ))
            {
                try
                {
                    DirectoryInfo info =
                        new DirectoryInfo(folder);

                    if (info.CreationTime <
                        limitDate)
                    {
                        Directory.Delete(
                            folder,
                            true
                        );

                        LogManager.Write(
                            "Archive deleted: "
                            + info.Name
                        );
                    }
                }
                catch
                {
                }
            }
        }

        private static void CleanupBySize()
        {
            long limitBytes =
                (long)(
                    ConfigManager.Current.archiveMaxGb
                    * 1024d
                    * 1024d
                    * 1024d
                );

            DirectoryInfo root =
                new DirectoryInfo(
                    ArchiveRoot
                );

            long totalSize =
                GetDirectorySize(root);

            if (totalSize <= limitBytes)
                return;

            var folders =
                root.GetDirectories()
                    .OrderBy(
                        f => f.CreationTime
                    )
                    .ToList();

            foreach (var folder in folders)
            {
                if (totalSize <= limitBytes)
                    break;

                try
                {
                    long size =
                        GetDirectorySize(folder);

                    folder.Delete(true);

                    totalSize -= size;

                    LogManager.Write(
                        "Archive deleted by size: "
                        + folder.Name
                    );
                }
                catch
                {
                }
            }
        }

        private static long GetDirectorySize(
            DirectoryInfo dir)
        {
            try
            {
                return dir
                    .GetFiles(
                        "*",
                        SearchOption.AllDirectories
                    )
                    .Sum(
                        f => f.Length
                    );
            }
            catch
            {
                return 0;
            }
        }

        public static string GetMonthFolder()
        {
            string folder =
                Path.Combine(
                    ArchiveRoot,
                    DateTime.Now.ToString(
                        "yyyy-MM"
                    )
                );

            Directory.CreateDirectory(
                folder
            );

            return folder;
        }
    }
}