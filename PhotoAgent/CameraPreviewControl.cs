using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;

namespace PhotoAgent
{
    public sealed class CameraPreviewControl : UserControl
    {
        private readonly string cameraId;
        private readonly Func<string, bool> isConnected;
        private readonly Func<string, IVideoSource> createSource;
        private readonly object frameSync = new object();
        private readonly PictureBox previewBox;
        private readonly Label waitingLabel;
        private readonly Timer timer;
        private IVideoSource source;
        private Task stoppingSource;
        private Bitmap currentFrame;
        private bool frameChanged;
        private bool sourceFailed;
        private bool stopped;
        private DateTime nextConnectionCheck = DateTime.MinValue;
        private string lastError;

        public bool IsWaitingForCamera { get; private set; }
        public event EventHandler ReadyChanged;

        public CameraPreviewControl(string cameraId)
            : this(cameraId, CameraManager.IsConnected, CameraManager.CreatePreviewDevice)
        {
        }

        internal CameraPreviewControl(string cameraId, Func<string, bool> isConnected,
            Func<string, IVideoSource> createSource)
        {
            this.cameraId = cameraId;
            this.isConnected = isConnected;
            this.createSource = createSource;
            IsWaitingForCamera = true;
            Dock = DockStyle.Fill;

            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            waitingLabel = new Label
            {
                Text = "подключите камеру",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 24, FontStyle.Bold)
            };
            Controls.Add(previewBox);
            Controls.Add(waitingLabel);
            waitingLabel.BringToFront();

            timer = new Timer { Interval = 100 };
            timer.Tick += delegate { UpdatePreview(); };
        }

        public void Start()
        {
            stopped = false;
            nextConnectionCheck = DateTime.MinValue;
            UpdatePreview();
            timer.Start();
        }

        private void UpdatePreview()
        {
            if (stopped)
                return;

            CheckConnection();
            Bitmap frame = null;
            lock (frameSync)
            {
                if (frameChanged && currentFrame != null && !sourceFailed)
                {
                    frame = (Bitmap)currentFrame.Clone();
                    frameChanged = false;
                }
            }

            if (frame == null)
                return;

            Image old = previewBox.Image;
            previewBox.Image = frame;
            old?.Dispose();
            lastError = null;
            SetWaiting(false);
        }

        private void CheckConnection()
        {
            bool failed;
            lock (frameSync) { failed = sourceFailed; }
            if (!failed && DateTime.UtcNow < nextConnectionCheck)
                return;

            nextConnectionCheck = DateTime.UtcNow.AddMilliseconds(500);
            try
            {
                bool connected = isConnected(cameraId);
                if (source != null && (!connected || failed || !source.IsRunning))
                    StopSource();

                if (source == null && connected
                    && (stoppingSource == null || stoppingSource.IsCompleted))
                {
                    IVideoSource device = createSource(cameraId);
                    lock (frameSync) { source = device; }
                    device.NewFrame += Source_NewFrame;
                    device.VideoSourceError += Source_Error;
                    device.PlayingFinished += Source_Finished;
                    device.Start();
                }
            }
            catch (Exception ex)
            {
                if (lastError != ex.Message)
                    LogManager.Write("Camera connection error: " + ex.Message);
                lastError = ex.Message;
                StopSource();
            }
        }

        private void Source_NewFrame(object sender, NewFrameEventArgs e)
        {
            lock (frameSync)
            {
                if (stopped || sourceFailed || !ReferenceEquals(sender, source))
                    return;

                currentFrame?.Dispose();
                currentFrame = (Bitmap)e.Frame.Clone();
                frameChanged = true;
            }
        }

        private void Source_Error(object sender, VideoSourceErrorEventArgs e)
        {
            lock (frameSync)
            {
                if (ReferenceEquals(sender, source))
                    sourceFailed = true;
            }
        }

        private void Source_Finished(object sender, ReasonToFinishPlaying reason)
        {
            lock (frameSync)
            {
                if (ReferenceEquals(sender, source))
                    sourceFailed = true;
            }
        }

        public Bitmap CaptureFrame()
        {
            // Recheck presence on a shutter press to reject a stale frame after unplugging.
            nextConnectionCheck = DateTime.MinValue;
            CheckConnection();
            lock (frameSync)
            {
                if (stopped || sourceFailed || currentFrame == null)
                    return null;

                return (Bitmap)currentFrame.Clone();
            }
        }

        private void SetWaiting(bool waiting)
        {
            waitingLabel.Visible = waiting;
            if (IsWaitingForCamera == waiting)
                return;

            IsWaitingForCamera = waiting;
            ReadyChanged?.Invoke(this, EventArgs.Empty);
        }

        private void StopSource()
        {
            IVideoSource oldSource;
            lock (frameSync)
            {
                oldSource = source;
                source = null;
                sourceFailed = false;
                currentFrame?.Dispose();
                currentFrame = null;
                frameChanged = false;
            }

            Image old = previewBox.Image;
            previewBox.Image = null;
            old?.Dispose();
            SetWaiting(true);
            if (oldSource == null)
                return;

            oldSource.NewFrame -= Source_NewFrame;
            oldSource.VideoSourceError -= Source_Error;
            oldSource.PlayingFinished -= Source_Finished;
            // A USB driver may take time to stop. Keep the close button and the 1C reply responsive.
            stoppingSource = Task.Run(() =>
            {
                try
                {
                    oldSource.SignalToStop();
                    oldSource.WaitForStop();
                }
                catch (Exception ex)
                {
                    LogManager.Write("Camera stop error: " + ex.Message);
                }
            });
        }

        public void Stop()
        {
            if (stopped)
                return;

            stopped = true;
            timer.Stop();
            StopSource();
        }

        protected override void Dispose(bool disposing)
        {
            LogManager.Write("Camera preview disposing: " + cameraId);
            if (disposing)
            {
                Stop();
                timer.Dispose();
            }
            LogManager.Write("Camera preview timer disposed: " + cameraId);
            base.Dispose(disposing);
            LogManager.Write("Camera preview disposed: " + cameraId);
        }
    }
}
