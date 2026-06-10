using System;
using System.Drawing;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;



namespace PhotoAgent
{
    public class CameraAssignmentForm : Form
    {
        private PictureBox previewBox;

        private Button btnLomo;
        private Button btnDocument;
        private Button btnSkip;

        private Label lblStatus;

        private VideoCaptureDevice camera;

        public string SelectedRole
        {
            get;
            private set;
        }

        public CameraAssignmentForm(
            CameraInfo cameraInfo,
            int current,
            int total,
            bool lomoAssigned,
            bool documentAssigned)
        {
            Text =
                "Настройка камер";

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

            lblStatus =
                new Label();

            lblStatus.Dock =
                DockStyle.Top;

            lblStatus.Height = 70;

            lblStatus.TextAlign =
                ContentAlignment.MiddleCenter;

            lblStatus.Font =
                new Font(
                    Font.FontFamily,
                    12,
                    FontStyle.Bold);

            lblStatus.Text =
                "Камера "
                + current
                + " из "
                + total
                + "\r\n"
                + "Ломосдатчик: "
                + (lomoAssigned ? "✓" : "✗")
                + "    Документ: "
                + (documentAssigned ? "✓" : "✗");

            Controls.Add(
                lblStatus
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

            FlowLayoutPanel panel =
                new FlowLayoutPanel();

            panel.Dock =
                DockStyle.Bottom;

            panel.Height = 80;

            btnLomo =
                new Button();

            btnLomo.Text =
                "Ломосдатчик";

            btnLomo.Width = 200;

            btnLomo.Enabled =
                !lomoAssigned;

            btnLomo.Click +=
                BtnLomo_Click;

            panel.Controls.Add(
                btnLomo
            );

            btnDocument =
                new Button();

            btnDocument.Text =
                "Документ";

            btnDocument.Width = 200;

            btnDocument.Enabled =
                !documentAssigned;

            btnDocument.Click +=
                BtnDocument_Click;

            panel.Controls.Add(
                btnDocument
            );

            btnSkip =
                new Button();

            btnSkip.Text =
                "Не использовать";

            btnSkip.Width = 200;

            btnSkip.Click +=
                BtnSkip_Click;

            panel.Controls.Add(
                btnSkip
            );

            Controls.Add(
                panel
            );

            camera =
                new VideoCaptureDevice(
                    cameraInfo.MonikerString
                );

            camera.NewFrame +=
                Camera_NewFrame;

            Load +=
                CameraAssignmentForm_Load;

            FormClosing +=
                CameraAssignmentForm_FormClosing;
            FormClosing +=
    CameraAssignmentForm_FormClosing2;
        }

        private void CameraAssignmentForm_Load(
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

        private void BtnLomo_Click(
            object sender,
            EventArgs e)
        {
            SelectedRole =
                "LOMO";

            DialogResult =
                DialogResult.OK;

            Close();
        }

        private void BtnDocument_Click(
            object sender,
            EventArgs e)
        {
            SelectedRole =
                "DOCUMENT";

            DialogResult =
                DialogResult.OK;

            Close();
        }

        private void BtnSkip_Click(
            object sender,
            EventArgs e)
        {
            SelectedRole =
                "SKIP";

            DialogResult =
                DialogResult.OK;

            Close();
        }

        private void CameraAssignmentForm_FormClosing(
            object sender,
            FormClosingEventArgs e)
        {
            if (camera != null &&
                camera.IsRunning)
            {
                camera.SignalToStop();
                camera.WaitForStop();
            }
        }

      

              
        private void CameraAssignmentForm_FormClosing2(
    object sender,
    FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
            {
                DialogResult =
                    DialogResult.Cancel;
            }
        }
    }
}