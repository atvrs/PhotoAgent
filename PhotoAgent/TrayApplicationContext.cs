using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Reflection;

namespace PhotoAgent
{
    public class TrayApplicationContext : ApplicationContext
    {
        private NotifyIcon trayIcon;

        private Timer watchdogTimer;

        private CommandWatcher commandWatcher;

        
        private readonly string rootFolder =
            @"C:\psa_photos";

        private Timer maintenanceTimer;

        private void OpenCurrentLog(
    object sender,
    EventArgs e)
        {
            try
            {
                string file =
                    Path.Combine(
                        @"C:\psa_photos\logs",
                        DateTime.Now.ToString(
                            "yyyy-MM"
                        ) + ".log"
                    );

                if (!File.Exists(file))
                {
                    MessageBox.Show(
                        "Журнал ещё не создан."
                    );

                    return;
                }

                Process.Start(
                    "notepad.exe",
                    file
                );
            }
            catch
            {
            }
        }

        private void MaintenanceTimer_Tick(
    object sender,
    EventArgs e)
        {
            try
            {
                ArchiveManager.RunMaintenance();

                LogManager.RunMaintenance();
            }
            catch
            {
            }
        }
        private void WatchdogTimer_Tick(
    object sender,
    EventArgs e)
        {
            if (!CaptureState.IsBusy)
            {
                return;
            }

            TimeSpan elapsed =
                DateTime.Now -
                CaptureState.Started;

            if (elapsed.TotalSeconds >
                ConfigManager.Current
                    .captureTimeoutSec)
            {
                LogManager.Write(
                    "Capture timeout. Busy reset."
                );

                CaptureState.IsBusy =
                    false;
            }
        }
        private void TestCamera(
     object sender,
     EventArgs e)
        {
            using (CaptureForm form =
    new CaptureForm(
        ConfigManager.Current
            .lomoCameraId,
        "Тест ломосдатчика",
        "Тест ломосдатчика"
    ))
            {
                if (form.ShowDialog() ==
                    DialogResult.OK)
                {
                    string fileName =
                        @"C:\psa_photos\test.jpg";

                    form.CapturedImage.Save(
                        fileName,
                        System.Drawing.Imaging.ImageFormat.Jpeg
                    );

                    MessageBox.Show(
                        "Снимок сохранён:\r\n" +
                        fileName,
                        "PhotoAgent",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
            }
        }
        private void TestDocumentCamera(
    object sender,
    EventArgs e)
        {
            using (CaptureForm form =
    new CaptureForm(
        ConfigManager.Current
            .documentCameraId,
        "Тест документа",
        "Тест документа"
    ))
            {
                if (form.ShowDialog() ==
                    DialogResult.OK)
                {
                    string fileName =
                        @"C:\psa_photos\test_document.jpg";

                    form.CapturedImage.Save(
                        fileName,
                        System.Drawing.Imaging.ImageFormat.Jpeg
                    );

                    MessageBox.Show(
                        "Снимок сохранён:\r\n" +
                        fileName
                    );
                }
            }
        }
       public TrayApplicationContext()
        {
            CreateFolders();

            AutoStartManager.EnsureAutoStart();

            ConfigManager.Load();

            ArchiveManager.RunMaintenance();

            
            LogManager.Write(
                "PhotoAgent started"
            );

            if (string.IsNullOrWhiteSpace(
        ConfigManager.Current.lomoCameraId)
    ||
    string.IsNullOrWhiteSpace(
        ConfigManager.Current.documentCameraId))
            {
                bool configured =
                    CameraConfigurator.Run();

                if (!configured)
                {
                    Application.Exit();

                    return;
                }
            }

            foreach (CameraInfo camera in
    CameraManager.GetCameras())
            {
                LogManager.Write(
                    "Camera found: "
                    + camera.Name
                );
            }
            LogManager.Write(
    "Preview size: "
    + ConfigManager.Current.previewWidth
    + "x"
    + ConfigManager.Current.previewHeight
);

            commandWatcher =
                new CommandWatcher(
                    Path.Combine(
                        rootFolder,
                        "commands"
                    )
                );

            commandWatcher.Start();

            maintenanceTimer =
    new Timer();

            maintenanceTimer.Interval =
                60 * 1000;

            maintenanceTimer.Tick +=
                MaintenanceTimer_Tick;

            maintenanceTimer.Start();

            watchdogTimer =
    new Timer();

            watchdogTimer.Interval =
                5000;

            watchdogTimer.Tick +=
                WatchdogTimer_Tick;

            watchdogTimer.Start();

            var menu = new ContextMenuStrip();

            menu.Items.Add(
                "Открыть папку",
                null,
                OpenFolder
            );
        
            menu.Items.Add(
                "Тест камеры ломосдатчика",
                null,
                TestCamera
            );
            menu.Items.Add(
    "Тест камеры документа",
    null,
    TestDocumentCamera
);
            menu.Items.Add(
    "Переназначить камеры",
    null,
    ReassignCameras
);
            menu.Items.Add(
    "Текущий журнал",
    null,
    OpenCurrentLog
);
            menu.Items.Add(
    "О программе",
    null,
    AboutApplication
);
            menu.Items.Add(
                "Выход",
                null,
                ExitApplication
            );

            string iconFile =
    Path.Combine(
        Application.StartupPath,
        "PhotoAgent.ico"
    );

            Icon trayAppIcon =
                File.Exists(iconFile)
                    ? new Icon(iconFile)
                    : SystemIcons.Application;

            trayIcon = new NotifyIcon
            {
                Icon = trayAppIcon,
                ContextMenuStrip = menu,
                Visible = true,
                Text = "PhotoAgent"
            };

            trayIcon.ShowBalloonTip(
                2000,
                "PhotoAgent",
                "Сервис запущен",
                ToolTipIcon.Info
            );
        }

        private void AboutApplication(
    object sender,
    EventArgs e)
        {
            MessageBox.Show(
                "PhotoAgent\r\nВерсия 1.0. Андрей Токмачёв.",
                "О программе",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        private void OpenLogFolder(
    object sender,
    EventArgs e)
        {
            Process.Start(
                "explorer.exe",
                Path.Combine(
                    rootFolder,
                    "logs"
                )
            );
        }
        private void ReassignCameras(
    object sender,
    EventArgs e)
        {
            DialogResult result =
                MessageBox.Show(
                    "Переназначить камеры?",
                    "PhotoAgent",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

            if (result != DialogResult.Yes)
            {
                return;
            }

            ConfigManager.Current
                .lomoCameraId = "";

            ConfigManager.Current
                .documentCameraId = "";

            ConfigManager.Save();

            bool configured =
                CameraConfigurator.Run();

            if (configured)
            {
                MessageBox.Show(
                    "Настройка камер завершена.",
                    "PhotoAgent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }
        private void CreateFolders()
        {
            Directory.CreateDirectory(rootFolder);

            Directory.CreateDirectory(
                Path.Combine(rootFolder, "commands")
            );

            Directory.CreateDirectory(
                Path.Combine(rootFolder, "photos")
            );

            Directory.CreateDirectory(
                Path.Combine(rootFolder, "responses")
            );

            Directory.CreateDirectory(
                Path.Combine(rootFolder, "archive")
            );

            Directory.CreateDirectory(
                Path.Combine(rootFolder, "logs")
            );
        }

        private void OpenFolder(
            object sender,
            EventArgs e)
        {
            Process.Start(
                "explorer.exe",
                rootFolder
            );
        }

        private void ExitApplication(
    object sender,
    EventArgs e)
        {
            LogManager.Write(
                "PhotoAgent stopped"
            );

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            if (watchdogTimer != null)
            {
                watchdogTimer.Stop();
                watchdogTimer.Dispose();
            }

            Application.Exit();
        }
    }
}