using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AForge.Video;
using PhotoAgent;
using Timer = System.Windows.Forms.Timer;

internal static class CameraTests
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int exitCode = 0;
        using (var host = new Form { ShowInTaskbar = false, Opacity = 0 })
        {
            host.Shown += delegate
            {
                host.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        RunAll();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        exitCode = 1;
                    }
                    finally
                    {
                        host.Close();
                    }
                }));
            };
            // Give successive modal windows a persistent owner on the CI desktop.
            Application.Run(host);
        }
        return exitCode;
    }

    private static void RunAll()
    {
            Run("Missing face camera resumes the same three-photo request", env => ReconnectWorkflow(env, "face"));
            Run("Missing document camera resumes after the face photo", env => ReconnectWorkflow(env, "document"));
            Run("Closing the missing-camera window returns an error", env => CloseMissing(env, false));
            Run("Closing missing extra capture returns an error", env => CloseMissing(env, true));
            Run("Closing after reconnect still returns an error", CloseAfterReconnect);
            Run("Extra capture resumes, saves a photo, and finishes with Escape", ExtraSuccess);
            Run("Closing extra capture after saving a photo returns an error", ExtraCross);
            Run("Unplugging clears stale frames and reconnects the assigned camera", Unplug);
            Run("Video source errors reconnect automatically", SourceError);
            Run("A slow USB shutdown cannot block closing or the 1C error reply", SlowShutdown);
            Run("Document shots share a live camera until both are saved", DocumentCameraReuse);
            Run("A new window waits for the previous camera release", ReopenAfterSlowShutdown);
            Run("A different request gets busy while the camera is missing", BusyWhileWaiting);
            Run("A camera start failure is retried", StartFailure);
            Console.WriteLine("PASS: " + passed + " camera regression tests");
    }

    private static void Run(string name, Action<TestEnvironment> action)
    {
        Console.WriteLine("RUN: " + name);
        Console.Out.Flush();
        using (var env = new TestEnvironment())
            action(env);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void ReconnectWorkflow(TestEnvironment env, string missing)
    {
        CameraManager.Connected.Add(missing == "face" ? "document" : "face");
        var waiting = new Stopwatch();
        bool resumed = false;
        env.Process(false, form =>
        {
            CameraPreviewControl preview = Preview(form);
            if (preview.IsWaitingForCamera)
            {
                Assert(Placeholder(preview), "Missing camera must show the requested text");
                if (!waiting.IsRunning)
                    waiting.Start();
                PressKey(form, Keys.Enter);
                Assert(form.Visible, "Shutter closed a window without a camera frame");
                Assert(CaptureState.IsWaitingForCamera, "Waiting must pause the request timeout");
                Assert(!CaptureState.IsTimeoutDue(1), "Missing camera timed out");
                if (waiting.ElapsedMilliseconds > 1400)
                    CameraManager.Connected.Add(missing);
                return;
            }

            if (waiting.IsRunning && !resumed)
            {
                Assert(waiting.ElapsedMilliseconds > 1400, "Test must exceed the configured timeout");
                Assert(!CaptureState.IsTimeoutDue(1), "Connection wait consumed the capture timeout");
                resumed = true;
            }
            using (Bitmap frame = preview.CaptureFrame())
            {
                Color expected = form.Text == "Ломосдатчик" ? Color.Blue : Color.Green;
                Assert(frame != null && frame.GetPixel(0, 0).ToArgb() == expected.ToArgb(),
                    "Captured frame came from the wrong assigned camera");
            }
            PressKey(form, Keys.Space);
        });
        Assert(resumed, "Request never resumed after camera connection");
        Assert(env.Status() == "ok", "Reconnected request failed");
        Assert(Directory.GetFiles(env.Photos, "*.jpg").Length == 3, "Three-photo workflow did not finish");
        Assert(!CaptureState.IsBusy && !CaptureState.IsWaitingForCamera, "Request state leaked");
    }

    private static void CloseMissing(TestEnvironment env, bool extra)
    {
        env.Process(extra, form =>
        {
            Assert(Placeholder(Preview(form)), "Missing-camera placeholder was not visible");
            form.Close();
        });
        env.AssertIncomplete();
    }

    private static void CloseAfterReconnect(TestEnvironment env)
    {
        env.Process(false, form =>
        {
            CameraPreviewControl preview = Preview(form);
            if (preview.IsWaitingForCamera)
                CameraManager.Connected.Add("face");
            else
                form.Close();
        });
        env.AssertIncomplete();
    }

    private static void ExtraSuccess(TestEnvironment env)
    {
        bool saved = false;
        env.Process(true, form =>
        {
            if (Preview(form).IsWaitingForCamera)
                CameraManager.Connected.Add("document");
            else if (!saved)
            {
                PressKey(form, Keys.Space);
                saved = true;
            }
            else
                PressKey(form, Keys.Escape);
        });
        Assert(saved && env.Status() == "ok", "Extra capture did not resume normally");
        Assert(Directory.GetFiles(env.Photos, "*_extra_*.jpg").Length == 1, "Extra photo was not saved");
    }

    private static void ExtraCross(TestEnvironment env)
    {
        CameraManager.Connected.Add("document");
        bool saved = false;
        env.Process(true, form =>
        {
            if (Preview(form).IsWaitingForCamera)
                return;
            if (!saved)
            {
                PressKey(form, Keys.Space);
                saved = true;
            }
            else
                form.Close();
        });
        Assert(saved, "Test must save a photo before closing");
        env.AssertIncomplete();
    }

    private static void Unplug(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        int phase = 0;
        env.Process(false, form =>
        {
            CameraPreviewControl preview = Preview(form);
            if (phase == 0 && !preview.IsWaitingForCamera)
            {
                FakeVideoSource oldSource = CameraManager.Devices.Last();
                NewFrameEventHandler lateFrame = oldSource.FrameHandler;
                CameraManager.Connected.Remove("face");
                PressKey(form, Keys.Enter);
                Assert(preview.IsWaitingForCamera && form.Visible, "Unplugged camera accepted a stale frame");
                Assert(Placeholder(preview), "Unplug did not show the placeholder");
                using (var stale = new Bitmap(4, 4))
                {
                    stale.SetPixel(0, 0, Color.Red);
                    lateFrame(oldSource, new NewFrameEventArgs(stale));
                }
                Assert(preview.CaptureFrame() == null, "Late callback restored a disconnected camera frame");
                CameraManager.Connected.Add("face");
                phase = 1;
            }
            else if (phase == 1 && !preview.IsWaitingForCamera)
            {
                Assert(CameraManager.Devices.Count >= 2, "Disconnected device was not recreated");
                using (Bitmap frame = preview.CaptureFrame())
                    Assert(frame.GetPixel(0, 0).ToArgb() == Color.Blue.ToArgb(), "Late stale frame survived reconnect");
                form.Close();
                phase = 2;
            }
        });
        Assert(phase == 2, "Camera never reconnected");
        env.AssertIncomplete();
    }

    private static void SourceError(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        bool failed = false;
        env.Process(false, form =>
        {
            CameraPreviewControl preview = Preview(form);
            if (!failed && !preview.IsWaitingForCamera)
            {
                CameraManager.Devices.Last().Fail();
                failed = true;
            }
            else if (failed && CameraManager.Devices.Count >= 2 && !preview.IsWaitingForCamera)
                form.Close();
        });
        Assert(CameraManager.Devices.Count >= 2, "Video source error was not retried");
        env.AssertIncomplete();
    }

    private static void SlowShutdown(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        using (var stopGate = new ManualResetEvent(false))
        {
            CameraManager.Factory = id => new FakeVideoSource(id) { StopGate = stopGate };
            try
            {
                env.Process(false, form => form.Close());
                env.AssertIncomplete();
                Assert(!stopGate.WaitOne(0), "Test must keep the driver shutdown blocked until after the response");
            }
            finally
            {
                stopGate.Set();
                foreach (FakeVideoSource device in CameraManager.Devices)
                    device.Stopped.WaitOne(5000);
            }
        }
    }

    private static void BusyWhileWaiting(TestEnvironment env)
    {
        env.Process(false, form =>
        {
            string otherFile = Path.Combine(env.Root, "other.json");
            File.WriteAllText(otherFile, new JavaScriptSerializer().Serialize(new
            {
                command = "capture_document", request_id = "other", document_number = env.Document
            }));
            Assert(CommandProcessor.Process(otherFile, env.Responses), "Busy request was not consumed");
            string json = File.ReadAllText(Path.Combine(env.Responses, "other.done"));
            Assert(json.Contains("\"busy\""), "Missing camera must keep the active request busy");
            Assert(CaptureState.RequestId == env.Request, "Busy response replaced the active request");
            form.Close();
        });
        env.AssertIncomplete();
    }

    private static void DocumentCameraReuse(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        CameraManager.Connected.Add("document");
        using (var stopGate = new ManualResetEvent(false))
        {
            CameraManager.Factory = id => new FakeVideoSource(id)
            {
                StopGate = id == "document" ? stopGate : null
            };
            CameraPreviewControl documentPreview = null;
            bool registrationSeen = false;
            try
            {
                env.Process(false, form =>
                {
                    CameraPreviewControl preview = Preview(form);
                    if (preview.IsWaitingForCamera)
                        return;

                    if (form.Text == "Паспорт")
                        documentPreview = preview;
                    if (form.Text == "Прописка")
                    {
                        Assert(ReferenceEquals(documentPreview, preview),
                            "The document preview was recreated between shots");
                        FakeVideoSource device = CameraManager.Devices.Single(d => d.Source == "document");
                        Assert(device.IsRunning && device.StopCalls == 0,
                            "The document camera was stopped before the second shot");
                        Assert(!CaptureState.IsWaitingForCamera,
                            "A ready shared preview incorrectly paused the request");
                        registrationSeen = true;
                    }
                    PressKey(form, Keys.Space);
                });
                Assert(registrationSeen && env.Status() == "ok", "Both document shots must finish");
                Assert(Directory.GetFiles(env.Photos, "*.jpg").Length == 3, "A photo was lost");
            }
            finally
            {
                stopGate.Set();
                foreach (FakeVideoSource device in CameraManager.Devices)
                    device.Stopped.WaitOne(5000);
            }
        }
    }

    private static void ReopenAfterSlowShutdown(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        using (var stopGate = new ManualResetEvent(false))
        {
            CameraManager.Factory = id => new FakeVideoSource(id) { StopGate = stopGate };
            try
            {
                env.Process(false, form =>
                {
                    if (!Preview(form).IsWaitingForCamera)
                        form.Close();
                });
                env.AssertIncomplete();
                Assert(CameraManager.Devices.Count == 1, "First request must have opened a camera");

                env.Request = Guid.NewGuid().ToString("N");
                CameraManager.Factory = id => new FakeVideoSource(id);
                bool waitedForRelease = false;
                env.Process(false, form =>
                {
                    if (!stopGate.WaitOne(0))
                    {
                        Assert(Preview(form).IsWaitingForCamera,
                            "Next request must wait for the previous camera shutdown");
                        Assert(CameraManager.Devices.Count == 1,
                            "The driver was reopened before the previous release completed");
                        waitedForRelease = true;
                        stopGate.Set();
                    }
                    else if (!Preview(form).IsWaitingForCamera)
                        form.Close();
                });
                Assert(waitedForRelease && CameraManager.Devices.Count == 2,
                    "A new request did not resume after release");
                env.AssertIncomplete();
            }
            finally
            {
                stopGate.Set();
                foreach (FakeVideoSource device in CameraManager.Devices)
                    device.Stopped.WaitOne(5000);
            }
        }
    }

    private static void StartFailure(TestEnvironment env)
    {
        CameraManager.Connected.Add("face");
        int attempts = 0;
        CameraManager.Factory = id =>
        {
            if (++attempts == 1)
                throw new IOException("Camera is still initializing");
            return new FakeVideoSource(id);
        };
        env.Process(false, form =>
        {
            if (!Preview(form).IsWaitingForCamera)
                form.Close();
        });
        Assert(attempts >= 2, "Failed camera initialization was not retried");
        env.AssertIncomplete();
    }

    private static CameraPreviewControl Preview(Form form)
    {
        return form.Controls.OfType<CameraPreviewControl>().Single();
    }

    private static bool Placeholder(CameraPreviewControl preview)
    {
        return preview.Controls.OfType<Label>().Any(
            label => label.Visible && label.Text == "подключите камеру");
    }

    private static void PressKey(Form form, Keys key)
    {
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { new KeyEventArgs(key) });
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class TestEnvironment : IDisposable
    {
        public string Request = Guid.NewGuid().ToString("N");
        public readonly string Document = "CAMERA_TEST_" + Guid.NewGuid().ToString("N");
        public readonly string Root = Path.Combine(Path.GetTempPath(), "PhotoAgentCamera_" + Guid.NewGuid().ToString("N"));
        public string Responses { get { return Path.Combine(Root, "responses"); } }
        public string Photos { get { return Path.Combine(@"C:\psa_photos\photos", Document); } }

        public TestEnvironment()
        {
            CameraManager.Reset();
            ConfigManager.Current = new Config
            {
                lomoCameraId = "face", documentCameraId = "document", captureTimeoutSec = 1
            };
            Directory.CreateDirectory(Root);
        }

        public void Process(bool extra, Action<Form> drive)
        {
            string commandFile = Path.Combine(Root, "command.json");
            File.WriteAllText(commandFile, new JavaScriptSerializer().Serialize(new
            {
                command = extra ? "capture_extra" : "capture_document",
                request_id = Request, document_number = Document
            }));
            Exception failure = null;
            var clock = Stopwatch.StartNew();
            using (var timer = new Timer { Interval = 50 })
            {
                timer.Tick += delegate
                {
                    try
                    {
                        if (clock.ElapsedMilliseconds > 15000)
                            throw new Exception("Camera UI test timed out");
                        Form form = Application.OpenForms.Cast<Form>()
                            .FirstOrDefault(f => f is CaptureForm || f is ExtraCaptureForm);
                        if (failure != null)
                        {
                            if (form != null)
                            {
                                form.DialogResult = DialogResult.Cancel;
                                form.Close();
                            }
                            return;
                        }
                        if (form != null)
                            drive(form);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        foreach (Form form in Application.OpenForms.Cast<Form>()
                            .Where(f => f is CaptureForm || f is ExtraCaptureForm).ToArray())
                        {
                            form.DialogResult = DialogResult.Cancel;
                            form.Close();
                        }
                    }
                };
                timer.Start();
                bool consumed = CommandProcessor.Process(commandFile, Responses);
                if (failure != null)
                    throw new Exception("Camera UI interaction failed", failure);
                Assert(consumed, "Capture command did not publish its response");
            }
        }

        public string Status()
        {
            return (string)Response()["status"];
        }

        private Dictionary<string, object> Response()
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(Path.Combine(Responses, Request + ".done")));
        }

        public void AssertIncomplete()
        {
            var response = Response();
            Assert((string)response["status"] == "error", "Closing a window must return error");
            Assert((string)response["message"] == "Некорректное завершение обработки задания",
                "1C did not receive the requested incomplete-processing message");
            Assert(!CaptureState.IsBusy && !CaptureState.IsWaitingForCamera, "Closing leaked capture state");
        }

        public void Dispose()
        {
            CaptureState.End();
            Directory.Delete(Root, true);
            if (Directory.Exists(Photos))
                Directory.Delete(Photos, true);
            foreach (FakeVideoSource device in CameraManager.Devices)
                device.Stopped.WaitOne(5000);
        }
    }
}

