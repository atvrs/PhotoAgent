using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PhotoAgent
{
    internal static class TransferCache
    {
        private const long Megabyte = 1024L * 1024L;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, int> Pins =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private sealed class Entry
        {
            internal string Path;
            internal string RequestId;
            internal List<FileInfo> Files = new List<FileInfo>();
            internal long Bytes;
            internal DateTime OldestUtc;
            internal bool Safe = true;
        }

        internal static string ExpiredNotice(string dataRoot, string requestId)
        {
            return Path.Combine(dataRoot, "responses", requestId + ".expired.json");
        }

        private static string RequestFolder(string dataRoot, string requestId)
        {
            return Path.GetFullPath(Path.Combine(dataRoot, "transfer", requestId));
        }

        internal static void Pin(string dataRoot, string requestId)
        {
            lock (Sync)
            {
                string path = RequestFolder(dataRoot, requestId);
                int count;
                Pins.TryGetValue(path, out count);
                Pins[path] = count + 1;
            }
        }

        internal static void Unpin(string dataRoot, string requestId)
        {
            lock (Sync)
            {
                string path = RequestFolder(dataRoot, requestId);
                int count;
                if (!Pins.TryGetValue(path, out count)) return;
                if (count == 1) Pins.Remove(path);
                else Pins[path] = count - 1;
            }
        }

        private static long MaxBytes(Config config)
        {
            double mb = config.transferMaxMb;
            if (mb <= 0 || double.IsNaN(mb) || double.IsInfinity(mb))
                mb = Config.DefaultTransferMaxMb;
            double bytes = mb * Megabyte;
            return bytes >= long.MaxValue ? long.MaxValue : Math.Max(1L, (long)bytes);
        }

        internal static void RunMaintenance()
        {
            RunMaintenance(PhotoTransfer.DataRoot, ConfigManager.Current, DateTime.UtcNow);
        }

        internal static void RunMaintenance(string dataRoot, Config config, DateTime nowUtc)
        {
            lock (Sync)
            {
                try
                {
                    if (!Clean(dataRoot, config, nowUtc, 0, null))
                        LogManager.Write("Transfer cache exceeds its limit; active or locked data is preserved");
                }
                catch (Exception ex)
                {
                    LogManager.Write("Transfer maintenance error: " + ex.Message);
                }
            }
        }

        internal static void StorePhoto(string dataRoot, string requestId, string sourceFile,
            int sequence, string receiptJson)
        {
            byte[] receipt = new UTF8Encoding(false).GetBytes(receiptJson);
            string folder = RequestFolder(dataRoot, requestId);
            string destination = Path.Combine(folder, Path.GetFileName(sourceFile));
            string pending = Path.Combine(folder, "photo."
                + sequence.ToString("D4", CultureInfo.InvariantCulture) + ".json.pending");
            lock (Sync)
            using (var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (File.Exists(ExpiredNotice(dataRoot, requestId)))
                    throw new IOException("Временные файлы этого задания уже удалены из transfer");
                if (File.Exists(destination) || File.Exists(pending))
                    throw new IOException("Снимок задания уже существует и не будет перезаписан");
                long required = checked(source.Length + receipt.LongLength);
                Config config = ConfigManager.Current;
                if (required > MaxBytes(config)
                    || !Clean(dataRoot, config, DateTime.UtcNow, required, folder))
                    throw new IOException("Недостаточно места в transfer. Лимит "
                        + config.transferMaxMb.ToString(CultureInfo.InvariantCulture)
                        + " МБ; активные и читаемые файлы сохраняются.");

                Directory.CreateDirectory(folder);
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                bool createdReceipt = false, saved = false;
                try
                {
                    // The pending receipt reserves its real bytes before the JPEG
                    // is copied. Promoting it later cannot exceed the cache quota.
                    using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        createdReceipt = true;
                        output.Write(receipt, 0, receipt.Length);
                    }
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        source.CopyTo(output);
                    File.Move(temporary, destination);
                    saved = true;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (!saved && createdReceipt && File.Exists(pending)) File.Delete(pending);
                }
            }
        }

        internal static void CommitReceipt(string path)
        {
            lock (Sync)
            {
                if (!File.Exists(path))
                    File.Move(path + ".pending", path);
            }
        }

        private static bool Clean(string dataRoot, Config config, DateTime nowUtc,
            long required, string excluded)
        {
            string root = Path.Combine(dataRoot, "transfer");
            Directory.CreateDirectory(root);
            int months = config.transferRetentionMonths > 0
                ? config.transferRetentionMonths : Config.DefaultTransferRetentionMonths;
            int elapsedMonths = (nowUtc.Year - 1) * 12 + nowUtc.Month - 1;
            DateTime cutoff = months > elapsedMonths ? DateTime.MinValue : nowUtc.AddMonths(-months);
            foreach (Entry entry in Scan(root))
                if (entry.OldestUtc < cutoff && CanDelete(entry, excluded))
                    DeleteEntry(dataRoot, entry, "age", nowUtc);

            Entry[] entries = Scan(root);
            long total = entries.Sum(entry => entry.Bytes);
            long max = MaxBytes(config);
            foreach (Entry entry in entries.OrderBy(item => item.OldestUtc)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (total <= max - required) break;
                if (CanDelete(entry, excluded))
                {
                    long before = entry.Bytes;
                    DeleteEntry(dataRoot, entry, "size", nowUtc);
                    total -= Math.Max(0, before - RemainingBytes(entry));
                }
            }
            return entries.All(entry => entry.Safe) && total <= max - required;
        }

        private static bool CanDelete(Entry entry, string excluded)
        {
            return entry.Safe && !Pins.ContainsKey(entry.Path)
                && !string.Equals(entry.Path, excluded, StringComparison.OrdinalIgnoreCase);
        }

        private static Entry[] Scan(string root)
        {
            var entries = new List<Entry>();
            foreach (DirectoryInfo directory in new DirectoryInfo(root).GetDirectories())
            {
                var entry = new Entry { Path = directory.FullName, RequestId = directory.Name,
                    OldestUtc = directory.LastWriteTimeUtc };
                Collect(directory, entry);
                SetSizeAndAge(entry);
                entries.Add(entry);
            }
            foreach (FileInfo file in new DirectoryInfo(root).GetFiles())
            {
                var entry = new Entry { Path = file.FullName, OldestUtc = file.LastWriteTimeUtc };
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) entry.Safe = false;
                else entry.Files.Add(file);
                SetSizeAndAge(entry);
                entries.Add(entry);
            }
            return entries.ToArray();
        }

        private static void Collect(DirectoryInfo directory, Entry entry)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                entry.Safe = false;
                return;
            }
            foreach (FileInfo file in directory.GetFiles())
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) entry.Safe = false;
                else entry.Files.Add(file);
            foreach (DirectoryInfo child in directory.GetDirectories()) Collect(child, entry);
        }

        private static void SetSizeAndAge(Entry entry)
        {
            entry.Bytes = entry.Files.Sum(file => file.Length);
            if (entry.Files.Count != 0) entry.OldestUtc = entry.Files.Min(file => file.LastWriteTimeUtc);
        }

        private static long RemainingBytes(Entry entry)
        {
            return entry.Files.Where(file => File.Exists(file.FullName))
                .Sum(file => new FileInfo(file.FullName).Length);
        }

        private static void DeleteEntry(string dataRoot, Entry entry, string reason, DateTime nowUtc)
        {
            var guards = new List<FileStream>();
            try
            {
                // Deny new readers and writers, but allow our deletion. If 1C
                // already has any file open, this fails before any file is removed.
                foreach (FileInfo file in entry.Files)
                {
                    if (file.IsReadOnly) return;
                    guards.Add(new FileStream(file.FullName, FileMode.Open,
                        FileAccess.Read, FileShare.Delete));
                }
                if (entry.RequestId != null)
                {
                    string notice = ExpiredNotice(dataRoot, entry.RequestId);
                    Directory.CreateDirectory(Path.GetDirectoryName(notice));
                    if (!File.Exists(notice))
                    {
                        string temporary = notice + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(new
                            {
                                status = "transfer_expired",
                                request_id = entry.RequestId,
                                reason = reason,
                                expired_at = nowUtc.ToString("o", CultureInfo.InvariantCulture),
                                message = "Временные файлы задания удалены из transfer по сроку или объёму"
                            }));
                            File.Move(temporary, notice);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    foreach (FileInfo file in entry.Files) File.Delete(file.FullName);
                }
                else File.Delete(entry.Path);
                LogManager.Write("Transfer cache removed (" + reason + "): " + entry.Path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { foreach (FileStream guard in guards) guard.Dispose(); }
            if (entry.RequestId != null)
            {
                try { RemoveEmptyDirectories(new DirectoryInfo(entry.Path)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void RemoveEmptyDirectories(DirectoryInfo directory)
        {
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (DirectoryInfo child in directory.GetDirectories()) RemoveEmptyDirectories(child);
            if (directory.GetFileSystemInfos().Length == 0) directory.Delete();
        }
    }
}
