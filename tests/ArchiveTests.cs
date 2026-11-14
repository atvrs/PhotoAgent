using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using PhotoAgent;

internal static class ArchiveTests
{
    private static int passed;
    private static readonly DateTime Now = new DateTime(2026, 11, 14, 15, 30, 0);

    private static int Main()
    {
        try
        {
            Run("New configuration retains sixty months", Defaults);
            Run("Legacy days migrate to sixty months with backup and camera IDs preserved", Legacy);
            Run("Explicit monthly depth is preserved without rewriting configuration", CustomConfig);
            Run("Monthly setting wins over legacy days", BothSettings);
            Run("Zero months safely defaults to sixty", env => InvalidConfig(env, 0));
            Run("Negative months safely defaults to sixty", env => InvalidConfig(env, -1));
            Run("Text months safely defaults to sixty", env => InvalidConfig(env, "wrong"));
            Run("Fractional months safely defaults to sixty", env => InvalidConfig(env, 1.5));
            Run("Missing transfer limits migrate to one month and one hundred MB", TransferDefaults);
            Run("Explicit transfer limits are preserved", TransferCustom);
            Run("Invalid transfer limits safely normalize", TransferInvalid);
            Run("Five-year boundary uses folder month rather than filesystem dates", FiveYears);
            Run("One month retains the current month and preserves future folders", OneMonth);
            Run("Custom depth crosses a year boundary", CustomMonths);
            Run("Leap-day retention uses calendar months", LeapDay);
            Run("Very large retention avoids date underflow", LargeDepth);
            Run("Invalid in-memory depth does not delete the current archive", InvalidRuntime);
            Run("Size cleanup deletes the oldest calendar month first", SizeOrder);
            Run("Zero size limit disables only size cleanup", NoSizeLimit);
            Run("Invalid size limits cannot purge the archive", InvalidSizeLimits);
            Run("Unrecognized folders and loose files are preserved", UnknownFolders);
            Run("A locked old month does not stop cleanup of other old months", LockedMonth);
            Console.WriteLine("PASS: " + passed + " archive regression tests");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Run(string name, Action<Environment> test)
    {
        using (var env = new Environment())
            test(env);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Defaults(Environment env)
    {
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.archiveRetentionMonths == 60, "Default is not five years");
        var json = ReadConfig(env);
        Assert((int)json["archiveRetentionMonths"] == 60, "Monthly setting missing from new file");
        Assert(!json.ContainsKey("archiveRetentionDays"), "Obsolete day setting remains");
    }

    private static void Legacy(Environment env)
    {
        string original = "{\"archiveRetentionDays\":365,\"archiveMaxGb\":100,"
            + "\"lomoCameraId\":\"face-id\",\"documentCameraId\":\"document-id\",\"custom\":\"keep\"}";
        File.WriteAllText(env.ConfigFile, original);
        ConfigManager.Load(env.ConfigFile);
        var json = ReadConfig(env);
        Assert(ConfigManager.Current.archiveRetentionMonths == 60, "Old default was not migrated");
        Assert(ConfigManager.Current.lomoCameraId == "face-id"
            && ConfigManager.Current.documentCameraId == "document-id", "Camera assignments changed");
        Assert((string)json["custom"] == "keep", "Unknown configuration field was lost");
        Assert(!json.ContainsKey("archiveRetentionDays"), "Day setting was not removed");
        Assert(File.ReadAllText(env.ConfigFile + ".bak") == original, "Original backup differs");
        Assert(Directory.GetFiles(env.Root, "*.tmp").Length == 0, "Configuration temp file leaked");
    }

    private static void CustomConfig(Environment env)
    {
        string json = "{ \"archiveRetentionMonths\": 24, \"archiveMaxGb\": 100,"
            + " \"transferRetentionMonths\": 1, \"transferMaxMb\": 100 }";
        File.WriteAllText(env.ConfigFile, json);
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.archiveRetentionMonths == 24, "Custom monthly depth changed");
        Assert(File.ReadAllText(env.ConfigFile) == json, "Valid configuration was rewritten");
    }

    private static void BothSettings(Environment env)
    {
        File.WriteAllText(env.ConfigFile, "{\"archiveRetentionMonths\":36,\"archiveRetentionDays\":7}");
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.archiveRetentionMonths == 36, "Legacy days overrode months");
        Assert(!ReadConfig(env).ContainsKey("archiveRetentionDays"), "Obsolete setting remains");
    }

