using System;
using System.Drawing;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using System.Runtime.InteropServices;

namespace PhotoAgent
{
    public class CaptureForm : Form
    {
        private PictureBox previewBox;

        private Label infoLabel;

        private Button captureButton;

        private VideoCaptureDevice camera;

        private Bitmap currentFrame;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(
    IntPtr hWnd);
        public Bitmap CapturedImage
        {
            get;
            private set;
        }

        public CaptureForm(
    string cameraId,
    string title,
    string infoText)
        {
            Text = title;

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

            infoLabel.Height = 120;

            infoLabel.TextAlign =
                ContentAlignment.MiddleCenter;

            infoLabel.Font =
                new Font(
                    Font.FontFamily,
                    14,
                    FontStyle.Bold
                );

            infoLabel.Text =
                infoText;

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

            captureButton =
                new Button();

            captureButton.Text =
                "Сделать снимок (Пробел / Enter)";

            captureButton.Height = 60;

            captureButton.Dock =
                DockStyle.Bottom;

            captureButton.Click +=
                CaptureButton_Click;

            ShowInTaskbar = true;

            FormBorderStyle =
                FormBorderStyle.FixedDialog;

            Controls.Add(
                captureButton
            );

            Load +=
                CaptureForm_Load;

            FormClosing +=
                CaptureForm_FormClosing;

            KeyDown +=
                CaptureForm_KeyDown;

            OpenCamera(
                cameraId
            );
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

                LogManager.Write(
                    "Selected: "
                    + best.FrameSize.Width
                    + "x"
                    + best.FrameSize.Height
                );
            }

            camera.NewFrame +=
                Camera_NewFrame;
        }

        private void CaptureForm_Load(
            object sender,
            EventArgs e)
        {
            camera.Start();

            WindowState =
                FormWindowState.Normal;

            SetForegroundWindow(
                Handle
            );

            BringToFront();

            Activate();

            TopMost = true;

            TopMost = false;

            TopMost = true;

            Focus();
        }

        private void Camera_NewFrame(
            object sender,
            NewFrameEventArgs eventArgs)
        {
            Bitmap frame =
                (Bitmap)eventArgs.Frame.Clone();

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

        private void CaptureButton_Click(
            object sender,
            EventArgs e)
        {
            lock (this)
            {
                if (currentFrame != null)
                {
                    CapturedImage =
                        (Bitmap)currentFrame.Clone();
                }
            }

            DialogResult =
                DialogResult.OK;

            Close();
        }

        private void CaptureForm_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.KeyCode ==
                Keys.Escape)
            {
                DialogResult =
                    DialogResult.Cancel;

                Close();

                return;
            }

            if (e.KeyCode ==
                    Keys.Enter ||
                e.KeyCode ==
                    Keys.Space)
            {
                lock (this)
                {
                    if (currentFrame != null)
                    {
                        CapturedImage =
                            (Bitmap)currentFrame.Clone();
                    }
                }

                DialogResult =
                    DialogResult.OK;

                Close();
            }
        }

        private void CaptureForm_FormClosing(
            object sender,
            FormClosingEventArgs e)
        {
            if (camera != null)
            {
                if (camera.IsRunning)
                {
                    camera.SignalToStop();
                    camera.WaitForStop();
                }
            }

            currentFrame?.Dispose();

            if (previewBox.Image != null)
            {
                previewBox.Image.Dispose();
            }
        }
    }
}