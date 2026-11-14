using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PhotoAgent
{
    public class ExtraCaptureForm : Form
    {
        private readonly CameraPreviewControl preview;
        private readonly Label infoLabel;
        private readonly string documentNumber;
        private readonly string folder;
        private readonly Action<string, string> photoReady;
        private int photoCounter = 1;
        public string ErrorMessage { get; private set; }

        public ExtraCaptureForm(string cameraId, string documentNumber, string folder)
            : this(cameraId, documentNumber, folder, null)
        {
        }

        internal ExtraCaptureForm(string cameraId, string documentNumber, string folder,
            Action<string, string> photoReady)
        {
            this.documentNumber = documentNumber;
            this.folder = folder;
            this.photoReady = photoReady;
            Text = "Дополнительные снимки";
            string iconFile = Path.Combine(Application.StartupPath, "PhotoAgent.ico");
            if (File.Exists(iconFile))
                Icon = new Icon(iconFile);

            Width = 900;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            KeyPreview = true;

            preview = new CameraPreviewControl(cameraId);
            preview.ReadyChanged += delegate
            {
                CaptureState.SetWaitingForCamera(preview.IsWaitingForCamera);
            };
            infoLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 150,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 14, FontStyle.Bold)
            };
            UpdateInfoText();
            Controls.Add(preview);
            Controls.Add(infoLabel);
            Load += delegate
            {
                CaptureState.SetWaitingForCamera(true);
                preview.Start();
            };
            KeyDown += ExtraCaptureForm_KeyDown;
        }

        private void ExtraCaptureForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                SaveCurrentPhoto();
            }
        }

        private void SaveCurrentPhoto()
        {
            try
            {
                using (Bitmap frame = preview.CaptureFrame())
                {
                    if (frame == null)
                        return;

                    string fileName = Path.Combine(folder,
                        documentNumber + "_extra_" + photoCounter.ToString("D3") + ".jpg");
                    frame.Save(fileName, ImageFormat.Jpeg);
                    if (photoReady != null)
                        photoReady(fileName, "extra");
                    ArchiveManager.ArchiveDocument(documentNumber);
                    LogManager.Write("Saved extra: " + fileName);
                }

                if (SessionManager.Current != null)
                    SessionManager.Current.ExtraPhotoCount++;
                photoCounter++;
                UpdateInfoText();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                LogManager.Write("Extra photo error: " + ex.Message);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
                return;

            preview.Stop();
            CaptureState.SetWaitingForCamera(false);
        }

        private void UpdateInfoText()
        {
            infoLabel.Text = "Дополнительный снимок"
                + "\r\n\r\nДокумент: " + documentNumber
                + "\r\nСохранено снимков: " + (photoCounter - 1)
                + "\r\n\r\nEnter / Пробел - сохранить"
                + "\r\nEsc - завершить";
        }
    }
}
