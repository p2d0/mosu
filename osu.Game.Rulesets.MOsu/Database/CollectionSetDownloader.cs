using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.MOsu.Database
{
    /// <summary>
    /// Shared beatmap-set resolution for collection imports: checks availability on osu!
    /// and falls back to the nekoha mirror for unavailable sets.
    /// </summary>
    public class CollectionSetDownloader
    {
        private readonly IAPIProvider api;
        private readonly BeatmapManager beatmapManager;
        private readonly INotificationOverlay notifications;
        private readonly Action<Action> schedule;

        public CollectionSetDownloader(IAPIProvider api, BeatmapManager beatmapManager, INotificationOverlay notifications, Action<Action> schedule)
        {
            this.api = api;
            this.beatmapManager = beatmapManager;
            this.notifications = notifications;
            this.schedule = schedule;
        }

        public bool IsLoggedIn => api.IsLoggedIn;

        /// <summary>
        /// Resolves a single set download: checks availability on osu! and either queues the official
        /// download (via <paramref name="localDownloader"/>) or falls back to the mirror backup.
        /// </summary>
        public async Task ResolveSet(int setId, BeatmapModelDownloader localDownloader, object syncLock, HashSet<int> failedSets)
        {
            var onlineSet = new APIBeatmapSet { OnlineID = setId };

            try
            {
                if (!await IsSetAvailable(setId))
                {
                    Logger.Log($"Set {setId} not found on osu!, using nekoha mirror backup...");
                    DownloadViaMirror(setId, syncLock, failedSets);
                }
                else if (localDownloader.GetExistingDownload(onlineSet) == null)
                {
                    localDownloader.Download(onlineSet);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Lookup failed for set {setId}, attempting normal download.");
                if (localDownloader.GetExistingDownload(onlineSet) == null)
                    localDownloader.Download(onlineSet);
            }
        }

        /// <summary>
        /// Checks whether a beatmap set still exists online (not deleted/unavailable) using the osu! API v2 lookup.
        /// </summary>
        private async Task<bool> IsSetAvailable(int setId)
        {
            var request = new GetBeatmapSetRequest(setId);
            var tcs = new TaskCompletionSource<bool>();

            request.Success += _ => tcs.TrySetResult(true);
            request.Failure += _ => tcs.TrySetResult(false);
            api.Queue(request);

            try
            {
                return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                return true; // unknown (e.g. network hiccup) -> attempt the normal download
            }
        }

        /// <summary>
        /// Downloads and imports a beatmap set from the nekoha mirror backup.
        /// Adds the set to <paramref name="failedSets"/> if the mirror download/import fails.
        /// </summary>
        public void DownloadViaMirror(int setId, object syncLock, HashSet<int> failedSets)
        {
            Logger.Log($"Download unavailable for set {setId}, trying nekoha mirror backup...");
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

                    schedule(() =>
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
                            schedule(() =>
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
                                    lock (syncLock) failedSets.Add(setId);
                                }
                            });
                        });
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Nekoha mirror backup failed for set {setId}");
                    lock (syncLock) failedSets.Add(setId);
                }
            }, TaskCreationOptions.LongRunning);
        }
    }
}
