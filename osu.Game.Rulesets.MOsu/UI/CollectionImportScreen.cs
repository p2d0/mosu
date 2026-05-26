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
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class CollectionImportScreen : OsuScreen
    {
        public override bool HideOverlaysOnEnter => true;
        public override bool DisallowExternalBeatmapRulesetChanges => true;

        private OsuFileSelector fileSelector;
        private Container contentContainer;
        private TextFlowContainer currentFileText;
        private RoundedButton importButton;

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
        private BeatmapModelDownloader downloader { get; set; } = null!;

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
                                Text = "Import & Download",
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

        private void importFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            importButton.Enabled.Value = false;
            currentFileText.Text = "Reading file...";

            // Run the initial DB import in a task to prevent UI freeze during JSON parsing
            Task.Run(() =>
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var collections = JsonConvert.DeserializeObject<List<CollectionTransferObject>>(json);

                    if (collections == null || collections.Count == 0)
                    {
                        Schedule(() =>
                        {
                            notifications?.Post(new SimpleErrorNotification { Text = "No collections found in file." });
                            importButton.Enabled.Value = true;
                            currentFileText.Text = "Import failed.";
                        });
                        return;
                    }

                    HashSet<string> allImportedHashes = new HashSet<string>();
                    int importedCount = 0;

                    // 1. Synchronously update Realm (Must be done on a thread safe for Realm, Task.Run with new context is fine)
                    realm.Write(r =>
                    {
                        foreach (var c in collections)
                        {
                            var existing = r.All<BeatmapCollection>().FirstOrDefault(bc => bc.Name == c.Name);

                            if (existing == null)
                            {
                                existing = new BeatmapCollection(c.Name);
                                r.Add(existing);
                                importedCount++;
                            }

                            foreach (var hash in c.BeatmapMD5Hashes)
                            {
                                if (!existing.BeatmapMD5Hashes.Contains(hash))
                                    existing.BeatmapMD5Hashes.Add(hash);

                                allImportedHashes.Add(hash);
                            }
                        }
                    });

                    // 2. Identify missing maps immediately
                    var missingHashes = realm.Run(r =>
                    {
                        var localBeatmaps = r.All<BeatmapInfo>()
                            .Filter("BeatmapSet.DeletePending == false")
                            .ToList(); // Materialize to memory

                        var localHashSet = new HashSet<string>(localBeatmaps.Select(b => b.MD5Hash));
                        return allImportedHashes.Where(h => !localHashSet.Contains(h)).ToList();
                    });

                    Schedule(() =>
                    {
                        notifications?.Post(new SimpleNotification
                        {
                            Text = $"Imported {importedCount} collections."
                        });

                        // 3. Start Background Process with Progress Notification
                        if (missingHashes.Count > 0)
                        {
                            startBackgroundDownload(missingHashes);
                        }

                        // 4. Close the screen immediately so user can do other things
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
            // Create the notification
            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Starting collection download...",
                CompletionText = "Missing collection maps have been queued.",
            };

            notifications.Post(notification);

            // Fire and forget task
            Task.Factory.StartNew(() =>
            {
                int processedCount = 0;
                int failedCount = 0;
                int queuedCount = 0;
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
                                queuedCount++;

                                if (downloader.GetExistingDownload(onlineSet) == null)
                                    downloader.Download(onlineSet);
                            }
                        }
                        else
                        {
                            failedCount++;
                        }

                        // Rate limiting
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
