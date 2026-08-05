using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        /// (with mirror fallback), and imports scores when the JSON contains any — each set's scores
        /// land as soon as that set is downloaded, plus a final pass for beatmaps already present locally.
        /// <paramref name="onCollectionsImported"/> fires once collections are written (before any downloads).
        /// Never throws — errors are posted as notifications.
        /// </summary>
        public async Task Import(string json, Action? onCollectionsImported = null)
        {
            try
            {
                // Reject structurally malformed files up front (e.g. a beatmap object pasted inside
                // another beatmap's Scores array) so the user gets a clear error instead of a
                // silently-missing beatmap that never reaches the download queue.
                validateJsonStructure(json);

                var transferObjects = JsonConvert.DeserializeObject<List<CollectionTransferObject>>(json);

                if (transferObjects == null || transferObjects.Count == 0)
                {
                    schedule(() => notifications.Post(new SimpleErrorNotification { Text = "No collections found in file." }));
                    return;
                }

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

                int importedScores = 0;

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

                    importedScores = await DownloadSequential(
                        missingSetIds,
                        notification,
                        transferObjects);
                }

                // Step 3: Final pass covers beatmaps that already existed locally (never downloaded);
                // per-set imports from Step 2 are skipped by the duplicate check.
                importedScores += importCollectionScores(transferObjects);

                if (importedScores > 0)
                    schedule(() => notifications.Post(new SimpleNotification { Text = $"Imported {importedScores} scores." }));
            }
            catch (CollectionJsonStructureException ex)
            {
                schedule(() => notifications.Post(new SimpleErrorNotification { Text = $"Invalid file: {ex.Message}" }));
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

        /// <summary>
        /// Downloads <paramref name="setIds"/> one at a time (sequential), importing scores per set
        /// as each one lands in the realm. Returns the number of scores imported.
        /// </summary>
        private async Task<int> DownloadSequential(
            List<int> setIds,
            ProgressNotification notification,
            List<CollectionTransferObject> transferObjects)
        {
            var downloader = new CollectionSetDownloader(api, beatmapManager, notifications, realm, schedule);
            int importedScores = 0;
            int unavailable = 0;

            for (int i = 0; i < setIds.Count; i++)
            {
                if (notification.State == ProgressNotificationState.Cancelled) break;

                int setId = setIds[i];
                string title = transferObjects.SelectMany(c => c.Beatmaps).FirstOrDefault(b => b.BeatmapSetId == setId)?.BeatmapTitle ?? $"Set {setId}";

                schedule(() =>
                {
                    notification.Text = $"Downloading \"{title}\" ({i + 1}/{setIds.Count})...";
                    notification.Progress = (float)i / setIds.Count;
                });

                if (!await downloader.DownloadSet(setId))
                {
                    unavailable++;
                    continue;
                }

                importedScores += importCollectionScores(transferObjects, setId);

                schedule(() =>
                {
                    notification.Text = $"Downloaded \"{title}\" ({i + 1}/{setIds.Count})...";
                    notification.Progress = (float)(i + 1) / setIds.Count;
                });
            }

            schedule(() =>
            {
                notification.Text = $"Downloaded {setIds.Count - unavailable} maps.";
                if (unavailable > 0)
                    notification.Text += $" ({unavailable} unavailable)";
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });

            return importedScores;
        }

        /// <summary>
        /// Rejects structurally invalid collection JSON: beatmap entries nested inside another
        /// beatmap's <c>Scores</c> array would be invisible to the import (never queued for
        /// download, never scored), and score entries missing required fields would import garbage
        /// — fail loudly instead.
        /// </summary>
        private static void validateJsonStructure(string json)
        {
            var root = JArray.Parse(json);

            for (int ci = 0; ci < root.Count; ci++)
            {
                var collection = root[ci];

                string collectionName = (string?)collection["Name"] ?? $"#{ci + 1}";

                if (collection["Beatmaps"] is not JArray beatmaps)
                    throw new CollectionJsonStructureException($"Collection \"{collectionName}\" is missing a \"Beatmaps\" array.");

                foreach (var beatmap in beatmaps)
                {
                    int? setId = (int?)beatmap["BeatmapSetId"];

                    if (setId == null)
                        throw new CollectionJsonStructureException($"Collection \"{collectionName}\" contains a beatmap entry without a \"BeatmapSetId\".");

                    if (beatmap["BeatmapMD5Hash"] is not JValue { Type: JTokenType.String } md5 || string.IsNullOrEmpty((string?)md5))
                        throw new CollectionJsonStructureException($"Beatmap (set {setId}) is missing a \"BeatmapMD5Hash\".");

                    if (beatmap["Scores"] is not JArray scores) continue;

                    foreach (var score in scores)
                    {
                        // a score entry carrying beatmap-level keys is a beatmap object in the wrong array
                        foreach (var nestedKey in new[] { "BeatmapSetId", "BeatmapMD5Hash", "BeatmapTitle", "BeatmapAuthor", "Scores" })
                        {
                            if (score[nestedKey] != null)
                                throw new CollectionJsonStructureException($"Beatmap (set {score["BeatmapSetId"]?.Value<string>() ?? score["BeatmapMD5Hash"]?.Value<string>()}) is nested inside the Scores array of set {setId}. Move it to the top-level \"Beatmaps\" array.");
                        }

                        // scores missing these fields would import as garbage or silently skip
                        foreach (var required in new[] { "BeatmapHash", "RulesetShortName", "TotalScore", "Date" })
                        {
                            if (score[required] == null)
                                throw new CollectionJsonStructureException($"Score in set {setId} is missing \"{required}\".");
                        }
                    }
                }
            }
        }

        private class CollectionJsonStructureException : Exception
        {
            public CollectionJsonStructureException(string message)
                : base(message)
            {
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

        private int importCollectionScores(List<CollectionTransferObject> transferObjects, int? setId = null)
        {
            int importedScores = 0;

            realm.Write(r =>
            {
                foreach (var dto in transferObjects)
                {
                    foreach (var beatmapEntry in dto.Beatmaps.Where(b => setId == null || b.BeatmapSetId == setId))
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
