using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using PhotoAgent;

internal static class TransferCacheTests
{
    private static int passed;
    private static readonly DateTime Now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    [STAThread]
    private static int Main()
    {
        try
        {
            Run("Defaults are one month and one hundred MiB", Defaults);
            Run("Expired request is removed with a durable expiry notice", Age);
            Run("Exactly one month old is retained", AgeBoundary);
            Run("Rolling-month cutoff handles a leap-year month end", LeapMonth);
            Run("Size quota removes the oldest request first", Size);
            Run("Exact quota boundary is retained", SizeBoundary);
            Run("File timestamps, not directory creation times, determine age", FileAge);
            Run("Active request is protected during age and quota cleanup", Active);
            Run("Nested active leases are reference counted", NestedPins);
            Run("A slow reader protects the entire request without partial deletion", Reader);
            Run("Read-only requests are preserved", ReadOnly);
            Run("A single oversized photo cannot evict other requests", Oversized);
            Run("New photo evicts old data before exceeding quota", Admission);
            Run("Full protected cache rejects a new photo without growth", Backpressure);
            Run("Pending receipt bytes stay reserved across publication failure", ReceiptReservation);
            Run("Old loose temporary files are removed", LooseFiles);
            Run("Old empty request folders are removed", EmptyFolder);
            Run("Archive and working photos are not affected", OtherFolders);
            Run("Failed expiry publication preserves the files", NoticeFailure);
            Run("Pending receipt with a completed JPEG recovers after restart", RecoverPending);
            Run("Pending receipt without a JPEG is not announced", OrphanPending);
            Run("Expired request cannot create a new snapshot", ExpiredWrite);
            Run("Invalid limits fall back without purging recent data", InvalidLimits);
            Run("Global FIFO preserves a fresh sibling in the oldest request", InterleavedFiles);
            Run("Global FIFO crosses request folders before deleting fresher files", TwoEvictions);
            Run("Age cleanup preserves fresh files in a partially expired request", PartialAge);
            Run("JPEG and matching receipts expire together using JPEG age", ReceiptPair);
            Run("Expiry notice accumulates only removed photo names", PartialNotices);
            Run("Legacy whole-request expiry notices are never narrowed", LegacyNotice);
            Run("A locked expiry notice prevents unreported photo deletion", LockedNotice);
            Run("Age cleanup visits photos from oldest to newest globally", AgeOrder);
            Console.WriteLine("PASS: " + passed + " transfer-cache regression tests");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Run(string name, Action<Fixture> test)
    {
        using (var env = new Fixture()) test(env);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Defaults(Fixture env)
    {
        var config = new Config();
        Assert(config.transferRetentionMonths == 1 && config.transferMaxMb == 100, "Wrong defaults");
    }

    private static void Age(Fixture env)
    {
        env.Request("old", 200, Now.AddMonths(-2));
        env.Clean();
        Assert(!env.Exists("old"), "Expired request remains");
        var notice = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(env.Notice("old")));
        Assert((string)notice["status"] == "transfer_expired" && (string)notice["reason"] == "age"
            && (string)notice["request_id"] == "old", "Expiry notice is incomplete");
    }

    private static void AgeBoundary(Fixture env)
    {
        env.Request("boundary", 10, Now.AddMonths(-1));
        env.Request("older", 10, Now.AddMonths(-1).AddSeconds(-1));
        env.Clean();
        Assert(env.Exists("boundary") && !env.Exists("older"), "Month boundary was rounded incorrectly");
    }

    private static void LeapMonth(Fixture env)
    {
        var now = new DateTime(2024, 3, 31, 12, 0, 0, DateTimeKind.Utc);
        env.Request("boundary", 10, now.AddMonths(-1));
        env.Request("older", 10, now.AddMonths(-1).AddSeconds(-1));
        TransferCache.RunMaintenance(env.Root, env.Config(10000), now);
        Assert(env.Exists("boundary") && !env.Exists("older"), "Leap-month cutoff failed");
    }

    private static void Size(Fixture env)
    {
        env.Request("oldest", 400, Now.AddDays(-3));
        env.Request("middle", 400, Now.AddDays(-2));
        env.Request("newest", 400, Now.AddDays(-1));
        env.Clean(800);
        Assert(!env.Exists("oldest") && env.Exists("middle") && env.Exists("newest"), "Wrong eviction order");
        Assert(File.ReadAllText(env.Notice("oldest")).Contains("\"size\""), "Size reason is missing");
    }

    private static void SizeBoundary(Fixture env)
    {
        env.Request("exact", 1000, Now.AddDays(-1));
        env.Clean(1000);
        Assert(env.Exists("exact"), "Exact quota boundary was evicted");
    }

    private static void FileAge(Fixture env)
    {
        string folder = env.Request("old", 10, Now.AddMonths(-2));
        Directory.SetCreationTimeUtc(folder, Now);
        env.Clean();
        Assert(!env.Exists("old"), "Moving a directory extended its lifetime");
    }

    private static void Active(Fixture env)
    {
        env.Request("active", 700, Now.AddMonths(-2));
        env.Request("other", 500, Now.AddDays(-1));
        using (var transfer = new PhotoTransfer(new CaptureCommand
            { request_id = "active", document_number = "DOC" }, env.Responses, env.Root))
        {
            env.Clean(800);
            Assert(env.Exists("active") && !env.Exists("other"), "Active capture was evicted");
        }
        env.Clean();
        Assert(!env.Exists("active"), "Disposed capture remained pinned");
    }

    private static void NestedPins(Fixture env)
    {
        env.Request("pinned", 10, Now.AddMonths(-2));
        TransferCache.Pin(env.Root, "pinned");
        TransferCache.Pin(env.Root, "pinned");
        try
        {
            TransferCache.Unpin(env.Root, "pinned");
            env.Clean();
            Assert(env.Exists("pinned"), "One lease released another lease's protection");
        }
        finally { TransferCache.Unpin(env.Root, "pinned"); }
        env.Clean();
        Assert(!env.Exists("pinned"), "Last lease did not release protection");
    }

    private static void Reader(Fixture env)
    {
        string folder = env.Request("reading", 100, Now.AddMonths(-2));
        string second = Path.Combine(folder, "second.jpg");
        File.WriteAllBytes(second, new byte[100]);
        File.SetLastWriteTimeUtc(second, Now.AddMonths(-2));
        using (var reader = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            env.Clean(1);
            Assert(File.Exists(Path.Combine(folder, "photo.jpg")) && File.Exists(second),
                "Reading request was partially deleted");
            Assert(!File.Exists(env.Notice("reading")), "Readable photo was incorrectly marked expired");
            Assert(reader.ReadByte() == 0, "Open reader was broken");
        }
        env.Clean();
        Assert(!env.Exists("reading") && File.Exists(env.Notice("reading")), "Released reader blocked cleanup");
    }

    private static void ReadOnly(Fixture env)
    {
        string folder = env.Request("readonly", 20, Now.AddMonths(-2));
        string file = Path.Combine(folder, "photo.jpg");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try { env.Clean(1); Assert(File.Exists(file), "Read-only file was changed"); }
        finally { File.SetAttributes(file, FileAttributes.Normal); }
    }

    private static void Oversized(Fixture env)
    {
        env.Request("keep", 20, DateTime.UtcNow);
        ConfigManager.Current = env.Config(100);
        ExpectIo(() => TransferCache.StorePhoto(env.Root, "new", env.Source("big.jpg", 101), 1, "{}"));
        Assert(env.Exists("keep") && !env.Exists("new"), "Impossible write evicted existing data");
    }

    private static void Admission(Fixture env)
    {
        env.Request("old", 1024, DateTime.UtcNow.AddDays(-1));
        ConfigManager.Current = env.Config(1024);
        TransferCache.StorePhoto(env.Root, "new", env.Source("new.jpg", 512), 1, "{}");
        Assert(!env.Exists("old") && env.Exists("new"), "Admission did not reclaim space");
        Assert(env.Bytes() == 514, "Receipt reservation is not included in quota");
        TransferCache.CommitReceipt(Path.Combine(env.Transfer, "new", "photo.0001.json"));
        Assert(env.Bytes() == 514, "Receipt promotion increased storage usage");
    }

    private static void Backpressure(Fixture env)
    {
        env.Request("active", 900, DateTime.UtcNow);
        ConfigManager.Current = env.Config(1000);
        TransferCache.Pin(env.Root, "active");
        try
        {
            ExpectIo(() => TransferCache.StorePhoto(env.Root, "new", env.Source("new.jpg", 200), 1, "{}"));
            Assert(env.Bytes() == 900 && !env.Exists("new"), "Protected full cache grew");
        }
        finally { TransferCache.Unpin(env.Root, "active"); }
    }

    private static void ReceiptReservation(Fixture env)
    {
        string folder = Path.Combine(env.Transfer, "active");
        string receipt = Path.Combine(folder, "photo.0001.json");
        Directory.CreateDirectory(receipt);
        ConfigManager.Current = env.Config(600);
        TransferCache.StorePhoto(env.Root, "active", env.Source("one.jpg", 512), 1, "{}");
        ExpectIo(() => TransferCache.CommitReceipt(receipt));
        ExpectIo(() => TransferCache.StorePhoto(env.Root, "active", env.Source("two.jpg", 100), 2, "{}"));
        Assert(env.Bytes() == 514, "Delayed receipt allowed quota to be exceeded");
        Directory.Delete(receipt);
        TransferCache.CommitReceipt(receipt);
        Assert(env.Bytes() == 514, "Receipt retry used additional storage");
    }

    private static void LooseFiles(Fixture env)
    {
        string file = Path.Combine(env.Transfer, "orphan.tmp");
        File.WriteAllText(file, "old");
        File.SetLastWriteTimeUtc(file, Now.AddMonths(-2));
        env.Clean();
        Assert(!File.Exists(file), "Loose temporary file leaked");
    }

    private static void EmptyFolder(Fixture env)
    {
        string folder = Path.Combine(env.Transfer, "empty");
        Directory.CreateDirectory(folder);
        Directory.SetLastWriteTimeUtc(folder, Now.AddMonths(-2));
        env.Clean();
        Assert(!Directory.Exists(folder), "Old empty directory leaked");
    }

    private static void OtherFolders(Fixture env)
    {
        foreach (string name in new[] { "archive", "photos" })
        {
            Directory.CreateDirectory(Path.Combine(env.Root, name));
            File.WriteAllText(Path.Combine(env.Root, name, "keep.jpg"), "keep");
        }
        env.Request("old", 20, Now.AddMonths(-2)); env.Clean(1);
        Assert(File.Exists(Path.Combine(env.Root, "archive", "keep.jpg"))
            && File.Exists(Path.Combine(env.Root, "photos", "keep.jpg")), "Permanent photos were deleted");
    }

    private static void NoticeFailure(Fixture env)
    {
        env.Request("old", 20, Now.AddMonths(-2));
        Directory.Delete(env.Responses);
        File.WriteAllText(env.Responses, "blocked");
        env.Clean(1);
        Assert(env.Exists("old"), "Photo was deleted before its expiry notice could be published");
    }

    private static void RecoverPending(Fixture env)
    {
        var photo = new ReadyPhoto { status = "photo_ready", request_id = "recover",
            document_number = "DOC", sequence = 1, photo_type = "lomo", file = "new.jpg",
            relative_path = Path.Combine("transfer", "recover", "new.jpg") };
        TransferCache.StorePhoto(env.Root, "recover", env.Source("new.jpg", 10), 1,
            new JavaScriptSerializer().Serialize(photo));
        using (var transfer = new PhotoTransfer(new CaptureCommand
            { request_id = "recover", document_number = "DOC" }, env.Responses, env.Root))
        {
            Assert(transfer.RecoverInterrupted() && transfer.Photos.Count == 1, "Pending snapshot was lost");
            transfer.Flush();
        }
        Assert(File.Exists(Path.Combine(env.Responses, "recover.photo.0001.json")), "Recovered photo not announced");
    }

    private static void OrphanPending(Fixture env)
    {
        string folder = Path.Combine(env.Transfer, "orphan");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "photo.0001.json.pending"),
            new JavaScriptSerializer().Serialize(new ReadyPhoto {
                status = "photo_ready", request_id = "orphan", document_number = "DOC", sequence = 1,
                photo_type = "lomo", file = "missing.jpg", relative_path = Path.Combine("transfer", "orphan", "missing.jpg")
            }));
        using (var transfer = new PhotoTransfer(new CaptureCommand
            { request_id = "orphan", document_number = "DOC" }, env.Responses, env.Root))
        {
            Assert(transfer.RecoverInterrupted() && transfer.Photos.Count == 0, "Incomplete JPEG was announced");
            transfer.Flush();
        }
        Assert(Directory.GetFiles(env.Responses).Length == 0, "Orphan pending receipt was published");
    }

