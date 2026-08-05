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
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    /// <summary>
    /// Shared sequential download of unavailable beatmap sets for collection imports:
    /// checks availability on osu!, routes unavailable maps to the nekoha mirror backup,
    /// and waits for each set to land in realm before moving on.
    /// </summary>
    public class CollectionSetDownloader
    {
        private readonly IAPIProvider api;
        private readonly BeatmapManager beatmapManager;
        private readonly INotificationOverlay notifications;
        private readonly RealmAccess realm;
        private readonly Action<Action> schedule;

        public CollectionSetDownloader(IAPIProvider api, BeatmapManager beatmapManager, INotificationOverlay notifications, RealmAccess realm, Action<Action> schedule)
        {
            this.api = api;
            this.beatmapManager = beatmapManager;
            this.notifications = notifications;
            this.realm = realm;
            this.schedule = schedule;
        }

        /// <summary>
        /// Filters beatmap set ids down to those not present (and not pending deletion) in the local realm.
        /// </summary>
        public static List<int> GetMissingSetIds(RealmAccess realm, IEnumerable<int> setIds)
        {
            return setIds.Where(id =>
            {
                var existing = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false && OnlineID == $0", id).FirstOrDefault());
                return existing == null;
            }).ToList();
        }

        /// <summary>
        /// Downloads <paramref name="setIds"/> one at a time (sequential), routing unavailable maps
        /// straight to the mirror backup. Returns the number of sets successfully downloaded.
        /// <paramref name="onSetDownloaded"/> fires for each set once it is confirmed in the realm
        /// (after the download completes).
        /// </summary>
        public async Task<int> DownloadSequential(
            List<int> setIds,
            ProgressNotification notification,
            Func<int, string>? getTitle = null,
            Action<int>? onSetDownloaded = null)
        {
            var localDownloader = new BeatmapModelDownloader(beatmapManager, api);
            var failedSets = new HashSet<int>();
            var syncLock = new object();
            int downloaded = 0;

            localDownloader.DownloadFailed += req =>
            {
                // official download failed -> fall back to the mirror
                DownloadViaMirror(req.Model.OnlineID, notification, syncLock, failedSets);
            };

            for (int i = 0; i < setIds.Count; i++)
            {
                if (notification.State == ProgressNotificationState.Cancelled) break;

                int setId = setIds[i];
                string title = getTitle?.Invoke(setId) ?? $"Set {setId}";

                schedule(() =>
                {
                    notification.Text = $"Downloading \"{title}\" ({i + 1}/{setIds.Count})...";
                    notification.Progress = (float)i / setIds.Count;
                });

                await ResolveSet(setId, localDownloader, notification, syncLock, failedSets);
                await WaitForSetInRealm(setId);

                if (failedSets.Contains(setId)) continue;
                downloaded++;

                onSetDownloaded?.Invoke(setId);

                schedule(() =>
                {
                    notification.Text = $"Downloaded \"{title}\" ({i + 1}/{setIds.Count})...";
                    notification.Progress = (float)(i + 1) / setIds.Count;
                });
            }

            schedule(() =>
            {
                notification.Text = $"Downloaded {downloaded} maps.";
                if (failedSets.Count > 0)
                    notification.Text += $" ({failedSets.Count} unavailable)";
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            });

            return downloaded;
        }

        private async Task ResolveSet(int setId, BeatmapModelDownloader localDownloader, ProgressNotification notification, object syncLock, HashSet<int> failedSets)
        {
            var onlineSet = new APIBeatmapSet { OnlineID = setId };

            try
            {
                if (!await IsSetAvailable(setId))
                {
                    Logger.Log($"Set {setId} not found on osu!, using nekoha mirror backup...");
                    DownloadViaMirror(setId, notification, syncLock, failedSets);
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
        /// Checks whether a beatmap set is available for download: <c>beatmapsets/{id}</c> 404s for
        /// deleted sets, and <c>availability.download_disabled</c> flags sets that exist but can't be
        /// downloaded ("This beatmap is currently not available for download.").
        /// </summary>
        private async Task<bool> IsSetAvailable(int setId)
        {
            var request = new GetBeatmapSetRequest(setId);
            var tcs = new TaskCompletionSource<bool>();

            request.Success += set => tcs.TrySetResult(!set.Availability.DownloadDisabled);
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

        private async Task WaitForSetInRealm(int setId)
        {
            var tcs = new TaskCompletionSource<bool>();
            IDisposable? subscription = null;

            subscription = realm.RegisterForNotifications(
                r => r.All<BeatmapSetInfo>().Where(s => !s.DeletePending),
                (sender, _) =>
                {
                    if (sender.ToList().Any(s => s.OnlineID == setId))
                    {
                        subscription?.Dispose();
                        tcs.TrySetResult(true);
                    }
                });

            // Check if already present
            var existing = realm.Run(r => r.All<BeatmapSetInfo>().Filter("DeletePending == false && OnlineID == $0", setId).FirstOrDefault());
            if (existing != null)
            {
                subscription?.Dispose();
                tcs.TrySetResult(true);
            }

            await tcs.Task.WaitAsync(TimeSpan.FromMinutes(2));
            subscription?.Dispose();
        }

        private void DownloadViaMirror(int setId, ProgressNotification notification, object syncLock, HashSet<int> failedSets)
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
                        notification.Text = $"Importing set {setId} from nekoha mirror...";

                        // throwaway notification so the shared progress notification isn't mutated/completed by the importer
                        var importNotification = new ProgressNotification();
                        Task.Run(async () =>
                        {
                            long fileSize = new FileInfo(path).Length;
                            var result = await beatmapManager.Import(importNotification, new[] { new ImportTask(path) });
                            File.Delete(path);
                            schedule(() =>
                            {
                                if (result.Any())
                                {
                                    notification.Text = $"Imported set {setId} from nekoha mirror backup";
                                }
                                else
                                {
                                    Logger.Error(new Exception($"Nekoha mirror import returned 0 items for set {setId}. File size: {fileSize} bytes."), "Nekoha mirror import empty");
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
