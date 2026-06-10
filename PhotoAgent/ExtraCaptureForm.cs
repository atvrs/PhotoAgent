using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;

namespace PhotoAgent
{
    public class ExtraCaptureForm : Form
    {
        private PictureBox previewBox;

        private Label infoLabel;

        private VideoCaptureDevice camera;

        private Bitmap currentFrame;

        private readonly string documentNumber;

        private readonly string folder;

        private int photoCounter = 1;

        public ExtraCaptureForm(
            string cameraId,
            string documentNumber,
            string folder)
        {
            this.documentNumber =
                documentNumber;

            this.folder =
                folder;

            Text =
                "Дополнительные снимки";

            string iconFile =
    System.IO.Path.Combine(
        Application.StartupPath,
        "PhotoAgent.ico"
    );

            if (System.IO.File.Exists(
                iconFile))
            {
                Icon =
                    new Icon(
                        iconFile
                    );
            }

            Width = 900;
            Height = 700;

            StartPosition =
                FormStartPosition.CenterScreen;

            TopMost = true;

            KeyPreview = true;

            infoLabel =
                new Label();

            infoLabel.Dock =
                DockStyle.Top;

            infoLabel.Height = 150;

            infoLabel.TextAlign =
                ContentAlignment.MiddleCenter;

            infoLabel.Font =
                new Font(
                    Font.FontFamily,
                    14,
                    FontStyle.Bold
                );

            UpdateInfoText();

            Controls.Add(
                infoLabel
            );

            previewBox =
                new PictureBox();

            previewBox.Dock =
                DockStyle.Fill;

            previewBox.SizeMode =
                PictureBoxSizeMode.Zoom;

            Controls.Add(
                previewBox
            );

            OpenCamera(
                cameraId
            );

            Load +=
                ExtraCaptureForm_Load;

            FormClosing +=
                ExtraCaptureForm_FormClosing;

            KeyDown +=
                ExtraCaptureForm_KeyDown;
        }

        private void OpenCamera(
            string cameraId)
        {
            camera =
                new VideoCaptureDevice(
                    cameraId
                );

            VideoCapabilities best =
                null;

            long bestPixels = 0;

            foreach (VideoCapabilities cap
                in camera.VideoCapabilities)
            {
                long pixels =
                    (long)cap.FrameSize.Width *
                    cap.FrameSize.Height;

                if (pixels > bestPixels)
                {
                    bestPixels =
                        pixels;

                    best =
                        cap;
                }
            }

            if (best != null)
            {
                camera.VideoResolution =
                    best;
            }

            camera.NewFrame +=
                Camera_NewFrame;
        }

        private void ExtraCaptureForm_Load(
            object sender,
            EventArgs e)
        {
            camera.Start();
        }

        private void Camera_NewFrame(
            object sender,
            NewFrameEventArgs e)
        {
            Bitmap frame =
                (Bitmap)e.Frame.Clone();

            lock (this)
            {
                currentFrame?.Dispose();

                currentFrame =
                    (Bitmap)frame.Clone();
            }

            BeginInvoke(
                new Action(() =>
                {
                    var old =
                        previewBox.Image;

                    previewBox.Image =
                        frame;

                    old?.Dispose();
                }));
        }

        private void ExtraCaptureForm_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.KeyCode ==
                Keys.Escape)
            {
                Close();

                return;
            }

            if (e.KeyCode ==
                    Keys.Enter ||
                e.KeyCode ==
                    Keys.Space)
            {
                SaveCurrentPhoto();
            }
        }

        private void SaveCurrentPhoto()
        {
            lock (this)
            {
                if (currentFrame == null)
                {
                    return;
                }

                string fileName =
                    Path.Combine(
                        folder,
                        documentNumber
                        + "_extra_"
                        + photoCounter.ToString("D3")
                        + ".jpg"
                    );

                using (Bitmap copy =
                    (Bitmap)currentFrame.Clone())
                {
                    copy.Save(
                        fileName,
                        ImageFormat.Jpeg
                    );
                    
                    ArchiveManager.ArchiveDocument(
                        documentNumber
                    );
                }

                LogManager.Write(
                    "Saved extra: "
                    + fileName
                );

                if (SessionManager.Current != null)
                {
                    SessionManager.Current.ExtraPhotoCount++;
                }

                photoCounter++;
                UpdateInfoText();
            }
        }

        private void ExtraCaptureForm_FormClosing(
            object sender,
            FormClosingEventArgs e)
        {
            if (camera != null &&
                camera.IsRunning)
            {
                camera.SignalToStop();
                camera.WaitForStop();
            }

            currentFrame?.Dispose();

            if (previewBox.Image != null)
            {
                previewBox.Image.Dispose();
            }
        }
        private void UpdateInfoText()
        {
            infoLabel.Text =
                "Дополнительный снимок"
                + "\r\n\r\nДокумент: "
                + documentNumber
                + "\r\nСохранено снимков: "
                + (photoCounter - 1)
                + "\r\n\r\nEnter / Пробел - сохранить"
                + "\r\nEsc - завершить";
        }
    }
}