    private static void ExpiredWrite(Fixture env)
    {
        File.WriteAllText(env.Notice("expired"), "{}");
        ExpectIo(() => TransferCache.StorePhoto(env.Root, "expired", env.Source("new.jpg", 10), 1, "{}"));
        Assert(!env.Exists("expired"), "Expired request was recreated");
    }

    private static void InvalidLimits(Fixture env)
    {
        env.Request("old", 20, Now.AddMonths(-2)); env.Request("new", 20, Now.AddDays(-1));
        TransferCache.RunMaintenance(env.Root, new Config { transferRetentionMonths = 0, transferMaxMb = 0 }, Now);
        Assert(!env.Exists("old") && env.Exists("new"), "Invalid limits purged recent files");
    }

    private static void InterleavedFiles(Fixture env)
    {
        string first = env.Photo("a", "old.jpg", 400, Now.AddDays(-3));
        string fresh = env.Photo("a", "fresh.jpg", 400, Now.AddDays(-1));
        string middle = env.Photo("b", "middle.jpg", 400, Now.AddDays(-2));
        env.Clean(800);
        Assert(!File.Exists(first) && File.Exists(fresh) && File.Exists(middle),
            "Deleting an old request removed a fresher photo before another request's older photo");
        Assert(env.Bytes() == 800 && ExpiredFiles(env, "a").SequenceEqual(new[] { "old.jpg" }),
            "Partial eviction notice includes a surviving photo");
    }

