using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Realms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.MOsu.Database
{
public static class DatabaseMigrationInitializer
{
private const string STALE_SHORT_NAME = "mosususu";
private const string NEW_SHORT_NAME = "mosu";

    [ModuleInitializer]
    public static void Initialize()
    {
        Logger.Log("[MOsu Migration] Initializer launched.");

        var thread = new Thread(() =>
        {
            try
            {
                string? storagePath = findOsuStoragePath();
                if (storagePath == null)
                {
                    Logger.Log("[MOsu Migration] osu! storage path not found.");
                    return;
                }

                if (migrateClientRealm(storagePath))
                    Logger.Log("[MOsu Migration] Migration finished, please restart the game for ruleset to load", level: LogLevel.Important);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MOsu Migration] Migration failed.");
            }
        });

        thread.IsBackground = true;
        thread.Start();
    }

    private static bool migrateClientRealm(string storagePath)
    {
        string? realmPath = findRealmFile(storagePath);
        if (realmPath == null)
        {
            Logger.Log("[MOsu Migration] client.realm not found.");
            return false;
        }

        Logger.Log($"[MOsu Migration] Using realm: {realmPath}");

        var config = new RealmConfiguration(realmPath)
        {
            SchemaVersion = 51,
            IsReadOnly = false,
        };

        using var realm = Realm.GetInstance(config);

        // Ensure latest state
        realm.Refresh();

        var staleEntry = realm.Find<RulesetInfo>(STALE_SHORT_NAME);
        var existingNewEntries = realm.All<RulesetInfo>()
                                      .Where(r => r.ShortName == NEW_SHORT_NAME)
                                      .ToList();

        if (staleEntry == null && existingNewEntries.Count == 0)
        {
            Logger.Log("[MOsu Migration] Nothing to migrate.");
            return false;
        }

        bool migrated = false;

        realm.Write(() =>
        {
            // --- Deduplicate existing "mosu"
            RulesetInfo? newEntry = null;

            if (existingNewEntries.Count > 0)
            {
                newEntry = existingNewEntries[0];

                foreach (var dup in existingNewEntries.Skip(1))
                {
                    migrated = true;
                    Logger.Log("[MOsu Migration] Removing duplicate mosu entry.");
                    realm.Remove(dup);
                }
            }

            if (staleEntry != null)
            {
                migrated = true;
                Logger.Log("[MOsu Migration] Found stale entry, migrating...");

                string instantiationInfo = staleEntry.InstantiationInfo;
                int lastApplied = staleEntry.LastAppliedDifficultyVersion;

                // --- Force clean recreation (avoids partial corruption)
                if (newEntry != null)
                {
                    Logger.Log("[MOsu Migration] Replacing existing mosu entry.");
                    realm.Remove(newEntry);
                    newEntry = null;
                }

                newEntry = realm.Add(new RulesetInfo
                {
                    ShortName = NEW_SHORT_NAME,
                    OnlineID = -1,
                    Name = "MOsu!",
                    InstantiationInfo = instantiationInfo,
                    LastAppliedDifficultyVersion = lastApplied,
                    Available = true,
                });

                // --- Reassign scores (reference compare = safer)
                var scores = realm.All<ScoreInfo>()
                                  .Where(s => s.Ruleset == staleEntry)
                                  .ToList();

                if (scores.Count > 0)
                {
                    Logger.Log($"[MOsu Migration] Reassigning {scores.Count} scores...");
                    foreach (var s in scores)
                        s.Ruleset = newEntry;
                }

                // --- Reassign mod presets
                var presets = realm.All<ModPreset>()
                                   .Where(p => p.Ruleset == staleEntry)
                                   .ToList();

                if (presets.Count > 0)
                {
                    Logger.Log($"[MOsu Migration] Reassigning {presets.Count} mod presets...");
                    foreach (var p in presets)
                        p.Ruleset = newEntry;
                }

                // --- Fix orphaned presets
                var orphaned = realm.All<ModPreset>()
                                    .Where(p => p.Ruleset == null)
                                    .ToList();

                if (orphaned.Count > 0)
                {
                    Logger.Log($"[MOsu Migration] Fixing {orphaned.Count} orphaned presets...");
                    foreach (var p in orphaned)
                        p.Ruleset = newEntry;
                }

                // --- Remove stale LAST (critical)
                Logger.Log("[MOsu Migration] Removing stale entry.");
                realm.Remove(staleEntry);
            }
        });

        if (!migrated)
        {
            Logger.Log("[MOsu Migration] Nothing to migrate.");
            return false;
        }

        return true;
    }

    private static string? findOsuStoragePath()
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

        string? custom = readCustomPathFromStorageIni(baseDir);
        if (custom != null) return custom;

        return Directory.Exists(baseDir) ? baseDir : null;
    }

    private static string? readCustomPathFromStorageIni(string storageDir)
    {
        string iniPath = Path.Combine(storageDir, "storage.ini");
        if (!File.Exists(iniPath)) return null;

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
        catch { }

        return null;
    }

    private static string? findRealmFile(string storagePath)
    {
        string releasePath = Path.Combine(storagePath, "client.realm");
        if (File.Exists(releasePath)) return releasePath;

        try
        {
            string[] files = Directory.GetFiles(storagePath, "client*.realm");
            if (files.Length > 0) return files[0];
        }
        catch { }

        return null;
    }
}
}
