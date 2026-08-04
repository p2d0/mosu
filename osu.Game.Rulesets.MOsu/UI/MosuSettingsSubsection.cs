// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions; // For Popover extensions
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Graphics.Sprites;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.UI;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osuTK;
using osu.Game.Graphics.Sprites;
using System.Linq.Expressions;
using Realms;
using osu.Game.Screens; // Required for IPerformFromScreenRunner
using System.Threading.Tasks;
using osu.Game.Screens.Import;
using osu.Game.Screens.Utility;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Online.API;
using osu.Framework.Testing;
using osu.Game.Rulesets.MOsu.Extensions;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Collections;
using osu.Game.Scoring;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;
using osu.Framework.Screens;
using osu.Game.Models;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class MosuSettingsSubsection : RulesetSettingsSubsection
    {
        private Ruleset ruleset;

        protected override LocalisableString Header => "mosu!";

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IPerformFromScreenRunner? performer { get; set; }

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.MOsu.Database.BackgroundCollectionImportProcessor? collectionImportProcessor { get; set; }

        private readonly Bindable<bool> exportWithScores = new Bindable<bool>(false);

        public MosuSettingsSubsection(Ruleset ruleset)
            : base(ruleset)
        {
            this.ruleset = ruleset;
        }
        // private ToolbarLocalUserButton? localUserButton;

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api, OsuGame game,GameHost host)
        {
            // LocalUserManager localUserManager;
            // host.Dependencies.Cache(localUserManager = new LocalUserManager((MosuRuleset)ruleset, realm, api));

            // game.GetToolbarContainer().Add(new ToolbarLocalUserButton());

            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = "Presets",
                    Margin = new MarginPadding { Left = 15 },
                    Font = OsuFont.GetFont(weight: FontWeight.Bold)
                },
                new SettingsButtonV2
                {
                    Text = "Export presets to file",
                    TooltipText = "Saves all mosu presets to exports/osu_mod_presets.json",
                    Action = exportPresets
                },
                new ImportPresetButton(),
                new SettingsButtonV2
                {
                    Text = "Import presets from file",
                    TooltipText = "Select a .json file from your computer",
                    Action = () =>
                    {
                        performer?.PerformFromScreen(screen => screen.Push(new ModPresetFileImportScreen()));
                    }
                },
                new OsuSpriteText
                {
                    Text = "Collections",
                    Margin = new MarginPadding { Left = 15 },
                    Font = OsuFont.GetFont(weight: FontWeight.Bold)
                },
                new SettingsCheckbox
                {
                    LabelText = "Include local scores in export",
                    Current = exportWithScores,
                    TooltipText = "If checked, exporting collections will also include local scores for the beatmaps in those collections."
                },
                new SettingsButtonV2
                {
                    Text = "Export collections to file",
                    TooltipText = "Saves all collections (and optionally scores) to exports/collections.json",
                    Action = exportCollections
                },
                new SettingsButtonV2
                {
                    Text = "Import collections from file",
                    TooltipText = "Open file browser to select a collection .json (Standard format). Scores are imported automatically if the file contains them.",
                    Action = () =>
                    {
                        performer?.PerformFromScreen(screen => screen.Push(new CollectionImportScreen()));
                    }
                },
                new ImportCollectionsFromClipboardButton(),
                new SettingsButtonV2
                {
                    Text = "Load example collections",
                    TooltipText = "Import example collections from embedded JSON file",
                    Action = () => collectionImportProcessor?.ImportExampleCollections()
                },
            };
        }

        private void exportPresets()
        {
            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Exporting presets...",
                CompletionText = "Presets exported!",
            };
            notifications.Post(notification);

            Task.Run(() =>
            {
                try
                {
                    notification.Text = "Fetching presets...";
                    var transferObjects = realm.Run(r => r.All<ModPreset>()
                        .Filter("Ruleset.ShortName == $0 && DeletePending == false", MosuRuleset.SHORT_NAME)
                        .ToList()
                        .Select(p => new ModPresetTransferObject
                        {
                            Name = p.Name,
                            Description = p.Description,
                            ModsJson = p.ModsJson
                        })
                        .ToList());

                    if (transferObjects.Count == 0)
                    {
                        notification.Text = "No mosu presets found to export.";
                        notification.State = ProgressNotificationState.Cancelled;
                        return;
                    }

                    notification.Text = $"Serializing {transferObjects.Count} presets...";
                    notification.Progress = 0.5f;

                    string json = JsonConvert.SerializeObject(transferObjects, Formatting.Indented);

                    var exportStorage = storage.GetStorageForDirectory("exports");
                    const string filename = "osu_mod_presets.json";

                    notification.Text = "Writing file...";
                    notification.Progress = 0.9f;

                    using (var stream = exportStorage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(json);
                    }

                    notification.CompletionText = $"Exported {transferObjects.Count} presets to {filename}!";
                    notification.State = ProgressNotificationState.Completed;
                    exportStorage.PresentFileExternally(filename);
                }
                catch (Exception ex)
                {
                    notification.State = ProgressNotificationState.Cancelled;
                    Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Export failed: {ex.Message}" }));
                }
            });
        }

        private void exportCollections()
        {
            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Exporting collections...",
                CompletionText = "Collections exported!",
            };
            notifications.Post(notification);

            bool includeScores = exportWithScores.Value;

            Task.Run(() =>
            {
                try
                {
                    var exportStorage = storage.GetStorageForDirectory("exports");
                    string filename = "collections.json";
                    string json;
                    int count = 0;

                    if (includeScores)
                    {
                        filename = "collections_with_scores.json";
                        notification.Text = "Fetching collections and scores...";
                        var collectionObjects = new List<CollectionTransferObject>();

                        realm.Run(r =>
                        {
                            var collections = r.All<BeatmapCollection>().Detach().ToList();
                            int total = collections.Count;
                            int current = 0;

                            foreach (var c in collections)
                            {
                                if (notification.State == ProgressNotificationState.Cancelled) return;

                                var dto = new CollectionTransferObject
                                {
                                    Name = c.Name,
                                    Beatmaps = new List<CollectionBeatmapEntry>()
                                };

                                foreach (var hash in c.BeatmapMD5Hashes)
                                {
                                    var beatmap = r.All<BeatmapInfo>().Filter("MD5Hash == $0", hash).FirstOrDefault();
                                    if (beatmap == null) continue;

                                    var entry = new CollectionBeatmapEntry
                                    {
                                        BeatmapSetId = beatmap.BeatmapSet!.OnlineID,
                                        BeatmapMD5Hash = hash,
                                        BeatmapTitle = beatmap.Metadata.Title,
                                        BeatmapAuthor = beatmap.Metadata.Artist,
                                        Scores = new List<ScoreExportDto>()
                                    };

                                    var scores = r.All<ScoreInfo>()
                                        .Filter("BeatmapInfo.MD5Hash == $0 && DeletePending == false", hash)
                                        .Detach()
                                        .ToList();

                                    foreach (var s in scores)
                                    {
                                        entry.Scores.Add(new ScoreExportDto
                                        {
                                            BeatmapHash = s.BeatmapInfo!.MD5Hash,
                                            RulesetShortName = s.Ruleset.ShortName,
                                            BeatmapDifficultyName = s.BeatmapInfo.DifficultyName,
                                            TotalScore = s.TotalScore,
                                            Accuracy = s.Accuracy,
                                            MaxCombo = s.MaxCombo,
                                            Rank = s.Rank.ToString(),
                                            Date = s.Date,
                                            Mods = s.Mods.Select(m => new APIMod(m)).ToList(),
                                            Statistics = s.Statistics.ToDictionary(k => k.Key.ToString(), v => v.Value)
                                        });
                                    }

                                    dto.Beatmaps.Add(entry);
                                }

                                collectionObjects.Add(dto);
                                current++;

                                notification.Text = $"Processed {current}/{total} collections...";
                                notification.Progress = (float)current / total;
                            }
                        });

                        notification.Text = "Serializing data...";
                        json = JsonConvert.SerializeObject(collectionObjects, Formatting.Indented);
                        count = collectionObjects.Count;
                    }
                    else
                    {
                        notification.Text = "Fetching collections...";
                        var collectionObjects = new List<CollectionTransferObject>();

                        realm.Run(r =>
                        {
                            var collections = r.All<BeatmapCollection>().Detach().ToList();

                            foreach (var c in collections)
                            {
                                var dto = new CollectionTransferObject
                                {
                                    Name = c.Name,
                                    Beatmaps = new List<CollectionBeatmapEntry>()
                                };

                                foreach (var hash in c.BeatmapMD5Hashes)
                                {
                                    var beatmap = r.All<BeatmapInfo>().Filter("MD5Hash == $0", hash).FirstOrDefault();
                                    if (beatmap == null) continue;

                                    dto.Beatmaps.Add(new CollectionBeatmapEntry
                                    {
                                        BeatmapSetId = beatmap.BeatmapSet!.OnlineID,
                                        BeatmapMD5Hash = hash,
                                        BeatmapTitle = beatmap.Metadata.Title,
                                        BeatmapAuthor = beatmap.Metadata.Artist,
                                        Scores = new List<ScoreExportDto>()
                                    });
                                }

                                collectionObjects.Add(dto);
                            }
                        });

                        if (collectionObjects.Count == 0)
                        {
                            notification.Text = "No collections found to export.";
                            notification.State = ProgressNotificationState.Cancelled;
                            return;
                        }

                        notification.Text = "Serializing data...";
                        json = JsonConvert.SerializeObject(collectionObjects, Formatting.Indented);
                        count = collectionObjects.Count;
                    }

                    notification.Text = "Writing file...";
                    using (var stream = exportStorage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(json);
                    }

                    notification.CompletionText = $"Exported {count} collections to {filename}!";
                    notification.State = ProgressNotificationState.Completed;
                    exportStorage.PresentFileExternally(filename);
                }
                catch (Exception ex)
                {
                    notification.State = ProgressNotificationState.Cancelled;
                    Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Export failed: {ex.Message}" }));
                }
            });
        }

    }

    public partial class ImportCollectionsFromClipboardButton : SettingsButtonV2
    {
        [Resolved]
        private Clipboard clipboard { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Import collections from clipboard";
            TooltipText = "Imports collection JSON from the clipboard. Scores are imported automatically if the file contains them.";
            Action = importFromClipboard;
        }

        private void importFromClipboard()
        {
            string? json = clipboard.GetText();

            if (string.IsNullOrWhiteSpace(json))
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Clipboard is empty." });
                return;
            }

            var processor = new CollectionImportProcessor(realm, notifications, api, beatmapManager, action => Schedule(action));
            _ = processor.Import(json);
        }
    }

    public partial class ImportPresetButton : SettingsButtonV2
    {
        [Resolved]
        private Clipboard clipboard { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Import presets from clipboard";
            TooltipText = "Imports preset JSON from the clipboard";
            Action = importFromClipboard;
        }

        private void importFromClipboard()
        {
            string? json = clipboard.GetText();

            if (string.IsNullOrWhiteSpace(json))
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Clipboard is empty." });
                return;
            }

            var processor = new ModPresetImportProcessor(realm, notifications, action => Schedule(action));
            processor.Import(json);
        }
    }
}