    private static void InvalidConfig(Environment env, object months)
    {
        File.WriteAllText(env.ConfigFile, new JavaScriptSerializer().Serialize(new
        {
            archiveRetentionMonths = months, lomoCameraId = "keep"
        }));
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.archiveRetentionMonths == 60, "Unsafe retention was accepted");
        Assert(ConfigManager.Current.lomoCameraId == "keep", "Normalization lost another field");
    }

    private static void TransferDefaults(Environment env)
    {
        File.WriteAllText(env.ConfigFile, "{\"archiveRetentionMonths\":60,\"documentCameraId\":\"keep\"}");
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.transferRetentionMonths == 1
            && ConfigManager.Current.transferMaxMb == 100, "Transfer defaults are missing");
        Assert(ConfigManager.Current.documentCameraId == "keep", "Migration changed a camera");
    }

    private static void TransferCustom(Environment env)
    {
        File.WriteAllText(env.ConfigFile, "{\"archiveRetentionMonths\":60,\"transferRetentionMonths\":2,\"transferMaxMb\":50.5}");
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.transferRetentionMonths == 2
            && ConfigManager.Current.transferMaxMb == 50.5, "Explicit transfer limits changed");
    }

    private static void TransferInvalid(Environment env)
    {
        File.WriteAllText(env.ConfigFile, "{\"archiveRetentionMonths\":60,\"transferRetentionMonths\":0,\"transferMaxMb\":\"bad\"}");
        ConfigManager.Load(env.ConfigFile);
        Assert(ConfigManager.Current.transferRetentionMonths == 1
            && ConfigManager.Current.transferMaxMb == 100, "Invalid transfer limits disabled cleanup");
    }

    private static void FiveYears(Environment env)
    {
        string old = env.Month("2021-11");
        string boundary = env.Month("2021-12");
        string current = env.Month("2026-11");
        Directory.SetCreationTime(old, Now);
        Directory.SetCreationTime(current, new DateTime(2000, 1, 1));
        env.Clean(60);
        Assert(!Directory.Exists(old), "Month before the sixty-month window survived");
        Assert(Directory.Exists(boundary), "First retained month was deleted");
        Assert(Directory.Exists(current), "Filesystem creation time deleted a current month");
    }

    private static void OneMonth(Environment env)
    {
        env.Month("2026-10"); env.Month("2026-11"); env.Month("2026-12");
        env.Clean(1);
        Assert(!env.Exists("2026-10") && env.Exists("2026-11") && env.Exists("2026-12"),
            "One-month retention handled month boundaries incorrectly");
    }

    private static void CustomMonths(Environment env)
    {
        env.Month("2025-11"); env.Month("2025-12"); env.Month("2026-11");
        env.Clean(12);
        Assert(!env.Exists("2025-11") && env.Exists("2025-12"), "Custom twelve-month depth failed");
    }

    private static void LeapDay(Environment env)
    {
        env.Month("2019-02"); env.Month("2019-03");
        ArchiveManager.RunMaintenance(env.Archive, new Config(), new DateTime(2024, 2, 29));
        Assert(!env.Exists("2019-02") && env.Exists("2019-03"), "Leap-year month cutoff failed");
    }

    private static void LargeDepth(Environment env)
    {
        env.Month("1900-01");
        env.Clean(int.MaxValue);
        Assert(env.Exists("1900-01"), "Large depth overflowed and removed data");
    }

    private static void InvalidRuntime(Environment env)
    {
        env.Month("2026-11"); env.Month("2021-12");
        env.Clean(0);
        Assert(env.Exists("2026-11") && env.Exists("2021-12"), "Invalid depth purged valid data");
    }

    private static void SizeOrder(Environment env)
    {
        string old = env.Month("2026-09", 1024), current = env.Month("2026-11", 1024);
        Directory.SetCreationTime(old, Now);
        Directory.SetCreationTime(current, new DateTime(2000, 1, 1));
        env.Clean(60, 1500d / (1024d * 1024d * 1024d));
        Assert(!env.Exists("2026-09") && env.Exists("2026-11"), "Size cleanup used filesystem dates");
    }

    private static void NoSizeLimit(Environment env)
    {
        env.Month("2021-11"); env.Month("2026-11", 4096);
        env.Clean(60, 0);
        Assert(!env.Exists("2021-11") && env.Exists("2026-11"), "Zero quota disabled age cleanup");
    }

    private static void InvalidSizeLimits(Environment env)
    {
        env.Month("2026-11", 4096);
        foreach (double limit in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            env.Clean(60, limit);
            Assert(env.Exists("2026-11"), "Invalid or huge size limit purged data");
        }
    }

    private static void UnknownFolders(Environment env)
    {
        env.Month("not-a-month", 4096); env.Month("2021-13", 4096);
        File.WriteAllText(Path.Combine(env.Archive, "keep.txt"), "keep");
        env.Clean(1, 1d / (1024d * 1024d * 1024d));
        Assert(env.Exists("not-a-month") && env.Exists("2021-13"), "Unknown folders were deleted");
        Assert(File.Exists(Path.Combine(env.Archive, "keep.txt")), "Loose file was deleted");
    }

    private static void LockedMonth(Environment env)
    {
        env.Month("2020-01"); env.Month("2020-02");
        using (var reader = new FileStream(Path.Combine(env.Archive, "2020-01", "photo.jpg"),
            FileMode.Open, FileAccess.Read, FileShare.Read))
            env.Clean(60);
        Assert(env.Exists("2020-01") && !env.Exists("2020-02"), "A locked month stopped cleanup");
    }

    private static Dictionary<string, object> ReadConfig(Environment env)
    {
        return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
            File.ReadAllText(env.ConfigFile));
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private sealed class Environment : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "PhotoAgentArchive_" + Guid.NewGuid().ToString("N"));
        internal string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        internal string Archive { get { return Path.Combine(Root, "archive"); } }
        internal Environment() { Directory.CreateDirectory(Archive); }
        internal string Month(string month, int bytes = 16)
        {
            string folder = Path.Combine(Archive, month);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "photo.jpg"), new byte[bytes]);
            return folder;
        }
        internal bool Exists(string month) { return Directory.Exists(Path.Combine(Archive, month)); }
        internal void Clean(int months, double maxGb = 0)
        {
            ArchiveManager.RunMaintenance(Archive, new Config
            {
                archiveRetentionMonths = months, archiveMaxGb = maxGb
            }, Now);
        }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}

namespace PhotoAgent
{
    internal static class LogManager
    {
        internal static void Write(string message) { Console.WriteLine(message); }
    }
}