    private static void TwoEvictions(Fixture env)
    {
        string old = env.Photo("a", "old.jpg", 400, Now.AddDays(-3));
        string fresh = env.Photo("a", "fresh.jpg", 400, Now.AddDays(-1));
        string middle = env.Photo("b", "middle.jpg", 400, Now.AddDays(-2));
        env.Clean(400);
        Assert(!File.Exists(old) && !File.Exists(middle) && File.Exists(fresh),
            "FIFO did not cross folders in photo timestamp order");
        Assert(env.Bytes() == 400, "FIFO did not stop when the quota was met");
    }

    private static void PartialAge(Fixture env)
    {
        string old = env.Photo("a", "old.jpg", 20, Now.AddMonths(-2));
        string fresh = env.Photo("a", "fresh.jpg", 20, Now.AddDays(-1));
        env.Clean();
        Assert(!File.Exists(old) && File.Exists(fresh), "Age cleanup removed a fresh sibling");
        Assert(ExpiredFiles(env, "a").SequenceEqual(new[] { "old.jpg" }), "Age notice is not per-photo");
    }

    private static void ReceiptPair(Fixture env)
    {
        string old = env.Photo("a", "old.jpg", 400, Now.AddDays(-3));
        string fresh = env.Photo("a", "fresh.jpg", 400, Now.AddDays(-1));
        string oldReceipt = env.Receipt("a", "old.jpg", 1, Now);
        string freshReceipt = env.Receipt("a", "fresh.jpg", 2, Now.AddDays(-4));
        long retained = new FileInfo(fresh).Length + new FileInfo(freshReceipt).Length;
        env.Clean(retained);
        Assert(!File.Exists(old) && !File.Exists(oldReceipt), "JPEG or its receipt leaked");
        Assert(File.Exists(fresh) && File.Exists(freshReceipt),
            "Receipt age overrode the newer JPEG timestamp");
        Assert(env.Bytes() == retained, "Paired receipt accounting is incorrect");
    }

