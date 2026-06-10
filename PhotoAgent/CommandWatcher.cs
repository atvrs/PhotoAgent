using System;
using System.IO;
using System.Threading;

namespace PhotoAgent
{
    public class CommandWatcher
    {
        private readonly string commandsFolder;
        private FileSystemWatcher watcher;

        public CommandWatcher(string folder)
        {
            commandsFolder = folder;
        }

        public void Start()
        {
            watcher = new FileSystemWatcher();

            watcher.Path = commandsFolder;
            watcher.Filter = "*.json";

            watcher.NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.CreationTime;

            watcher.Created += Watcher_Created;

            watcher.EnableRaisingEvents = true;

            LogManager.Write(
                "CommandWatcher started"
            );
        }

        private void Watcher_Created(
            object sender,
            FileSystemEventArgs e)
        {
            LogManager.Write(
                "File detected: " + e.Name
            );
            try
            {
                string fileName = e.FullPath;

                ThreadPool.QueueUserWorkItem(
                    _ =>
                    {
                        try
                        {
                            Thread.Sleep(500);

                            CommandProcessor.Process(
                                fileName
                            );
                        }
                        catch (Exception ex)
                        {
                            LogManager.Write(
                                "Process thread error: "
                                + ex.Message
                            );
                        }
                    }
                );
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "Watcher error: "
                    + ex.Message
                );
            }
        }
    }
}