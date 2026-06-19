using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Realms;
using Realms.Schema;

namespace RealmFixup;

// ─── All types needed to match osu! Realm schema v51 ───

[MapTo("Ruleset")]
public partial class RulesetInfo : IRealmObject
{
    [PrimaryKey]
    public string ShortName { get; set; } = string.Empty;

    [Indexed]
    public int OnlineID { get; set; } = -1;

    public string Name { get; set; } = string.Empty;

    public string InstantiationInfo { get; set; } = string.Empty;

    public int LastAppliedDifficultyVersion { get; set; }

    public bool Available { get; set; }
}

[MapTo("File")]
public partial class RealmFile : IRealmObject
{
    [PrimaryKey]
    public string Hash { get; set; } = string.Empty;
}

public partial class RealmNamedFileUsage : IEmbeddedObject
{
    public RealmFile File { get; set; } = null!;

    public string Filename { get; set; } = string.Empty;
}

public partial class RealmUser : IEmbeddedObject
{
    public int OnlineID { get; set; } = 1;

    public string Username { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;
}

public partial class BeatmapDifficulty : IEmbeddedObject
{
    public float DrainRate { get; set; }

    public float CircleSize { get; set; }

    public float OverallDifficulty { get; set; }

    public float ApproachRate { get; set; }

    public double SliderMultiplier { get; set; }

    public double SliderTickRate { get; set; }
}

public partial class BeatmapUserSettings : IEmbeddedObject
{
    public double Offset { get; set; }
}

[MapTo("BeatmapMetadata")]
public partial class BeatmapMetadata : IRealmObject
{
    public string Title { get; set; } = string.Empty;

