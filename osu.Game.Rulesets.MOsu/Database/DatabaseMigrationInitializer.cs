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
            Logger.Log("[MOsu Migration] DatabaseMigrationInitializer launched.");
            var thread = new Thread(() =>
            {
                try
                {
                    string? storagePath = findOsuStoragePath();
                    if (storagePath == null)
                    {
                        Logger.Log("[MOsu Migration] Could not find osu! storage path.");
                        return;
                    }

                    migrateClientRealm(storagePath);

                    Logger.Log("[MOsu Migration] Database migration completed successfully.");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[MOsu Migration] Failed during ruleset storage migration.");
                }
            });
            thread.Start();
        }

        private static void migrateClientRealm(string storagePath)
        {
            string? realmPath = findRealmFile(storagePath);
            if (realmPath == null)
            {
                Logger.Log("[MOsu Migration] Could not find client.realm file.");
                return;
            }

            Logger.Log($"[MOsu Migration] Found client.realm at {realmPath}.");

            var config = new RealmConfiguration(realmPath)
            {
                SchemaVersion = 51,
                IsReadOnly = false,
            };

            using var realm = Realm.GetInstance(config);

            var staleEntry = realm.Find<RulesetInfo>(STALE_SHORT_NAME);
            var newEntry = realm.Find<RulesetInfo>(NEW_SHORT_NAME);

            if (staleEntry == null && newEntry == null)
            {
                Logger.Log("[MOsu Migration] No ruleset entries found. Nothing to migrate.");
                return;
            }

            realm.Write(() =>
            {
                if (staleEntry != null)
                {
                    string instantiationInfo = staleEntry.InstantiationInfo;
                    int lastApplied = staleEntry.LastAppliedDifficultyVersion;

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

                    var scores = realm.All<ScoreInfo>()
                                     .Where(s => s.Ruleset != null && s.Ruleset.ShortName == STALE_SHORT_NAME)
                                     .ToList();

                    if (scores.Count > 0)
                    {
                        Logger.Log($"[MOsu Migration] Reassigning {scores.Count} scores to {NEW_SHORT_NAME}...");
                        foreach (var s in scores)
                            s.Ruleset = newEntry;
                    }

                    var stalePresets = realm.All<ModPreset>()
                                           .Where(p => p.Ruleset != null && p.Ruleset.ShortName == STALE_SHORT_NAME)
                                           .ToList();

                    if (stalePresets.Count > 0)
                    {
                        Logger.Log($"[MOsu Migration] Reassigning {stalePresets.Count} ModPreset entries to {NEW_SHORT_NAME}...");
                        foreach (var p in stalePresets)
                            p.Ruleset = newEntry;
                    }

                    realm.Remove(staleEntry);
                }

                // Always fix orphaned ModPresets (null Ruleset from deleted stale entry)
                // if (newEntry != null)
                // {
                //     var orphaned = realm.All<ModPreset>()
                //                        .Where(p => p.Ruleset == null)
                //                        .ToList();

                //     if (orphaned.Count > 0)
                //     {
                //         Logger.Log($"[MOsu Migration] Fixing {orphaned.Count} orphaned ModPreset entries (null Ruleset) to {NEW_SHORT_NAME}...");
                //         foreach (var p in orphaned)
                //             p.Ruleset = newEntry;
                //     }
                // }
            });
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
