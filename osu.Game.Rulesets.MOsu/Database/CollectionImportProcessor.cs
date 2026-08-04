using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    /// <summary>
    /// Shared collection import pipeline: parses collection JSON, imports collections to realm,
    /// downloads missing beatmap sets (with mirror fallback), and optionally imports scores.
    /// The only caller-specific concern is where the JSON comes from.
    /// </summary>
    public class CollectionImportProcessor
    {
        private readonly RealmAccess realm;
        private readonly INotificationOverlay notifications;
        private readonly IAPIProvider api;
        private readonly BeatmapManager beatmapManager;
        private readonly Action<Action> schedule;

        public CollectionImportProcessor(RealmAccess realm, INotificationOverlay notifications, IAPIProvider api, BeatmapManager beatmapManager, Action<Action> schedule)
        {
            this.realm = realm;
            this.notifications = notifications;
            this.api = api;
            this.beatmapManager = beatmapManager;
            this.schedule = schedule;
        }

        /// <summary>
        /// Imports collections from JSON: writes collections, downloads missing beatmap sets
        /// (with mirror fallback), and imports scores when the JSON contains any.
        /// <paramref name="onCollectionsImported"/> fires once collections are written (before any downloads).
        /// Never throws — errors are posted as notifications.
        /// </summary>
        public async Task Import(string json, Action? onCollectionsImported = null)
        {
            try
            {
                var transferObjects = JsonConvert.DeserializeObject<List<CollectionTransferObject>>(json);

                if (transferObjects == null || transferObjects.Count == 0)
                {
                    schedule(() => notifications.Post(new SimpleErrorNotification { Text = "No collections found in file." }));
                    return;
                }

                // Import scores only if the JSON actually contains any.
                bool importScores = transferObjects.Any(c => c.Beatmaps.Any(b => b.Scores.Count > 0));

                // Step 1: Import collections
                var (allSetIds, importedCollections) = importCollections(transferObjects);

                schedule(() =>
                {
                    onCollectionsImported?.Invoke();
                    notifications.Post(new SimpleNotification
                    {
                        Text = $"Imported {importedCollections} collections."
                    });
                });

                // Step 2: Download missing beatmap sets
                var missingSetIds = CollectionSetDownloader.GetMissingSetIds(realm, allSetIds);

                if (missingSetIds.Count > 0)
                {
                    if (!api.IsLoggedIn)
                    {
                        schedule(() => notifications.Post(new SimpleErrorNotification { Text = "Cannot download maps: not logged in." }));
                        return;
                    }

                    var notification = new ProgressNotification
                    {
                        State = ProgressNotificationState.Active,
                        Text = "Downloading collection maps...",
                    };
                    notifications.Post(notification);

                    var downloader = new CollectionSetDownloader(api, beatmapManager, notifications, realm, schedule);
                    await downloader.DownloadSequential(
                        missingSetIds,
                        notification,
                        getTitle: setId => transferObjects.SelectMany(c => c.Beatmaps).FirstOrDefault(b => b.BeatmapSetId == setId)?.BeatmapTitle ?? $"Set {setId}");
                }

                // Step 3: Import scores last so freshly downloaded sets are included
                if (importScores)
                {
                    int importedScores = importCollectionScores(transferObjects);
                    schedule(() => notifications.Post(new SimpleNotification { Text = $"Imported {importedScores} scores." }));
                }
            }
            catch (JsonException)
            {
                // malformed or wrong-shape user-provided JSON is an expected input error, not a code bug — no stack trace noise.
                schedule(() => notifications.Post(new SimpleErrorNotification { Text = "Invalid file: expected a JSON array of collections." }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to import collections.");
                schedule(() => notifications.Post(new SimpleErrorNotification { Text = $"Failed to import collections: {ex.Message}" }));
            }
        }

        private (HashSet<int> setIds, int count) importCollections(List<CollectionTransferObject> transferObjects)
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

        private int importCollectionScores(List<CollectionTransferObject> transferObjects)
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

                            // Restore the player from the exported user fields, falling back to legacy CustomName.
                            score.User = new APIUser
                            {
                                Id = sDto.UserOnlineId > 0 ? sDto.UserOnlineId : -123,
                                Username = !string.IsNullOrEmpty(sDto.UserUsername)
                                    ? sDto.UserUsername
                                    : !string.IsNullOrEmpty(sDto.CustomName)
                                        ? sDto.CustomName
                                        : "Example mods configuration",
                            };

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
    }
}
