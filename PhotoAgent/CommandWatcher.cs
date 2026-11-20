using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PhotoAgent
{
    public sealed class CommandWatcher : IDisposable
    {
        private readonly string commandsFolder;
        private readonly Func<string, bool> processCommand;
        private readonly Dictionary<string, PendingFile> pendingFiles =
            new Dictionary<string, PendingFile>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> processingFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Timer timer;
        private bool disposed;
        private string lastScanError;

        public CommandWatcher(string folder)
            : this(folder, CommandProcessor.Process)
        {
        }

        internal CommandWatcher(string folder, Func<string, bool> processor)
        {
            commandsFolder = folder;
            processCommand = processor;
        }

        public void Start()
        {
            if (disposed)
                throw new ObjectDisposedException("CommandWatcher");

            if (timer != null)
                return;

            Directory.CreateDirectory(commandsFolder);
            // The WinForms timer also keeps capture dialogs on the application's STA thread.
            timer = new Timer { Interval = 500 };
            timer.Tick += delegate { ScanCommands(); };
            timer.Start();
            LogManager.Write("Command polling started: " + commandsFolder);
        }

        internal void ScanCommands()
        {
            if (disposed)
                return;

            try
            {
                Directory.CreateDirectory(commandsFolder);
                string[] files = Directory.GetFiles(commandsFolder, "*.json");
                var existingFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                foreach (string missing in pendingFiles.Keys.Where(
                    path => !existingFiles.Contains(path)).ToArray())
                {
                    pendingFiles.Remove(missing);
                }

                lastScanError = null;
                foreach (string file in files)
                {
                    if (disposed)
                        return;

                    // ShowDialog pumps messages, so another timer tick can arrive during capture.
                    if (processingFiles.Contains(file))
                        continue;

                    ProcessFile(file);
                }
            }
            catch (Exception ex)
            {
                if (lastScanError != ex.Message)
                    LogManager.Write("Command scan error: " + ex.Message);
                lastScanError = ex.Message;
            }
        }

        private void ProcessFile(string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists)
                return;

            PendingFile pending;
            if (!pendingFiles.TryGetValue(file, out pending)
                || pending.Length != info.Length
                || pending.LastWriteTimeUtc != info.LastWriteTimeUtc)
            {
                pendingFiles[file] = new PendingFile
                {
                    Length = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc
                };
                return;
            }

            processingFiles.Add(file);
            try
            {
                if (!processCommand(file) && !pending.WaitingLogged)
                {
                    LogManager.Write("Command not ready; will retry: " + Path.GetFileName(file));
                    pending.WaitingLogged = true;
                }
            }
            catch (Exception ex)
            {
                if (!pending.WaitingLogged)
                    LogManager.Write("Command processing error: " + ex.Message);
                pending.WaitingLogged = true;
            }
            finally
            {
                processingFiles.Remove(file);
            }
        }

        public void Dispose()
        {
            disposed = true;
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
        }

        private sealed class PendingFile
        {
            public long Length;
            public DateTime LastWriteTimeUtc;
            public bool WaitingLogged;
        }
    }
}
