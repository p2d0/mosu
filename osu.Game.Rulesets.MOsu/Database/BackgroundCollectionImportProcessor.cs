using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundCollectionImportProcessor : Component
    {
        [Resolved]
        private MOsuRealmAccess mosuRealm { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader downloader { get; set; } = null!;

        private const string resource_name = "osu.Game.Rulesets.MOsu.example_collections.json";

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Task.Factory.StartNew(() =>
            {
                try
                {
                    Logger.Log("Beginning MOsu default collection import check..");

                    bool alreadyImported = mosuRealm.Run(r =>
                    {
                        var state = r.All<PresetImportState>().FirstOrDefault();
                        return state?.CollectionsImported ?? false;
                    });

                    if (alreadyImported)
                    {
                        Logger.Log("MOsu default collections already imported, skipping.");
                        return;
                    }

                    string json = readEmbeddedCollections();

                    var transferObjects = JsonConvert.DeserializeObject<List<OsuSettingsSubsection.CollectionWithScoresTransferObject>>(json);

                    if (transferObjects == null || transferObjects.Count == 0)
                    {
                        Logger.Log("No embedded collections found to import.");
                        return;
                    }

                    HashSet<string> allImportedHashes = new HashSet<string>();
                    int importedCollections = 0;
                    int importedScores = 0;

                    realm.Write(r =>
                    {
                        foreach (var dto in transferObjects)
                        {
                            var collection = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == dto.Name);
                            if (collection == null)
                            {
                                collection = new BeatmapCollection(dto.Name);
                                r.Add(collection);
                                importedCollections++;
                            }

                            foreach (var hash in dto.BeatmapMD5Hashes)
                            {
                                if (!collection.BeatmapMD5Hashes.Contains(hash))
                                    collection.BeatmapMD5Hashes.Add(hash);

                                allImportedHashes.Add(hash);
                            }

                            foreach (var sDto in dto.Scores)
                            {
                                var beatmap = r.All<BeatmapInfo>().FirstOrDefault(b => b.MD5Hash == sDto.BeatmapHash);
                                var rulesetInfo = r.All<RulesetInfo>().FirstOrDefault(ru => ru.ShortName == sDto.RulesetShortName);

                                if (beatmap == null || rulesetInfo == null) continue;

                                bool scoreExists = r.All<ScoreInfo>()
                                    .Filter("BeatmapInfo.MD5Hash == $0 && TotalScore == $1 && Date == $2",
                                        sDto.BeatmapHash, sDto.TotalScore, sDto.Date)
                                    .Count() > 0;

                                if (scoreExists) continue;

                                var rulesetInstance = rulesetInfo.CreateInstance();
                                var mods = sDto.Mods.Select(m => m.ToMod(rulesetInstance)).ToArray();

                                var score = new ScoreInfo(beatmap, rulesetInfo)
                                {
                                    TotalScore = sDto.TotalScore,
                                    Accuracy = sDto.Accuracy,
                                    MaxCombo = sDto.MaxCombo,
                                    Rank = Enum.TryParse<ScoreRank>(sDto.Rank, out var rank) ? rank : ScoreRank.F,
                                    Date = sDto.Date,
                                    Mods = mods,
                                };

                                score.User = new APIUser { Username = @"Example mods configuration", Id = -123 };

                                foreach (var stat in sDto.Statistics)
                                {
                                    if (Enum.TryParse<HitResult>(stat.Key, out var result))
                                        score.Statistics[result] = stat.Value;
                                }

                                score.StatisticsJson = JsonConvert.SerializeObject(score.Statistics);

                                r.Add(score);
                                importedScores++;
                            }
                        }
                    });

                    Logger.Log($"Imported {importedCollections} collections and {importedScores} scores.");

                    // Identify missing maps for download
                    var missingHashes = realm.Run(r =>
                    {
                        var localBeatmaps = r.All<BeatmapInfo>()
                            .Filter("BeatmapSet.DeletePending == false")
                            .ToList();
                        var localHashSet = new HashSet<string>(localBeatmaps.Select(b => b.MD5Hash));
                        return allImportedHashes.Where(h => !localHashSet.Contains(h)).ToList();
                    });

                    mosuRealm.Write(r =>
                    {
                        var state = r.All<PresetImportState>().FirstOrDefault();
                        if (state == null)
                            r.Add(new PresetImportState { CollectionsImported = true });
                        else
                            state.CollectionsImported = true;
                    });

                    Schedule(() =>
                    {
                        notifications.Post(new SimpleNotification
                        {
                            Text = $"MOsu example collections imported! ({importedCollections} collections)"
                        });

                        if (missingHashes.Count > 0)
                            startBackgroundDownload(missingHashes);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to import MOsu default collections.");
                    Schedule(() => notifications.Post(new SimpleErrorNotification
                    {
                        Text = $"Failed to import MOsu collections: {ex.Message}"
                    }));
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void startBackgroundDownload(List<string> missingHashes)
        {
            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Starting collection download...",
                CompletionText = "Missing maps have been queued.",
            };

            notifications.Post(notification);

            Task.Factory.StartNew(() =>
            {
                int processedCount = 0;
                int failedCount = 0;
                var processedSets = new HashSet<int>();

                foreach (var hash in missingHashes)
                {
                    if (notification.State == ProgressNotificationState.Cancelled)
                        break;

                    updateNotificationProgress(notification, processedCount, missingHashes.Count);

                    try
                    {
                        var req = new GetBeatmapRequest(new BeatmapInfo { MD5Hash = hash });
                        req.AttachAPI(api);
                        req.Perform();

                        var onlineSet = req.Response?.BeatmapSet;

                        if (onlineSet != null)
                        {
                            if (!processedSets.Contains(onlineSet.OnlineID))
                            {
                                processedSets.Add(onlineSet.OnlineID);
                                if (downloader.GetExistingDownload(onlineSet) == null)
                                    downloader.Download(onlineSet);
                            }
                        }
                        else
                        {
                            failedCount++;
                        }
                        Thread.Sleep(100);
                    }
                    catch
                    {
                        failedCount++;
                    }
                    finally
                    {
                        processedCount++;
                    }
                }

                completeNotification(notification, processedCount, missingHashes.Count, failedCount);
            }, TaskCreationOptions.LongRunning);
        }

        private static string readEmbeddedCollections()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resource_name))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Embedded resource '{resource_name}' not found.");

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void updateNotificationProgress(ProgressNotification notification, int processedCount, int totalCount)
        {
            notification.Text = $"Checking map {processedCount} of {totalCount} online...";
            notification.Progress = (float)processedCount / totalCount;
        }

        private void completeNotification(ProgressNotification notification, int processedCount, int totalCount, int failedCount)
        {
            if (processedCount == totalCount)
            {
                notification.CompletionText = "Download queueing finished.";
                if (failedCount > 0)
                    notification.CompletionText += $" ({failedCount} maps unavailable)";

                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            }
            else
            {
                notification.State = ProgressNotificationState.Cancelled;
            }
        }
    }
}
