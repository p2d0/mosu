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
    /// Per-set beatmap download primitives for MOsu imports: availability check, official download
    /// with mirror fallback, and waiting for a set to land in realm. Used by collection imports
    /// (sequential loop) and single-set downloads.
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
        /// Downloads a single set: official osu! download when available, mirror fallback otherwise.
        /// Returns true once the set is confirmed in the realm. Single-set entry point — sequential
        /// callers await each set in turn.
        /// </summary>
        /// <summary>
        /// Downloads a single set: official osu! download when available, mirror fallback otherwise.
        /// Returns true once the set is confirmed in the realm. Single-set entry point — sequential
        /// callers await each set in turn.
        /// </summary>
        public async Task<bool> DownloadSet(int setId)
        {
            await ResolveSet(setId);
            return await WaitForSetInRealm(setId);
        }

        /// <summary>
        /// Attempts a download of <paramref name="setId"/>: official osu! download when available,
        /// mirror fallback otherwise. Failures post their own notifications.
        /// </summary>
        private async Task ResolveSet(int setId)
        {
            var onlineSet = new APIBeatmapSet { OnlineID = setId };

            var localDownloader = new BeatmapModelDownloader(beatmapManager, api);

            localDownloader.DownloadFailed += req =>
            {
                // official download failed -> fall back to the mirror
                DownloadViaMirror(req.Model.OnlineID);
            };

            try
            {
                if (!await IsSetAvailable(setId))
                {
                    Logger.Log($"Set {setId} not found on osu!, using beatconnect mirror backup...");
                    DownloadViaMirror(setId);
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

        /// <summary>
        /// Waits until <paramref name="setId"/> appears in the realm, or returns false after a timeout
        /// (the mirror backup's HTTP budget — a dead mirror never lands a set).
        /// </summary>
        private async Task<bool> WaitForSetInRealm(int setId)
        {
            var tcs = new TaskCompletionSource<bool>();

            using (realm.RegisterForNotifications(
                       r => r.All<BeatmapSetInfo>().Where(s => s.OnlineID == setId && !s.DeletePending),
                       (items, _) =>
                       {
                           if (items.Any())
                               tcs.TrySetResult(true);
                       }))
                  {
                try
                {
                    // the initial notification fires immediately, covering sets already present
                    return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
                }
                catch (TimeoutException)
                {
                    return false;
                }
            }
        }

        private void DownloadViaMirror(int setId)
        {
            Logger.Log($"Download unavailable for set {setId}, trying beatconnect mirror backup...");
            Task.Factory.StartNew(() =>
            {
                try
                {
                    var response = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(60)
                    }.GetAsync($"https://beatconnect.io/b/{setId}/").Result;

                    string filename = $"beatconnect_{setId}.osz";
                    string path = Path.Combine(Path.GetTempPath(), filename);

                    byte[] data = response.Content.ReadAsByteArrayAsync().Result;
                    Logger.Log($"Beatconnect mirror response: status={response.StatusCode}, content-type={response.Content.Headers.ContentType?.MediaType}, size={data.Length} bytes");
                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"Beatconnect mirror returned {response.StatusCode}");
                    File.WriteAllBytes(path, data);

                    schedule(() =>
                    {
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
                                    Logger.Log($"Imported set {setId} from beatconnect mirror backup");
                                }
                                else
                                {
                                    Logger.Error(new Exception($"Beatconnect mirror import returned 0 items for set {setId}. File size: {fileSize} bytes."), "Beatconnect mirror import empty");
                                    notifications.Post(new SimpleErrorNotification { Text = $"Beatconnect mirror import failed for set {setId}" });
                                }
                            });
                        });
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Beatconnect mirror backup failed for set {setId}");
                }
            }, TaskCreationOptions.LongRunning);
        }
    }
}