    private static void PartialNotices(Fixture env)
    {
        env.Photo("a", "old.jpg", 100, Now.AddDays(-3));
        env.Photo("a", "middle.jpg", 100, Now.AddDays(-2));
        string fresh = env.Photo("a", "fresh.jpg", 100, Now.AddDays(-1));
        env.Clean(200);
        Assert(ExpiredFiles(env, "a").SequenceEqual(new[] { "old.jpg" }), "First partial notice is wrong");
        env.Clean(100);
        Assert(ExpiredFiles(env, "a").SequenceEqual(new[] { "old.jpg", "middle.jpg" }),
            "Second cleanup lost earlier expired filenames");
        Assert(File.Exists(fresh), "Fresh photo was included in the accumulated notice");
    }

    private static void LegacyNotice(Fixture env)
    {
        string old = env.Photo("a", "old.jpg", 100, Now.AddDays(-3));
        string fresh = env.Photo("a", "fresh.jpg", 100, Now.AddDays(-1));
        string notice = "{\"status\":\"transfer_expired\",\"request_id\":\"a\",\"reason\":\"size\"}";
        File.WriteAllText(env.Notice("a"), notice);
        env.Clean(100);
        Assert(!File.Exists(old) && File.Exists(fresh), "Legacy notice changed FIFO selection");
        Assert(File.ReadAllText(env.Notice("a")) == notice, "Legacy whole-request notice was narrowed");
    }

