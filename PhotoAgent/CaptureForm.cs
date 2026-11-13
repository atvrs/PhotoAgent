using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhotoAgent
{
    public class CaptureForm : Form
    {
        private readonly CameraPreviewControl preview;
        private readonly Button captureButton;
        private readonly bool ownsPreview;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public Bitmap CapturedImage { get; private set; }

        public CaptureForm(string cameraId, string title, string infoText)
            : this(new CameraPreviewControl(cameraId), title, infoText, true)
        {
        }

        internal CaptureForm(CameraPreviewControl preview, string title, string infoText)
            : this(preview, title, infoText, false)
        {
        }

        private CaptureForm(CameraPreviewControl preview, string title, string infoText, bool ownsPreview)
        {
            this.preview = preview;
            this.ownsPreview = ownsPreview;
            Text = title;
            string iconFile = Path.Combine(Application.StartupPath, "PhotoAgent.ico");
            if (File.Exists(iconFile))
                Icon = new Icon(iconFile);

            Width = 900;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            TopMost = true;
            KeyPreview = true;
            ShowInTaskbar = true;

            preview.ReadyChanged += Preview_ReadyChanged;
            var infoLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 120,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
                Text = infoText
            };
            captureButton = new Button
            {
                Text = "Сделать снимок (Пробел / Enter)",
                Height = 60,
                Dock = DockStyle.Bottom,
                Enabled = !preview.IsWaitingForCamera
            };
            captureButton.Click += delegate { CaptureCurrentFrame(); };
            Controls.Add(preview);
            Controls.Add(infoLabel);
            Controls.Add(captureButton);
            Load += CaptureForm_Load;
            KeyDown += CaptureForm_KeyDown;
        }

        private void CaptureForm_Load(object sender, EventArgs e)
        {
            CaptureState.SetWaitingForCamera(preview.IsWaitingForCamera);
            preview.Start();
            Preview_ReadyChanged(preview, EventArgs.Empty);
            WindowState = FormWindowState.Normal;
            SetForegroundWindow(Handle);
            BringToFront();
            Activate();
            Focus();
        }

        private void Preview_ReadyChanged(object sender, EventArgs e)
        {
            captureButton.Enabled = !preview.IsWaitingForCamera;
            CaptureState.SetWaitingForCamera(preview.IsWaitingForCamera);
        }

        private void CaptureCurrentFrame()
        {
            Bitmap frame = preview.CaptureFrame();
            if (frame == null)
                return;

            CapturedImage = frame;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CaptureForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                CaptureCurrentFrame();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
                return;

            preview.ReadyChanged -= Preview_ReadyChanged;
            if (ownsPreview)
                preview.Stop();
            else
                Controls.Remove(preview);
            CaptureState.SetWaitingForCamera(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                preview.ReadyChanged -= Preview_ReadyChanged;
                if (!ownsPreview)
                    Controls.Remove(preview);
                CapturedImage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
