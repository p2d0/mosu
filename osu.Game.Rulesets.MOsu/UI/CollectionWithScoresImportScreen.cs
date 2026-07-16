using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Models;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class CollectionWithScoresImportScreen : OsuScreen
    {
        public override bool HideOverlaysOnEnter => true;
        public override bool DisallowExternalBeatmapRulesetChanges => true;

        private OsuFileSelector fileSelector = null!;
        private Container contentContainer = null!;
        private TextFlowContainer currentFileText = null!;
        private RoundedButton importButton = null!;

        private const float duration = 300;
        private const float button_height = 50;
        private const float button_vertical_margin = 15;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = contentContainer = new Container
            {
                Masking = true,
                CornerRadius = 10,
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(0.9f, 0.8f),
                Children = new Drawable[]
                {
                    fileSelector = new OsuFileSelector(validFileExtensions: new[] { ".json" })
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0.65f
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0.35f,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Colour = colourProvider.Background4,
                                RelativeSizeAxes = Axes.Both
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Bottom = button_height + button_vertical_margin * 3 },
                                Child = new OsuScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Child = currentFileText = new TextFlowContainer(t => t.Font = OsuFont.Default.With(size: 30))
                                    {
                                        AutoSizeAxes = Axes.Y,
                                        RelativeSizeAxes = Axes.X,
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        TextAnchor = Anchor.Centre,
                                        Padding = new MarginPadding(20)
                                    },
                                    ScrollContent =
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                    }
                                },
                            },
                            importButton = new RoundedButton
                            {
                                Text = "Import Collections & Scores",
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                                RelativeSizeAxes = Axes.X,
                                Height = button_height,
                                Width = 0.9f,
                                Margin = new MarginPadding { Bottom = button_vertical_margin },
                                Action = () => importFile(fileSelector.CurrentFile.Value?.FullName),
                                Enabled = { Value = false }
                            }
                        }
                    }
                }
            };

            fileSelector.CurrentFile.BindValueChanged(fileChanged, true);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);
            contentContainer.ScaleTo(0.95f).ScaleTo(1, duration, Easing.OutQuint);
            this.FadeInFromZero(duration);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            contentContainer.ScaleTo(0.95f, duration, Easing.OutQuint);
            this.FadeOut(duration, Easing.OutQuint);
            return base.OnExiting(e);
        }

        private void fileChanged(ValueChangedEvent<FileInfo> selectedFile)
        {
            importButton.Enabled.Value = selectedFile.NewValue != null;
            currentFileText.Text = selectedFile.NewValue?.Name ?? "Select a .json file";
        }

        private void importFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            importButton.Enabled.Value = false;
            currentFileText.Text = "Reading and importing...";

            Task.Run(() =>
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var transferObjects = JsonConvert.DeserializeObject<List<CollectionWithScoresTransferObject>>(json);

                    if (transferObjects == null || transferObjects.Count == 0)
                    {
                        Schedule(() =>
                        {
                            notifications?.Post(new SimpleErrorNotification { Text = "No valid data found in file." });
                            importButton.Enabled.Value = true;
                            currentFileText.Text = "Import failed.";
                        });
                        return;
                    }

                    HashSet<string> allImportedHashes = new HashSet<string>();
                    int importedCollections = 0;
                    int importedScores = 0;

                    realm.Write(r =>
                    {
                        // We do not lookup RealmUser because it is an EmbeddedObject.
                        // Instead, we assign the current API User to each new score, which creates a new embedded RealmUser.

                        foreach (var dto in transferObjects)
                        {
                            // 1. Collections
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
                            }

                            // 2. Scores
                            foreach (var beatmapEntry in dto.Beatmaps)
                            foreach (var sDto in beatmapEntry.Scores)
                            {
                                var beatmap = r.All<BeatmapInfo>().FirstOrDefault(b => b.MD5Hash == sDto.BeatmapHash);
                                var rulesetInfo = r.All<RulesetInfo>().FirstOrDefault(ru => ru.ShortName == sDto.RulesetShortName);

                                // If map missing, we skip score import for now (map needs to exist for ScoreInfo)
                                if (beatmap == null || rulesetInfo == null) continue;

                                // Fix: Use .Filter() instead of LINQ for nested property check (BeatmapInfo.MD5Hash)
                                // to avoid Realm LINQ provider limitations.
                                bool scoreExists = r.All<ScoreInfo>()
                                    .Filter("BeatmapInfo.MD5Hash == $0 && TotalScore == $1 && Date == $2",
                                        sDto.BeatmapHash, sDto.TotalScore, sDto.Date)
                                    .Count() > 0;

                                if (scoreExists) continue;

                                var rulesetInstance = rulesetInfo.CreateInstance();
                                var mods = sDto.Mods.Select(m => m.ToMod(rulesetInstance)).ToArray();

                                // Construct ScoreInfo.
                                // Note: We don't pass a RealmUser here. We set the User property afterwards to ensure
                                // the embedded RealmUser is created correctly for this specific score instance.
                                var score = new ScoreInfo(beatmap, rulesetInfo)
                                {
                                    TotalScore = sDto.TotalScore,
                                    Accuracy = sDto.Accuracy,
                                    MaxCombo = sDto.MaxCombo,
                                    Rank = Enum.TryParse<ScoreRank>(sDto.Rank, out var rank) ? rank : ScoreRank.F,
                                    Date = sDto.Date,
                                    Mods = mods,
                                };

                                // Assign the current API user. The setter in ScoreInfo will create a new embedded RealmUser.
                                score.User = new APIUser() { Username = string.IsNullOrEmpty(sDto.CustomName) ? "Imported" : sDto.CustomName, Id = -123 };

                                foreach (var stat in sDto.Statistics)
                                {
                                    if (Enum.TryParse<HitResult>(stat.Key, out var result))
                                        score.Statistics[result] = stat.Value;
                                }

                                // IMPORTANT: Manually serialize statistics to JSON for Realm persistence
                                score.StatisticsJson = JsonConvert.SerializeObject(score.Statistics);

                                r.Add(score);
                                importedScores++;
                            }
                        }
                    });

                    // Identify missing maps for download
                    var missingHashes = realm.Run(r =>
                    {
                        var localBeatmaps = r.All<BeatmapInfo>()
                            .Filter("BeatmapSet.DeletePending == false")
                            .ToList();
                        var localHashSet = new HashSet<string>(localBeatmaps.Select(b => b.MD5Hash));
                        return allImportedHashes.Where(h => !localHashSet.Contains(h)).ToList();
                    });

                    Schedule(() =>
                    {
                        notifications?.Post(new SimpleNotification
                        {
                            Text = $"Imported {importedCollections} collections and {importedScores} scores."
                        });

                        if (missingHashes.Count > 0)
                        {
                            if (!api.IsLoggedIn)
                            {
                                notifications?.Post(new SimpleErrorNotification { Text = "Cannot download maps: not logged in." });
                            }
                            else
                            {
                                startBackgroundDownload(missingHashes);
                            }
                        }

                        this.Exit();
                    });
                }
                catch (Exception ex)
                {
                    Schedule(() =>
                    {
                        notifications?.Post(new SimpleErrorNotification { Text = $"Import failed: {ex.Message}" });
                        importButton.Enabled.Value = true;
                    });
                }
            });
        }

        private void startBackgroundDownload(List<string> missingHashes)
        {
            if (!api.IsLoggedIn)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Cannot download maps: not logged in." });
                return;
            }

            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Resolving missing maps...",
            };

            notifications.Post(notification);

            // Local downloader — no PostNotification set, so no per-download notifications
            var localDownloader = new BeatmapModelDownloader(beatmapManager, api);
            var pendingSets = new HashSet<int>();
            var failedSets = new HashSet<int>();
            var lockObj = new object();

            localDownloader.DownloadFailed += req =>
            {
                lock (lockObj) failedSets.Add(req.Model.OnlineID);
            };

            // Resolve hashes to set IDs and queue downloads
            foreach (var hash in missingHashes)
            {
                try
                {
                    var req = new GetBeatmapRequest(new BeatmapInfo { MD5Hash = hash });
                    req.AttachAPI(api);
                    req.Perform();

                    var onlineSet = req.Response?.BeatmapSet;
                    if (onlineSet != null && !pendingSets.Contains(onlineSet.OnlineID))
                    {
                        pendingSets.Add(onlineSet.OnlineID);
                        if (localDownloader.GetExistingDownload(onlineSet) == null)
                            localDownloader.Download(onlineSet);
                    }
                    Thread.Sleep(100);
                }
                catch { }
            }

            if (pendingSets.Count == 0)
            {
                notification.State = ProgressNotificationState.Completed;
                return;
            }

            // Subscribe to all beatmap sets, filter client-side (Realm can't translate HashSet.Contains)
            IDisposable? realmSubscription = null;
            realmSubscription = realm.RegisterForNotifications(
                r => r.All<BeatmapSetInfo>().Where(s => !s.DeletePending),
                (sender, _) =>
                {
                    if (notification.State == ProgressNotificationState.Cancelled) return;

                    int importedCount = sender.ToList().Count(s => pendingSets.Contains(s.OnlineID));

                    lock (lockObj)
                    {
                        int resolved = importedCount + failedSets.Count;
                        float progress = (float)resolved / pendingSets.Count;

                        Schedule(() =>
                        {
                            notification.Text = $"Importing {importedCount}/{pendingSets.Count} maps...";
                            notification.Progress = progress;
                        });

                        if (resolved >= pendingSets.Count)
                        {
                            realmSubscription?.Dispose();
                            Schedule(() =>
                            {
                                notification.Text = $"Downloaded {importedCount} maps.";
                                if (failedSets.Count > 0)
                                    notification.Text += $" ({failedSets.Count} unavailable)";
                                notification.Progress = 1;
                                notification.State = ProgressNotificationState.Completed;
                            });
                        }
                    }
                });
        }
    }
}
