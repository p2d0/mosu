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

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private const string resource_name = "osu.Game.Rulesets.MOsu.example_collections.json";

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Logger.Log("Beginning MOsu default collection import check..");

            bool alreadyImported = mosuRealm.Run(r =>
            {
                var state = r.All<PresetImportState>().FirstOrDefault();
                return state?.CollectionsImported ?? false;
            });

            if (!alreadyImported)
                ImportExampleCollections();
            else
                Logger.Log("MOsu default collections already imported, skipping.");
        }

        public void ImportExampleCollections()
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    string json = readEmbeddedCollections();

                    var transferObjects = JsonConvert.DeserializeObject<List<CollectionWithScoresTransferObject>>(json);

                    if (transferObjects == null || transferObjects.Count == 0)
                    {
                        Logger.Log("No embedded collections found to import.");
                        return;
                    }

                    // Step 1: Import collections (structure + hashes only)
                    HashSet<string> allImportedHashes = new HashSet<string>();
                    HashSet<int> allSetIds = new HashSet<int>();
                    int importedCollections = 0;

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

                            foreach (var beatmapEntry in dto.Beatmaps)
                            {
                                if (!collection.BeatmapMD5Hashes.Contains(beatmapEntry.BeatmapMD5Hash))
                                    collection.BeatmapMD5Hashes.Add(beatmapEntry.BeatmapMD5Hash);

                                allImportedHashes.Add(beatmapEntry.BeatmapMD5Hash);
                                allSetIds.Add(beatmapEntry.BeatmapSetId);
                            }
                        }
                    });

                    Logger.Log($"Imported {importedCollections} collections.");

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
                    });

                    // Step 2: Download missing maps (blocking)
                    var missingSetIds = allSetIds.Where(id =>
                    {
                        var existing = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false && OnlineID == $0", id).FirstOrDefault());
                        return existing == null;
                    }).ToList();

                    if (missingSetIds.Count > 0)
                        await startBackgroundDownload(missingSetIds);

                    // Step 3: Import scores (maps now exist)
                    int importedScores = 0;

                    realm.Write(r =>
                    {
                        foreach (var dto in transferObjects)
                        {
                            foreach (var beatmapEntry in dto.Beatmaps)
                            {
                                foreach (var sDto in beatmapEntry.Scores)
                                {
                                    var beatmap = r.All<BeatmapInfo>().FirstOrDefault(b => b.MD5Hash == beatmapEntry.BeatmapMD5Hash);
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
                        }
                    });

                    Logger.Log($"Imported {importedScores} scores.");
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

        private async Task startBackgroundDownload(List<int> missingSetIds)
        {
            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Starting collection download...",
                CompletionText = "Missing maps have been queued.",
            };

            notifications.Post(notification);

            var downloadedSets = new HashSet<int>(missingSetIds);

            Action<ArchiveDownloadRequest<IBeatmapSetInfo>> onDownloadFailed = req =>
            {
                int setId = req.Model.OnlineID;

                if (File.Exists(Path.Combine(Path.GetTempPath(), $"beatconnect_{setId}.osz")))
                {
                    Logger.Log($"Beatconnect download already in progress for set {setId}, skipping.");
                    return;
                }
                Logger.Log($"Download failed for set {setId}, trying beatconnect backup...");
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var webReq = new osu.Framework.IO.Network.WebRequest($"https://beatconnect.io/b/{setId}")
                        {
                            Timeout = 60000
                        };
                        webReq.Perform();

                        string filename = $"beatconnect_{setId}.osz";
                        string path = Path.Combine(Path.GetTempPath(), filename);

                        byte[] data;
                        using (var ms = new MemoryStream())
                        using (var src = webReq.ResponseStream)
                        {
                            src.CopyTo(ms);
                            data = ms.ToArray();
                        }
                        File.WriteAllBytes(path, data);

                        Schedule(() =>
                        {
                            var importNotification = new ProgressNotification
                            {
                                State = ProgressNotificationState.Active,
                                Text = $"Importing set {setId} from beatconnect..."
                            };
                            notifications.Post(importNotification);
                            Task.Run(async () =>
                            {
                                var result = await beatmapManager.Import(importNotification, new[] { new ImportTask(path) });
                                File.Delete(path);
                                Schedule(() =>
                                {
                                    if (result.Any())
                                    {
                                        importNotification.State = ProgressNotificationState.Completed;
                                        importNotification.CompletionText = $"Imported set {setId} from beatconnect backup";
                                    }
                                    else
                                    {
                                        notifications.Post(new SimpleErrorNotification { Text = $"Beatconnect import failed for set {setId}" });
                                    }
                                });
                            });
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Beatconnect backup failed for set {setId}");
                    }
                }, TaskCreationOptions.LongRunning);
            };

            downloader.DownloadFailed += onDownloadFailed;

            Task.Factory.StartNew(() =>
            {
                int processedCount = 0;

                foreach (var setId in missingSetIds)
                {
                    if (notification.State == ProgressNotificationState.Cancelled)
                        break;

                    updateNotificationProgress(notification, processedCount, missingSetIds.Count);

                    try
                    {
                        var onlineSet = new APIBeatmapSet { OnlineID = setId };
                        if (downloader.GetExistingDownload(onlineSet) == null)
                        {
                            downloader.Download(onlineSet);
                        }
                        Thread.Sleep(100);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        processedCount++;
                    }
                }
            }, TaskCreationOptions.LongRunning);

            // Wait for all downloads (official + beatconnect) to complete
            await Task.Run(async () =>
            {
                var maxWait = 3600;
                for (int i = 0; i < maxWait; i++)
                {
                    var localSets = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false").ToList().Select(b => b.OnlineID).ToList());
                    bool allDone = downloadedSets.All(id => localSets.Contains(id));
                    if (allDone) break;
                    await Task.Delay(1000);
                }
            });

            downloader.DownloadFailed -= onDownloadFailed;

            // Count truly unavailable: set IDs still missing after all downloads
            var localSetIds = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false").ToList().Select(b => b.OnlineID).ToHashSet());
            int unavailableCount = missingSetIds.Count(id => !localSetIds.Contains(id));

            Schedule(() =>
            {
                notification.CompletionText = "Download queueing finished.";
                if (unavailableCount > 0)
                    notification.CompletionText += $" ({unavailableCount} maps unavailable)";

                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });
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

    }
}