namespace PhotoAgent
{
    internal static class CameraManager
    {
        public static readonly HashSet<string> Connected = new HashSet<string>();
        public static readonly List<FakeVideoSource> Devices = new List<FakeVideoSource>();
        public static Func<string, FakeVideoSource> Factory;
        public static bool IsConnected(string id) { return Connected.Contains(id); }
        public static IVideoSource CreatePreviewDevice(string id)
        {
            FakeVideoSource device = Factory(id);
            Devices.Add(device);
            return device;
        }
        public static void Reset()
        {
            Connected.Clear();
            Devices.Clear();
            Factory = id => new FakeVideoSource(id);
        }
    }

    internal sealed class FakeVideoSource : IVideoSource
    {
        public event NewFrameEventHandler NewFrame;
        public event VideoSourceErrorEventHandler VideoSourceError;
        public event PlayingFinishedEventHandler PlayingFinished;
        public NewFrameEventHandler FrameHandler { get { return NewFrame; } }
        public string Source { get; private set; }
        public int FramesReceived { get { return 1; } }
        public long BytesReceived { get { return 64; } }
        public bool IsRunning { get; private set; }
        public int StopCalls;
        public ManualResetEvent StopGate;
        public readonly ManualResetEvent Stopped = new ManualResetEvent(false);

        public FakeVideoSource(string source) { Source = source; }
        public void Start()
        {
            IsRunning = true;
            using (var frame = new Bitmap(4, 4))
            {
                frame.SetPixel(0, 0, Source == "face" ? Color.Blue : Color.Green);
                NewFrame(this, new NewFrameEventArgs(frame));
            }
        }
        public void Fail()
        {
            VideoSourceError(this, new VideoSourceErrorEventArgs("USB camera disconnected"));
        }
        public void SignalToStop()
        {
            Interlocked.Increment(ref StopCalls);
            IsRunning = false;
        }
        public void WaitForStop()
        {
            if (StopGate != null)
                StopGate.WaitOne();
            Stopped.Set();
        }
        public void Stop()
        {
            SignalToStop();
            WaitForStop();
            if (PlayingFinished != null)
                PlayingFinished(this, ReasonToFinishPlaying.StoppedByUser);
        }
    }

    internal static class ConfigManager { public static Config Current; }
    internal static class ArchiveManager { public static void ArchiveDocument(string document) { } }
    internal static class LogManager { public static void Write(string message) { Console.WriteLine(message); } }
}