    public string TitleUnicode { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string ArtistUnicode { get; set; } = string.Empty;

    public RealmUser Author { get; set; } = null!;

    public string Source { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public IList<string> UserTags { get; } = null!;

    public int PreviewTime { get; set; }

    public string AudioFile { get; set; } = string.Empty;

    public string BackgroundFile { get; set; } = string.Empty;
}

[MapTo("BeatmapSet")]
public partial class BeatmapSetInfo : IRealmObject
{
    [PrimaryKey]
    public Guid ID { get; set; }

    [Indexed]
    public int OnlineID { get; set; } = -1;

    public DateTimeOffset DateAdded { get; set; }

    public DateTimeOffset? DateSubmitted { get; set; }

    public DateTimeOffset? DateRanked { get; set; }

    public IList<BeatmapInfo> Beatmaps { get; } = null!;

    public IList<RealmNamedFileUsage> Files { get; } = null!;

    public int Status { get; set; }

    public bool DeletePending { get; set; }

    public string Hash { get; set; } = string.Empty;

    public bool Protected { get; set; }
}

[MapTo("Beatmap")]
public partial class BeatmapInfo : IRealmObject
{
    [PrimaryKey]
    public Guid ID { get; set; }

    public string DifficultyName { get; set; } = string.Empty;

    public RulesetInfo Ruleset { get; set; } = null!;

    public BeatmapDifficulty Difficulty { get; set; } = null!;

    public BeatmapMetadata Metadata { get; set; } = null!;

    public BeatmapUserSettings UserSettings { get; set; } = null!;

    public BeatmapSetInfo? BeatmapSet { get; set; }

    public int Status { get; set; }

    [Indexed]
    public int OnlineID { get; set; } = -1;

    public double Length { get; set; }

    public double BPM { get; set; }

    public string Hash { get; set; } = string.Empty;

    public double StarRating { get; set; } = -1;

    [Indexed]
    public string MD5Hash { get; set; } = string.Empty;

    public string OnlineMD5Hash { get; set; } = string.Empty;

    public DateTimeOffset? LastLocalUpdate { get; set; }

    public DateTimeOffset? LastOnlineUpdate { get; set; }

    public bool Hidden { get; set; }

    public int EndTimeObjectCount { get; set; } = -1;

    public int TotalObjectCount { get; set; } = -1;

    public DateTimeOffset? LastPlayed { get; set; }

    public int BeatDivisor { get; set; } = 4;

    public double? EditorTimestamp { get; set; }
}

[MapTo("Score")]
public partial class ScoreInfo : IRealmObject
{
    [PrimaryKey]
    public Guid ID { get; set; }

    public BeatmapInfo? BeatmapInfo { get; set; }

    public string ClientVersion { get; set; } = string.Empty;

    public string BeatmapHash { get; set; } = string.Empty;

    public RulesetInfo Ruleset { get; set; } = null!;

    public IList<RealmNamedFileUsage> Files { get; } = null!;

    public string Hash { get; set; } = string.Empty;

    public bool DeletePending { get; set; }

    public long TotalScore { get; set; }

    public long TotalScoreWithoutMods { get; set; }

    public int TotalScoreVersion { get; set; }

    public long? LegacyTotalScore { get; set; }

    public bool BackgroundReprocessingFailed { get; set; }

    public int MaxCombo { get; set; }

    public double Accuracy { get; set; }

    public DateTimeOffset Date { get; set; }

    public double? PP { get; set; }

    [Indexed]
    public long OnlineID { get; set; } = -1;

    [Indexed]
    public long LegacyOnlineID { get; set; } = -1;

    [MapTo("User")]
    public RealmUser RealmUser { get; set; } = null!;

    [MapTo("Mods")]
    public string ModsJson { get; set; } = string.Empty;

    [MapTo("Statistics")]
    public string StatisticsJson { get; set; } = string.Empty;

    [MapTo("MaximumStatistics")]
    public string MaximumStatisticsJson { get; set; } = string.Empty;

    public IList<int> Pauses { get; } = null!;

    public int Rank { get; set; }

    public int Combo { get; set; }

    public bool IsLegacyScore { get; set; }
}

// ─── Program ───

class Program
{
    const string STALE_SHORT_NAME = "mosususu";
    const string NEW_SHORT_NAME = "mosu";

    static int Main(string[] args)
    {
        string realmPath;

        if (args.Length > 0)
        {
            realmPath = Path.GetFullPath(args[0]);

            if (!File.Exists(realmPath))
            {
                Console.Error.WriteLine($"File not found: {realmPath}");
                return 1;
            }
        }
        else
        {
            string? storagePath = FindOsuStoragePath();

            if (storagePath == null)
            {
                Console.Error.WriteLine("Could not find osu! storage directory.");
                return 1;
            }

            Console.WriteLine($"Storage: {storagePath}");

            string? found = FindRealmFile(storagePath);

            if (found == null)
            {
                Console.Error.WriteLine("Could not find client.realm in storage directory.");
                return 1;
            }

            realmPath = found;
        }

        Console.WriteLine($"Realm: {realmPath}");

        var config = new RealmConfiguration(realmPath)
        {
            SchemaVersion = 51,
            IsReadOnly = false,
        };

        using var realm = Realm.GetInstance(config);

        // Step 1: Check for stale entry
        var staleEntry = realm.Find<RulesetInfo>(STALE_SHORT_NAME);

        if (staleEntry != null)
        {
            Console.WriteLine($"Found stale: ShortName=\"{staleEntry.ShortName}\", OnlineID={staleEntry.OnlineID}, Name=\"{staleEntry.Name}\"");

            string instantiationInfo = staleEntry.InstantiationInfo;
            int lastApplied = staleEntry.LastAppliedDifficultyVersion;

            var newEntry = realm.Find<RulesetInfo>(NEW_SHORT_NAME);
            bool hadNewEntry = newEntry != null;

            realm.Write(() =>
            {
                if (newEntry == null)
                {
                    newEntry = realm.Add(new RulesetInfo
                    {
                        ShortName = NEW_SHORT_NAME,
                        OnlineID = -1,
                        Name = "MOsu!",
                        InstantiationInfo = instantiationInfo,
                        LastAppliedDifficultyVersion = lastApplied,
                        Available = true,
                    });
                }
                else
                {
                    newEntry.OnlineID = -1;
                    newEntry.Name = "MOsu!";
                    newEntry.Available = true;
                }

                // Reassign scores before removing
                var scores = realm.All<ScoreInfo>().Where(s => s.Ruleset == staleEntry).ToList();
                int reassigned = 0;
                foreach (var score in scores)
                {
                    score.Ruleset = newEntry;
                    reassigned++;
                }

                realm.Remove(staleEntry);
                Console.WriteLine($"Reassigned {reassigned} score(s) from \"{STALE_SHORT_NAME}\" to \"{NEW_SHORT_NAME}\".");
            });

            Console.WriteLine(hadNewEntry
                ? $"Updated existing RulesetInfo(\"{NEW_SHORT_NAME}\") and removed \"{STALE_SHORT_NAME}\"."
                : $"Created RulesetInfo(\"{NEW_SHORT_NAME}\", OnlineID=-1) and removed \"{STALE_SHORT_NAME}\".");
        }
        else
        {
            Console.WriteLine($"No stale RulesetInfo(\"{STALE_SHORT_NAME}\") found.");
        }

        // Step 2: Fix orphaned scores (null Ruleset)
        var newRuleset = realm.Find<RulesetInfo>(NEW_SHORT_NAME);

        if (newRuleset != null)
        {
            var orphaned = realm.All<ScoreInfo>().Where(s => s.Ruleset == null).ToList();

            if (orphaned.Count > 0)
            {
                Console.WriteLine($"Found {orphaned.Count} orphaned score(s) with null Ruleset. Reassigning to \"{NEW_SHORT_NAME}\"...");

                realm.Write(() =>
                {
                    foreach (var score in orphaned)
                        score.Ruleset = newRuleset;
                });

                Console.WriteLine($"Reassigned {orphaned.Count} orphaned score(s).");
            }
            else
            {
                Console.WriteLine("No orphaned scores found.");
            }
        }

        // Summary
        var allScores = realm.All<ScoreInfo>().ToList();
        var nullRuleset = allScores.Count(s => s.Ruleset == null);
        Console.WriteLine($"\n=== Final state ===");
        Console.WriteLine($"  Total scores: {allScores.Count}");
        Console.WriteLine($"  Null Ruleset: {nullRuleset}");

        foreach (var g in allScores.GroupBy(s => s.Ruleset?.ShortName ?? "(null)").OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key}: {g.Count()}");

        return 0;
    }

    static string? FindOsuStoragePath()
    {
        string baseDir;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            baseDir = Path.Combine(localAppData, "osu");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            baseDir = Path.Combine(xdgData, "osu");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support", "osu");
        }
        else
        {
            return null;
        }

        string? custom = ReadCustomPathFromStorageIni(baseDir);

        if (custom != null)
            return custom;

        if (Directory.Exists(baseDir))
            return baseDir;

        return null;
    }

    static string? ReadCustomPathFromStorageIni(string storageDir)
    {
        string iniPath = Path.Combine(storageDir, "storage.ini");

        if (!File.Exists(iniPath))
            return null;

        try
        {
            foreach (string line in File.ReadLines(iniPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("FullPath", StringComparison.OrdinalIgnoreCase))
                {
                    int eq = trimmed.IndexOf('=');

                    if (eq < 0) continue;

                    string value = trimmed.Substring(eq + 1).Trim().Trim('"');

                    if (value.Length > 0 && Directory.Exists(value))
                        return value;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    static string? FindRealmFile(string storagePath)
    {
        string releasePath = Path.Combine(storagePath, "client.realm");

        if (File.Exists(releasePath))
            return releasePath;

        try
        {
            string[] files = Directory.GetFiles(storagePath, "client*.realm");

            if (files.Length > 0)
                return files[0];
        }
        catch
        {
        }

        return null;
    }
}