    private static void LockedNotice(Fixture env)
    {
        env.Photo("a", "old.jpg", 100, Now.AddDays(-3));
        string middle = env.Photo("a", "middle.jpg", 100, Now.AddDays(-2));
        string fresh = env.Photo("a", "fresh.jpg", 100, Now.AddDays(-1));
        env.Clean(200);
        using (var reader = new FileStream(env.Notice("a"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            env.Clean(100);
            Assert(File.Exists(middle) && File.Exists(fresh), "Photo was removed before its notice update");
            Assert(ExpiredFiles(env, "a").SequenceEqual(new[] { "old.jpg" }), "Locked notice changed");
        }
        env.Clean(100);
        Assert(!File.Exists(middle) && File.Exists(fresh), "Cleanup did not resume after notice unlock");
    }

    private static void AgeOrder(Fixture env)
    {
        string newest = env.Photo("a", "newest.jpg", 10, Now.AddMonths(-2));
        string oldest = env.Photo("a", "oldest.jpg", 10, Now.AddMonths(-4));
        string middle = env.Photo("b", "middle.jpg", 10, Now.AddMonths(-3));
        env.Clean();
        var removed = LogManager.Messages.Where(line => line.StartsWith("Transfer cache removed")).ToList();
        Assert(removed.Count == 3 && removed[0].EndsWith(oldest) && removed[1].EndsWith(middle)
            && removed[2].EndsWith(newest), "Age cleanup order is not globally oldest-first");
    }

    private static string[] ExpiredFiles(Fixture env, string request)
    {
        var notice = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
            File.ReadAllText(env.Notice(request)));
        return ((System.Collections.IEnumerable)notice["expired_files"]).Cast<object>()
            .Select(value => (string)value).ToArray();
    }

    private static void ExpectIo(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new Exception("Expected storage rejection");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "PhotoAgentCache_" + Guid.NewGuid().ToString("N"));
        internal string Transfer { get { return Path.Combine(Root, "transfer"); } }
        internal string Responses { get { return Path.Combine(Root, "responses"); } }
        internal Fixture()
        {
            Directory.CreateDirectory(Transfer); Directory.CreateDirectory(Responses);
            ConfigManager.Current = new Config();
            LogManager.Messages.Clear();
        }
        internal Config Config(long bytes)
        {
            return new Config { transferRetentionMonths = 1, transferMaxMb = bytes / (1024d * 1024d) };
        }
        internal string Request(string name, int bytes, DateTime date)
        {
            string folder = Path.Combine(Transfer, name); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "photo.jpg"); File.WriteAllBytes(file, new byte[bytes]);
            File.SetLastWriteTimeUtc(file, date); return folder;
        }
        internal string Source(string name, int bytes)
        {
            string file = Path.Combine(Root, name); File.WriteAllBytes(file, new byte[bytes]); return file;
        }
        internal string Photo(string request, string name, int bytes, DateTime date)
        {
            string folder = Path.Combine(Transfer, request); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name); File.WriteAllBytes(file, new byte[bytes]);
            File.SetLastWriteTimeUtc(file, date); return file;
        }
        internal string Receipt(string request, string photo, int sequence, DateTime date)
        {
            string file = Path.Combine(Transfer, request, "photo." + sequence.ToString("D4") + ".json");
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(new ReadyPhoto
            {
                status = "photo_ready", request_id = request, document_number = "DOC",
                sequence = sequence, photo_type = "extra", file = photo,
                relative_path = Path.Combine("transfer", request, photo)
            }));
            File.SetLastWriteTimeUtc(file, date); return file;
        }
        internal string Notice(string request) { return TransferCache.ExpiredNotice(Root, request); }
        internal bool Exists(string name) { return Directory.Exists(Path.Combine(Transfer, name)); }
        internal long Bytes() { return Directory.GetFiles(Transfer, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length); }
        internal void Clean(long bytes = 100 * 1024 * 1024) { TransferCache.RunMaintenance(Root, Config(bytes), Now); }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}

namespace PhotoAgent
{
    internal static class ConfigManager { internal static Config Current = new Config(); }
    internal static class LogManager
    {
        internal static readonly List<string> Messages = new List<string>();
        internal static void Write(string message) { Messages.Add(message); Console.WriteLine(message); }
    }
}
