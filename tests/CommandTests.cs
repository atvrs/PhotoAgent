using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PhotoAgent;

internal static class CommandTests
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        try
        {
            Run("Pending command is found at startup on the STA thread", Startup);
            Run("File is found without filesystem notifications", Polling);
            Run("Slow writer and an open file are retried", SlowWriter);
            Run("Incomplete JSON is retried after being completed", IncompleteJson);
            Run("Temporary file renamed to JSON is found", Rename);
            Run("Modal message-loop reentry returns busy for a different request", Busy);
            Run("Busy request can be retried after the active capture finishes", BusyRetry);
            Run("Duplicate active request does not overwrite its result", ActiveDuplicate);
            Run("Completed result is replayed without recapture", Replay);
            Run("Failed response publication is retried without recapture", PublishRetry);
            Run("Failed command deletion is retried without recapture", DeleteRetry);
            Run("Cancellation returns an error and releases the capture gate", Cancel);
            Run("Capture exception returns an error", CaptureException);
            Run("Timeout cannot return a successful response", Timeout);
            Run("Invalid JSON and unsafe names stay pending", InvalidCommands);
            Run("Unknown command returns an error", UnknownCommand);
            Run("Capture gate admits only one concurrent caller", CaptureGate);
            Run("Disposal stops the polling timer", Disposal);
            Console.WriteLine("PASS: " + passed + " command regression tests");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run(string name, Action<TestEnvironment> test)
    {
        using (var environment = new TestEnvironment())
            test(environment);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Startup(TestEnvironment env)
    {
        env.Write("startup");
        env.Watcher.Start();
        PumpUntil(() => File.Exists(env.ResponsePath("startup")), 5000);
        Assert(env.Captures == 1, "Startup command was missed or duplicated");
        Assert(env.CaptureApartment == ApartmentState.STA, "Capture must run on STA");
    }

    private static void Polling(TestEnvironment env)
    {
        env.Scan();
        env.Write("poll");
        env.Scan();
        Assert(env.Captures == 0, "First observation must wait for stability");
        env.Scan();
        Assert(env.Status("poll") == "ok", "Enumeration must find an eventless command");
    }

    private static void SlowWriter(TestEnvironment env)
    {
        string path = env.CommandPath("slow");
        byte[] json = Encoding.UTF8.GetBytes(env.Json("slow"));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            stream.Write(json, 0, 15);
            stream.Flush();
            env.ScanTwice();
            Assert(env.Captures == 0, "Partially written command was accepted");
            stream.Write(json, 15, json.Length - 15);
            stream.Flush();
            env.ScanTwice();
            Assert(env.Captures == 0, "Open writer must block command acceptance");
        }
        env.Scan();
        Assert(env.Status("slow") == "ok", "Command must run after the writer closes");
    }

    private static void IncompleteJson(TestEnvironment env)
    {
        string json = env.Json("partial");
        File.WriteAllText(env.CommandPath("partial"), json.Substring(0, json.Length - 2));
        env.ScanTwice();
        Assert(env.Captures == 0, "Incomplete JSON was accepted");
        File.AppendAllText(env.CommandPath("partial"), json.Substring(json.Length - 2));
        env.ScanTwice();
        Assert(env.Status("partial") == "ok", "Completed JSON was not retried");
    }

    private static void Rename(TestEnvironment env)
    {
        string temporaryFile = Path.Combine(env.Commands, "rename.tmp");
        File.WriteAllText(temporaryFile, env.Json("rename"));
        env.ScanTwice();
        Assert(env.Captures == 0, "Temporary files must be ignored");
        File.Move(temporaryFile, env.CommandPath("rename"));
        env.ScanTwice();
        Assert(env.Status("rename") == "ok", "Renamed JSON was missed");
    }

    private static void Busy(TestEnvironment env)
    {
        env.OnCapture = delegate
        {
            env.Write("other");
            env.ScanTwice();
            Assert(env.Status("other") == "busy", "Different request must get busy");
            Assert(CaptureState.IsBusy, "Nested processing released the active capture");
            return new CaptureResult { Success = true };
        };
        env.Write("active");
        env.ScanTwice();
        Assert(env.Status("active") == "ok" && env.Captures == 1, "Active capture was duplicated");
    }

    private static void BusyRetry(TestEnvironment env)
    {
        Busy(env);
        File.Delete(env.ResponsePath("other"));
        env.OnCapture = () => new CaptureResult { Success = true };
        env.Write("other");
        env.ScanTwice();
        Assert(env.Status("other") == "ok" && env.Captures == 2, "Transient busy result was cached");
    }

    private static void ActiveDuplicate(TestEnvironment env)
    {
        env.OnCapture = delegate
        {
            File.WriteAllText(env.CommandPath("duplicate"), env.Json("SAME"));
            env.ScanTwice();
            Assert(!File.Exists(env.ResponsePath("same")), "Duplicate overwrote the active request");
            return new CaptureResult { Success = true };
        };
        env.Write("same");
        env.ScanTwice();
        env.Scan();
        Assert(env.Status("same") == "ok" && env.Captures == 1, "Same request captured twice");
        Assert(!File.Exists(env.CommandPath("duplicate")), "Duplicate command was not consumed");
    }

    private static void Replay(TestEnvironment env)
    {
        env.Write("replay");
        env.ScanTwice();
        string original = File.ReadAllText(env.ResponsePath("replay"));
        File.Delete(env.ResponsePath("replay"));
        env.Write("replay");
        env.ScanTwice();
        Assert(env.Captures == 1, "Completed request was captured again");
        Assert(File.ReadAllText(env.ResponsePath("replay")) == original, "Result changed on replay");
    }

    private static void PublishRetry(TestEnvironment env)
    {
        Directory.CreateDirectory(env.ResponsePath("publish"));
        env.Write("publish");
        env.ScanTwice();
        Assert(env.Captures == 1 && File.Exists(env.CommandPath("publish")), "Retry lost the command");
        Directory.Delete(env.ResponsePath("publish"));
        env.Scan();
        Assert(env.Captures == 1 && env.Status("publish") == "ok", "Publication retry recaptured");
        Assert(Directory.GetFiles(env.Responses, "*.tmp").Length == 0, "Temporary response leaked");
    }

    private static void DeleteRetry(TestEnvironment env)
    {
        FileStream reader = null;
        env.OnCapture = delegate
        {
            reader = new FileStream(env.CommandPath("delete"), FileMode.Open,
                FileAccess.Read, FileShare.Read);
            return new CaptureResult { Success = true };
        };
        try
        {
            env.Write("delete");
            env.ScanTwice();
            Assert(env.Status("delete") == "ok", "Response was not published");
            Assert(File.Exists(env.CommandPath("delete")), "Test must hold the command against deletion");
            File.Delete(env.ResponsePath("delete"));
        }
        finally
        {
            if (reader != null)
                reader.Dispose();
        }
        env.Scan();
        Assert(env.Captures == 1 && env.Status("delete") == "ok", "Deletion retry recaptured");
    }

    private static void Cancel(TestEnvironment env)
    {
        env.OnCapture = () => new CaptureResult { Success = false };
        env.Write("cancel");
        env.ScanTwice();
        env.Scan();
        Assert(env.Status("cancel") == "error" && env.Captures == 1, "Cancellation was retried");
        Assert(!CaptureState.IsBusy, "Cancellation left the agent busy");
    }

    private static void CaptureException(TestEnvironment env)
    {
        env.OnCapture = delegate { throw new InvalidOperationException("Camera unavailable"); };
        env.Write("exception");
        env.ScanTwice();
        Assert(env.Status("exception") == "error", "Camera error was lost");
        Assert(!CaptureState.IsBusy, "Exception left the agent busy");
    }

    private static void Timeout(TestEnvironment env)
    {
        env.OnCapture = delegate
        {
            CaptureState.MarkTimedOut();
            return new CaptureResult { Success = true };
        };
        env.Write("timeout");
        env.ScanTwice();
        Assert(env.Status("timeout") == "error", "Timeout returned ok");
    }

    private static void InvalidCommands(TestEnvironment env)
    {
        File.WriteAllText(env.CommandPath("null"), "null");
        File.WriteAllText(env.CommandPath("empty"), "{}");
        File.WriteAllText(env.CommandPath("invalid"), "{\"request_id\":");
        File.WriteAllText(env.CommandPath("unsafe"), env.Json(@"..\outside"));
        env.ScanTwice();
        Assert(env.Captures == 0, "Invalid command reached a camera");
        Assert(Directory.GetFiles(env.Commands, "*.json").Length == 4, "Invalid command was deleted");
    }

    private static void UnknownCommand(TestEnvironment env)
    {
        File.WriteAllText(env.CommandPath("unknown"), env.Json("unknown").Replace(
            "capture_document", "unsupported"));
        env.ScanTwice();
        Assert(env.Status("unknown") == "error" && env.Captures == 0, "Unknown command was lost");
    }

    private static void CaptureGate(TestEnvironment env)
    {
        int winners = 0;
        var threads = new List<Thread>();
        using (var start = new ManualResetEvent(false))
        {
            for (int i = 0; i < 16; i++)
            {
                var thread = new Thread(delegate()
                {
                    start.WaitOne();
                    if (CaptureState.TryBegin("race"))
                        Interlocked.Increment(ref winners);
                });
                threads.Add(thread);
                thread.Start();
            }
            start.Set();
            foreach (Thread thread in threads)
                thread.Join();
        }
        Assert(winners == 1, "Concurrent requests passed the busy gate");
        CaptureState.End();
    }

    private static void Disposal(TestEnvironment env)
    {
        env.Watcher.Start();
        env.Watcher.Dispose();
        env.Write("disposed");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 1200)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert(env.Captures == 0, "Disposed watcher still processed a command");
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new Exception("Polling timer did not process the command");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(),
            "PhotoAgentTests_" + Guid.NewGuid().ToString("N"));
        public string Commands;
        public string Responses;
        public CommandWatcher Watcher;
        public int Captures;
        public ApartmentState CaptureApartment;
        public Func<CaptureResult> OnCapture = () => new CaptureResult { Success = true };

        public TestEnvironment()
        {
            Commands = Path.Combine(root, "commands");
            Responses = Path.Combine(root, "responses");
            Directory.CreateDirectory(Commands);
            Directory.CreateDirectory(Responses);
            CaptureWorkflow.OnCapture = delegate
            {
                Captures++;
                CaptureApartment = Thread.CurrentThread.GetApartmentState();
                return OnCapture();
            };
            Watcher = new CommandWatcher(Commands, path => CommandProcessor.Process(path, Responses));
        }

        public string CommandPath(string id) { return Path.Combine(Commands, id + ".json"); }
        public string ResponsePath(string id) { return Path.Combine(Responses, id + ".done"); }
        public void Write(string id) { File.WriteAllText(CommandPath(id), Json(id)); }
        public void Scan() { Watcher.ScanCommands(); }
        public void ScanTwice() { Scan(); Scan(); }
        public string Json(string id)
        {
            return new JavaScriptSerializer().Serialize(new
            {
                command = "capture_document", request_id = id, document_number = "TEST_DOC"
            });
        }

        public string Status(string id)
        {
            return (string)new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                File.ReadAllText(ResponsePath(id)))["status"];
        }

        public void Dispose()
        {
            Watcher.Dispose();
            CaptureState.End();
            Directory.Delete(root, true);
        }
    }
}

// Replace only physical capture and logging. Compile the production command transport unchanged.
namespace PhotoAgent
{
    internal static class CaptureWorkflow
    {
        public static Func<CaptureResult> OnCapture;
        public static CaptureResult CaptureDocument(CaptureCommand command) { return OnCapture(); }
        public static CaptureResult CaptureExtra(CaptureCommand command) { return OnCapture(); }
    }

    internal static class LogManager
    {
        public static void Write(string message) { Console.WriteLine(message); }
    }
}
