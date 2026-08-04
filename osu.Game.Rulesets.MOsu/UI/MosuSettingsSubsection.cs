// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.Mods;
using osuTK;
using osu.Game.Graphics.Sprites;
using System.Linq.Expressions;
using Newtonsoft.Json;
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
                    Text = "Imports",
                    Margin = new MarginPadding { Left = 15 },
                    Font = OsuFont.GetFont(weight: FontWeight.Bold)
                },
                new SettingsButtonV2
                {
                    Text = "Import from file",
                    TooltipText = "Select a .json file containing presets or collections (auto-detected)",
                    Action = () =>
                    {
                        performer?.PerformFromScreen(screen => screen.Push(new JsonImportScreen()));
                    }
                },
                new ImportFromClipboardButton(),
                new SettingsButtonV2
                {
                    Text = "Load examples",
                    TooltipText = "Import example collections from embedded JSON file",
                    Action = () => collectionImportProcessor?.ImportInBackground()
                },
                new OsuSpriteText
                {
                    Text = "Exports",
                    Margin = new MarginPadding { Left = 15 },
                    Font = OsuFont.GetFont(weight: FontWeight.Bold)
                },
                new ExportPresetsButton(),
                new ExportCollectionsButton(),
            };
        }


    }

    public partial class ExportPresetsButton : SettingsButtonV2, IHasPopover
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Export presets to file";
            TooltipText = "Choose which rulesets' presets to export";
            Action = () =>
            {
                // Toggle, like the collections export: clicking again hides instead of reopening.
                if (this.FindClosestParent<PopoverContainer>()?.CurrentTarget == this)
                    this.HidePopover();
                else
                    this.ShowPopover();
            };
        }

        public Popover GetPopover() => new ExportPresetsPopover();
    }

    public partial class ExportPresetsPopover : OsuPopover
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private readonly List<(RulesetInfo ruleset, Bindable<bool> selected)> entries = new List<(RulesetInfo, Bindable<bool>)>();

        private FillFlowContainer rulesetList = null!;

        public ExportPresetsPopover()
        {
            AutoSizeAxes = Axes.Both;
            Origin = Anchor.TopCentre;

            Child = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                Width = 360,
                Spacing = new Vector2(10f),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "Export presets",
                        Font = OsuFont.GetFont(weight: FontWeight.Bold),
                    },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Child = rulesetList = new FillFlowContainer
                        {
                            Direction = FillDirection.Vertical,
                            AutoSizeAxes = Axes.Y,
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(4f),
                        },
                    },
                    new RoundedButton
                    {
                        Text = "Export",
                        Height = 40,
                        RelativeSizeAxes = Axes.X,
                        Action = export,
                    },
                }
            };
        }

        protected override void PopIn()
        {
            base.PopIn();
            reloadRulesets();
        }

        private void reloadRulesets()
        {
            rulesetList.Clear();
            entries.Clear();

            realm.Run(r =>
            {
                foreach (var ruleset in r.All<RulesetInfo>().Detach()
                                         .Where(rs => r.All<ModPreset>().Any(p => p.Ruleset.ShortName == rs.ShortName && !p.DeletePending))
                                         .OrderBy(rs => rs.ShortName))
                    entries.Add((ruleset, new Bindable<bool>(true)));
            });

            foreach (var entry in entries)
            {
                rulesetList.Add(new OsuCheckbox
                {
                    LabelText = entry.ruleset.ShortName,
                    Current = entry.selected,
                });
            }
        }

        private void export()
        {
            var selectedShortNames = entries.Where(e => e.selected.Value).Select(e => e.ruleset.ShortName).ToHashSet();

            this.HidePopover();

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
                        .Filter("DeletePending == false")
                        .ToList()
                        .Where(p => selectedShortNames.Contains(p.Ruleset.ShortName))
                        .Select(p => new ModPresetTransferObject
                        {
                            Name = p.Name,
                            Description = p.Description,
                            ModsJson = p.ModsJson,
                            RulesetShortName = p.Ruleset.ShortName,
                        })
                        .ToList());

                    if (transferObjects.Count == 0)
                    {
                        notification.Text = "No presets found to export.";
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
    }

    public partial class ExportCollectionsButton : SettingsButtonV2, IHasPopover
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Export collections to file";
            TooltipText = "Choose collections to export";
            Action = this.ShowPopover;
        }

        public Popover GetPopover() => new ExportCollectionsPopover();
    }

    public partial class ExportCollectionsPopover : OsuPopover
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private readonly Bindable<bool> selectAll = new Bindable<bool>(true);
        private readonly Bindable<bool> includeScores = new Bindable<bool>(false);
        private readonly List<(BeatmapCollection collection, Bindable<bool> selected)> entries = new List<(BeatmapCollection, Bindable<bool>)>();

        private FillFlowContainer collectionList = null!;

        public ExportCollectionsPopover()
        {
            AutoSizeAxes = Axes.Both;
            Origin = Anchor.TopCentre;

            Child = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                Width = 360,
                Spacing = new Vector2(10f),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "Export collections",
                        Font = OsuFont.GetFont(weight: FontWeight.Bold),
                    },
                    new OsuCheckbox
                    {
                        LabelText = "Select all",
                        Current = selectAll,
                    },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Child = collectionList = new FillFlowContainer
                        {
                            Direction = FillDirection.Vertical,
                            AutoSizeAxes = Axes.Y,
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(4f),
                        },
                    },
                    new OsuCheckbox
                    {
                        LabelText = "Include local scores in export",
                        Current = includeScores,
                    },
                    new RoundedButton
                    {
                        Text = "Export",
                        Height = 40,
                        RelativeSizeAxes = Axes.X,
                        Action = export,
                    },
                }
            };

            selectAll.ValueChanged += e =>
            {
                foreach (var entry in entries)
                    entry.selected.Value = e.NewValue;
            };
        }

        protected override void PopIn()
        {
            base.PopIn();
            reloadCollections();
        }

        private void reloadCollections()
        {
            collectionList.Clear();
            entries.Clear();

            realm.Run(r =>
            {
                foreach (var collection in r.All<BeatmapCollection>().Detach())
                    entries.Add((collection, new Bindable<bool>(true)));
            });

            foreach (var entry in entries)
            {
                entry.selected.ValueChanged += _ =>
                {
                    selectAll.Value = entries.All(e => e.selected.Value);
                };

                collectionList.Add(new OsuCheckbox
                {
                    LabelText = entry.collection.Name,
                    Current = entry.selected,
                });
            }
        }

        private void export()
        {
            var selected = entries.Where(e => e.selected.Value).Select(e => e.collection).ToList();

            if (selected.Count == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "No collections selected." });
                return;
            }

            bool withScores = includeScores.Value;
            this.HidePopover();

            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = "Exporting collections...",
                CompletionText = "Collections exported!",
            };
            notifications.Post(notification);

            Task.Run(() =>
            {
                try
                {
                    var exportStorage = storage.GetStorageForDirectory("exports");
                    string filename = withScores ? "collections_with_scores.json" : "collections.json";

                    var collectionObjects = new List<CollectionTransferObject>();

                    realm.Run(r =>
                    {
                        foreach (var collection in selected)
                        {
                            var dto = new CollectionTransferObject
                            {
                                Name = collection.Name,
                                Beatmaps = new List<CollectionBeatmapEntry>()
                            };

                            foreach (var hash in collection.BeatmapMD5Hashes)
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

                                if (withScores)
                                {
                                    var scores = r.All<ScoreInfo>()
                                        .Filter("BeatmapInfo.MD5Hash == $0 && DeletePending == false", hash)
                                        .Detach()
                                        .ToList();

                                    foreach (var s in scores)
                                        entry.Scores.Add(ScoreExportDto.FromScore(s));
                                }

                                dto.Beatmaps.Add(entry);
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
                    string json = JsonConvert.SerializeObject(collectionObjects, Formatting.Indented);

                    notification.Text = "Writing file...";
                    using (var stream = exportStorage.CreateFileSafely(filename))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(json);
                    }

                    notification.CompletionText = $"Exported {collectionObjects.Count} collections to {filename}!";
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

    public partial class ImportFromClipboardButton : SettingsButtonV2
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
            Text = "Import from clipboard";
            TooltipText = "Imports preset or collection JSON from the clipboard (auto-detected)";
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

            switch (JsonImportTypeDetector.Detect(json))
            {
                case JsonImportType.Presets:
                    new ModPresetImportProcessor(realm, notifications, action => Schedule(action)).Import(json);
                    break;

                case JsonImportType.Collections:
                    _ = new CollectionImportProcessor(realm, notifications, api, beatmapManager, action => Schedule(action)).Import(json);
                    break;

                default:
                    notifications?.Post(new SimpleErrorNotification { Text = "Clipboard content is not valid presets or collections JSON." });
                    break;
            }
        }
    }
}

