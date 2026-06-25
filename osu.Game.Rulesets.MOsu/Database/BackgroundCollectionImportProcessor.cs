using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using osu.Game.Online.API;
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

                    // Step 1: Import collections
                    var (allSetIds, importedCollections) = importCollections(transferObjects);
                    markCollectionsImported();

                    Schedule(() =>
                    {
                        notifications.Post(new SimpleNotification
                        {
                            Text = $"MOsu example collections imported! ({importedCollections} collections)"
                        });
                    });

                    // Step 2: Download missing maps (blocking)
                    var missingSetIds = getMissingSetIds(allSetIds);
                    if (missingSetIds.Count > 0)
                        await startBackgroundDownload(missingSetIds);

                    // Step 3: Import scores
                    int importedScores = importScores(transferObjects);
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

        private (HashSet<int> setIds, int count) importCollections(List<CollectionWithScoresTransferObject> transferObjects)
        {
            var allSetIds = new HashSet<int>();
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
                        allSetIds.Add(beatmapEntry.BeatmapSetId);
                    }
                }
            });

            Logger.Log($"Imported {importedCollections} collections.");
            return (allSetIds, importedCollections);
        }

        private void markCollectionsImported()
        {
            mosuRealm.Write(r =>
            {
                var state = r.All<PresetImportState>().FirstOrDefault();
                if (state == null)
                    r.Add(new PresetImportState { CollectionsImported = true });
                else
                    state.CollectionsImported = true;
            });
        }

        private List<int> getMissingSetIds(HashSet<int> allSetIds)
        {
            return allSetIds.Where(id =>
            {
                var existing = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false && OnlineID == $0", id).FirstOrDefault());
                return existing == null;
            }).ToList();
        }

        private int importScores(List<CollectionWithScoresTransferObject> transferObjects)
        {
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

            return importedScores;
        }

        private async Task startBackgroundDownload(List<int> missingSetIds)
        {
            if (!api.IsLoggedIn)
            {
                Schedule(() => notifications.Post(new SimpleErrorNotification { Text = "Cannot download maps: not logged in." }));
                return;
            }

            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Starting collection download...",
                CompletionText = "Missing maps have been queued.",
            };

            notifications.Post(notification);

            var remainingSets = new HashSet<int>(missingSetIds);
            var failedSets = new HashSet<int>();
            var lockObj = new object();

            downloader.DownloadFailed += createDownloadFailedHandler(lockObj, failedSets);

            // Queue all downloads
            await Task.Factory.StartNew(() =>
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
                            downloader.Download(onlineSet);
                        Thread.Sleep(100);
                    }
                    catch { }
                    finally
                    {
                        processedCount++;
                    }
                }
            }, TaskCreationOptions.LongRunning);

            // Wait for downloads to appear in realm or fail
            await Task.Run(async () =>
            {
                for (int i = 0; i < 300; i++) // 5 min max
                {
                    var localIds = realm.Run(r =>
                        r.All<BeatmapSetInfo>().Filter("DeletePending == false").ToList()
                            .Select(b => b.OnlineID).ToHashSet());

                    lock (lockObj)
                    {
                        bool allDone = missingSetIds.All(id => localIds.Contains(id) || failedSets.Contains(id));
                        if (allDone) break;
                    }
                    await Task.Delay(1000);
                }
            });

            downloader.DownloadFailed -= createDownloadFailedHandler(lockObj, failedSets);

            int unavailableCount;
            lock (lockObj) unavailableCount = failedSets.Count;

            Schedule(() =>
            {
                notification.CompletionText = "Download queueing finished.";
                if (unavailableCount > 0)
                    notification.CompletionText += $" ({unavailableCount} maps unavailable)";

                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });
        }

        private Action<ArchiveDownloadRequest<IBeatmapSetInfo>> createDownloadFailedHandler(object lockObj, HashSet<int> failedSets)
        {
            return req =>
            {
                int setId = req.Model.OnlineID;
                if (File.Exists(Path.Combine(Path.GetTempPath(), $"nekoha_{setId}.osz")))
                {
                    Logger.Log($"Beatconnect download already in progress for set {setId}, skipping.");
                    return;
                }
                tryMirrorFallback(setId, lockObj, failedSets);
            };
        }

        private void tryMirrorFallback(int setId, object lockObj, HashSet<int> failedSets)
        {
            Logger.Log($"Download failed for set {setId}, trying nekoha mirror backup...");
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var response = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(60)
                    }.GetAsync($"https://mirror.nekoha.moe/api4/download/{setId}").Result;

                    string filename = $"nekoha_{setId}.osz";
                    string path = Path.Combine(Path.GetTempPath(), filename);

                    byte[] data = response.Content.ReadAsByteArrayAsync().Result;
                    Logger.Log($"Nekoha mirror response: status={response.StatusCode}, content-type={response.Content.Headers.ContentType?.MediaType}, size={data.Length} bytes");
                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"Nekoha mirror returned {response.StatusCode}");
                    File.WriteAllBytes(path, data);

                    Schedule(() =>
                    {
                        var importNotification = new ProgressNotification
                        {
                            State = ProgressNotificationState.Active,
                            Text = $"Importing set {setId} from nekoha mirror..."
                        };
                        notifications.Post(importNotification);
                        Task.Run(async () =>
                        {
                            long fileSize = new FileInfo(path).Length;
                            var result = await beatmapManager.Import(importNotification, new[] { new ImportTask(path) });
                            File.Delete(path);
                            Schedule(() =>
                            {
                                if (result.Any())
                                {
                                    importNotification.State = ProgressNotificationState.Completed;
                                    importNotification.CompletionText = $"Imported set {setId} from nekoha mirror backup";
                                }
                                else
                                {
                                    Logger.Error(new Exception($"Nekoha mirror import returned 0 items for set {setId}. File size: {fileSize} bytes."), "Nekoha mirror import empty");
                                    importNotification.State = ProgressNotificationState.Cancelled;
                                    notifications.Post(new SimpleErrorNotification { Text = $"Nekoha mirror import failed for set {setId}" });
                                    lock (lockObj) failedSets.Add(setId);
                                }
                            });
                        });
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Nekoha mirror backup failed for set {setId}");
                    lock (lockObj) failedSets.Add(setId);
                }
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

    }
}
