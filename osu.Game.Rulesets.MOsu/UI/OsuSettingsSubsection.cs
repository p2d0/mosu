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
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.UI;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Localisation;
using osuTK;
using osu.Game.Graphics.UserInterface;
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
using osu.Game.Collections;
using osu.Game.Scoring;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Localisation;
using osu.Game.Screens;
using osu.Game.Models;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class OsuSettingsSubsection : RulesetSettingsSubsection
    {
        private Ruleset ruleset;

        protected override LocalisableString Header => "osu!";

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private IPerformFromScreenRunner? performer { get; set; }

        private readonly Bindable<bool> exportWithScores = new Bindable<bool>(false);

        public OsuSettingsSubsection(Ruleset ruleset)
            : base(ruleset)
        {
            this.ruleset = ruleset;
        }
        private LocalUserProfileOverlay? localUserProfileOverlay;
        private ChangelogOverlay? changelogOverlay;
        // private ToolbarLocalUserButton? localUserButton;

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api, OsuGame game,GameHost host)
        {
            // LocalUserManager localUserManager;
            // host.Dependencies.Cache(localUserManager = new LocalUserManager((OsuRuleset)ruleset, realm, api));

            // game.GetToolbarContainer().Add(new ToolbarLocalUserButton());

            var config = (OsuRulesetConfigManager)Config;
            Children = new Drawable[]
            {
                new SettingsCheckbox
                {
                    LabelText = RulesetSettingsStrings.SnakingInSliders,
                    Current = config.GetBindable<bool>(OsuRulesetSetting.SnakingInSliders)
                },
                new SettingsCheckbox
                {
                    ClassicDefault = false,
                    LabelText = RulesetSettingsStrings.SnakingOutSliders,
                    Current = config.GetBindable<bool>(OsuRulesetSetting.SnakingOutSliders)
                },
                new SettingsCheckbox
                {
                    LabelText = RulesetSettingsStrings.CursorTrail,
                    Current = config.GetBindable<bool>(OsuRulesetSetting.ShowCursorTrail)
                },
                new SettingsCheckbox
                {
                    LabelText = RulesetSettingsStrings.CursorRipples,
                    Current = config.GetBindable<bool>(OsuRulesetSetting.ShowCursorRipples)
                },
                new SettingsEnumDropdown<PlayfieldBorderStyle>
                {
                    LabelText = RulesetSettingsStrings.PlayfieldBorderStyle,
                    Current = config.GetBindable<PlayfieldBorderStyle>(OsuRulesetSetting.PlayfieldBorderStyle),
                },
                // --- Preset Export ---
                new SettingsButtonV2
                {
                    Text = "Export presets to file",
                    TooltipText = "Saves all mosususu presets to exports/osu_mod_presets.json",
                    Action = exportPresets
                },
                
                // --- Preset Import (Popup) ---
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
                    Margin = new MarginPadding { Top = 20, Bottom = 5 },
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
                    TooltipText = "Open file browser to select a collection .json (Standard format)",
                    Action = () =>
                    {
                        performer?.PerformFromScreen(screen => screen.Push(new CollectionImportScreen()));
                    }
                },
                new ImportCollectionScoresButton(),
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
                        .Filter("Ruleset.ShortName == $0 && DeletePending == false", "mosususu")
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
                        notification.Text = "No mosususu presets found to export.";
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
                        var collectionObjects = new List<CollectionWithScoresTransferObject>();

                        realm.Run(r =>
                        {
                            var collections = r.All<BeatmapCollection>().Detach().ToList();
                            int total = collections.Count;
                            int current = 0;

                            foreach (var c in collections)
                            {
                                if (notification.State == ProgressNotificationState.Cancelled) return;

                                var dto = new CollectionWithScoresTransferObject
                                {
                                    Name = c.Name,
                                    BeatmapMD5Hashes = c.BeatmapMD5Hashes.ToList(),
                                    Scores = new List<ScoreExportDto>()
                                };

                                foreach (var hash in c.BeatmapMD5Hashes)
                                {
                                    var scores = r.All<ScoreInfo>()
                                        .Filter("BeatmapInfo.MD5Hash == $0 && DeletePending == false", hash)
                                        .Detach()
                                        .ToList();

                                    foreach (var s in scores)
                                    {
                                        dto.Scores.Add(new ScoreExportDto
                                        {
                                            BeatmapHash = s.BeatmapInfo.MD5Hash,
                                            RulesetShortName = s.Ruleset.ShortName,
                                            TotalScore = s.TotalScore,
                                            Accuracy = s.Accuracy,
                                            MaxCombo = s.MaxCombo,
                                            Rank = s.Rank.ToString(),
                                            Date = s.Date,
                                            Mods = s.Mods.Select(m => new APIMod(m)).ToList(),
                                            Statistics = s.Statistics.ToDictionary(k => k.Key.ToString(), v => v.Value)
                                        });
                                    }
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
                        var collections = realm.Run(r => r.All<BeatmapCollection>().Detach())
                            .Select(c => new CollectionTransferObject
                            {
                                Name = c.Name,
                                BeatmapMD5Hashes = c.BeatmapMD5Hashes.ToList()
                            })
                            .ToList();

                        if (collections.Count == 0)
                        {
                            notification.Text = "No collections found to export.";
                            notification.State = ProgressNotificationState.Cancelled;
                            return;
                        }

                        notification.Text = "Serializing data...";
                        json = JsonConvert.SerializeObject(collections, Formatting.Indented);
                        count = collections.Count;
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

        // DTOs for Scores Export
        public class CollectionWithScoresTransferObject
        {
            public string Name { get; set; } = string.Empty;
            public List<string> BeatmapMD5Hashes { get; set; } = new List<string>();
            public List<ScoreExportDto> Scores { get; set; } = new List<ScoreExportDto>();
        }

        public class ScoreExportDto
        {
            public string BeatmapHash { get; set; } = string.Empty;
            public string RulesetShortName { get; set; } = string.Empty;
            public long TotalScore { get; set; }
            public double Accuracy { get; set; }
            public int MaxCombo { get; set; }
            public string Rank { get; set; } = string.Empty;
            public DateTimeOffset Date { get; set; }
            public List<APIMod> Mods { get; set; } = new List<APIMod>();
            public Dictionary<string, int> Statistics { get; set; } = new Dictionary<string, int>();
        }
    }

    public partial class ImportPresetButton : SettingsButtonV2, IHasPopover
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Import presets (Paste JSON)";
            TooltipText = "Click to paste preset JSON data";
            Action = this.ShowPopover;
        }

        public Popover GetPopover() => new ImportPresetPopover();
    }

    public partial class ImportCollectionScoresButton : SettingsButtonV2
    {
        [Resolved(CanBeNull = true)]
        private IPerformFromScreenRunner? performer { get; set; }

        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Import collections + scores from file";
            TooltipText = "Import a JSON file containing collections and scores";
            Action = () => performer?.PerformFromScreen(screen => screen.Push(new CollectionWithScoresImportScreen()));
        }
    }

    public partial class ImportPresetPopover : OsuPopover
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private readonly FocusedTextBox textBox;

        public ImportPresetPopover()
        {
            AutoSizeAxes = Axes.Both;
            Origin = Anchor.TopCentre;

            RoundedButton importButton;

            Child = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                Width = 300,
                Spacing = new Vector2(10f),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "Paste JSON below:",
                        Font = OsuFont.GetFont(weight: FontWeight.Bold),
                    },
                    textBox = new FocusedTextBox
                    {
                        PlaceholderText = @"[{ ""Name"": ""..."" ... }]",
                        Height = 100,
                        RelativeSizeAxes = Axes.X,
                        SelectAllOnFocus = true,
                        LengthLimit = 1000000,
                    },
                    importButton = new RoundedButton
                    {
                        Height = 40,
                        RelativeSizeAxes = Axes.X,
                        Text = "Import",
                    }
                }
            };

            importButton.Action += import;
            textBox.OnCommit += (_, _) => import();
        }

        protected override void PopIn()
        {
            base.PopIn();
            textBox.TakeFocus();
        }

        private void import()
        {
            string json = textBox.Text;

            if (string.IsNullOrWhiteSpace(json))
            {
                this.HidePopover();
                return;
            }

            try
            {
                var transferObjects = JsonConvert.DeserializeObject<List<ModPresetTransferObject>>(json);

                if (transferObjects == null || transferObjects.Count == 0)
                {
                    notifications?.Post(new SimpleErrorNotification { Text = "Invalid JSON or no presets found." });
                    return;
                }

                int importedCount = 0;

                realm.Write(r =>
                {
                    var osuRulesetInfo = r.Find<RulesetInfo>("mosususu");

                    if (osuRulesetInfo == null)
                        throw new InvalidOperationException("Could not find mosususu ruleset in database.");

                    foreach (var dto in transferObjects)
                    {
                        bool exists = r.All<ModPreset>()
                            .Filter("Name == $0 && Ruleset.ShortName == $1 && DeletePending == false", dto.Name, "mosususu")
                            .Count() > 0;

                        if (exists) continue;

                        r.Add(new ModPreset
                        {
                            ID = Guid.NewGuid(),
                            Name = dto.Name,
                            Description = dto.Description,
                            ModsJson = dto.ModsJson,
                            Ruleset = osuRulesetInfo,
                            DeletePending = false
                        });
                        importedCount++;
                    }
                });

                if (importedCount > 0)
                    notifications?.Post(new SimpleNotification { Text = $"Successfully imported {importedCount} presets!" });
                else
                    notifications?.Post(new SimpleNotification { Text = "No new presets imported (duplicates skipped)." });

                this.HidePopover();
            }
            catch (JsonException)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Failed to parse JSON. Check format." });
            }
            catch (Exception ex)
            {
                notifications?.Post(new SimpleErrorNotification { Text = $"Import failed: {ex.Message}" });
            }
        }
    }
}
