using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PhotoAgent
{
    public sealed class ReadyPhoto
    {
        public string status { get; set; }
        public string request_id { get; set; }
        public string document_number { get; set; }
        public int sequence { get; set; }
        public string photo_type { get; set; }
        public string file { get; set; }
        public string relative_path { get; set; }
    }

    // All disk operations are local. 1C reads immutable JPEGs independently;
    // neither camera capture nor the next photo waits for the network copy.
    internal sealed class PhotoTransfer : IDisposable
    {
        internal const string DataRoot = @"C:\psa_photos";
        private readonly CaptureCommand command;
        private readonly string responsesFolder;
        private readonly string transferFolder;
        private readonly string dataRoot;
        private readonly Timer retryTimer;
        private readonly List<ReadyPhoto> photos = new List<ReadyPhoto>();
        private readonly Queue<ReadyPhoto> pending = new Queue<ReadyPhoto>();
        private string lastPublishError;
        private bool disposed;

        public IList<ReadyPhoto> Photos { get { return photos.AsReadOnly(); } }

        internal PhotoTransfer(CaptureCommand command, string responsesFolder, string dataRoot)
        {
            this.command = command;
            this.responsesFolder = responsesFolder;
            this.dataRoot = dataRoot;
            transferFolder = Path.Combine(dataRoot, "transfer", command.request_id);
            TransferCache.Pin(dataRoot, command.request_id);
            retryTimer = new Timer { Interval = 500 };
            retryTimer.Tick += delegate { TryFlush(); };
            retryTimer.Start();
        }

        // A surviving outbox means a previous process captured part of this
        // request. Never overwrite JPEGs that 1C may still be reading.
        internal bool RecoverInterrupted()
        {
            if (!Directory.Exists(transferFolder))
                return false;

            foreach (string receipt in Directory.GetFiles(transferFolder, "photo.*.json*")
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".json.pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ReadyPhoto photo = new JavaScriptSerializer().Deserialize<ReadyPhoto>(
                    File.ReadAllText(receipt));
                if (photo == null || photo.status != "photo_ready"
                    || photo.request_id != command.request_id
                    || photo.document_number != command.document_number
                    || photo.file != Path.GetFileName(photo.file)
                    || string.IsNullOrWhiteSpace(photo.file)
                    || photo.relative_path != Path.Combine("transfer", command.request_id, photo.file))
                    throw new InvalidDataException("Повреждён журнал передачи фотографий");

                if (receipt.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(Path.Combine(transferFolder, photo.file)))
                    continue;
                if (photo.sequence != photos.Count + 1)
                    throw new InvalidDataException("Повреждена последовательность передачи фотографий");
                photos.Add(photo);
                pending.Enqueue(photo);
            }
            return true;
        }

        internal void PhotoSaved(string savedFile, string photoType)
        {
            int sequence = photos.Count + 1;
            string name = Path.GetFileName(savedFile);
            var photo = new ReadyPhoto
            {
                status = "photo_ready",
                request_id = command.request_id,
                document_number = command.document_number,
                sequence = sequence,
                photo_type = photoType,
                file = name,
                relative_path = Path.Combine("transfer", command.request_id, name)
            };
            TransferCache.StorePhoto(dataRoot, command.request_id, savedFile, sequence,
                new JavaScriptSerializer().Serialize(photo));
            photos.Add(photo);
            pending.Enqueue(photo);
            TryFlush();
        }

        private void TryFlush()
        {
            try
            {
                Flush();
                lastPublishError = null;
            }
            catch (IOException ex) { LogPending(ex); }
            catch (UnauthorizedAccessException ex) { LogPending(ex); }
        }

        private void LogPending(Exception ex)
        {
            if (lastPublishError != ex.Message)
                LogManager.Write("Photo notification pending; will retry: " + ex.Message);
            lastPublishError = ex.Message;
        }

        internal void Flush()
        {
            while (pending.Count != 0)
            {
                ReadyPhoto photo = pending.Peek();
                string json = new JavaScriptSerializer().Serialize(photo);
                string suffix = "photo." + photo.sequence.ToString("D4") + ".json";
                // Keep a durable local receipt before publishing the notification.
                TransferCache.CommitReceipt(Path.Combine(transferFolder, suffix));
                WriteJsonOnce(Path.Combine(responsesFolder, command.request_id + "." + suffix), json);
                pending.Dequeue();
                LogManager.Write("Photo ready: " + command.request_id + " #" + photo.sequence);
            }
        }

        private static void WriteJsonOnce(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
                return;
            string temporaryFile = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryFile, json);
                File.Move(temporaryFile, path);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                    File.Delete(temporaryFile);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            retryTimer.Stop();
            retryTimer.Dispose();
            TransferCache.Unpin(dataRoot, command.request_id);
        }
    }
}
