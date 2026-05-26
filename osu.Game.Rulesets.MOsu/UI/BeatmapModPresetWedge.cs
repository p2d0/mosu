using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Database; // Ensure this matches your model namespace
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Select;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.MOsu.UI
{

    public partial class BeatmapModPresetWedge : VisibilityContainer
    {
        [Resolved]
        private MOsuRealmAccess realm { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

        private FillFlowContainer<BeatmapModPresetPanel> presetFlow = null!;
        private IDisposable? realmSubscription;

        protected override bool StartHidden => false;

        public BeatmapModPresetWedge()
        {
            RelativeSizeAxes = Axes.Both;
            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;
            Padding = new MarginPadding { Bottom = 20, Left = 40};
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            // 1. OsuContextMenuContainer handles the Right-Click menu logic
            InternalChild = new OsuContextMenuContainer
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    // 2. The Background stays OUTSIDE the shear correction.
                    // It inherits the parent's negative shear, giving it the "Wedge" shape.
                    new LocalWedgeBackground(),

                    // 3. The Content gets sheared positively to appear straight to the user.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = 15, Bottom = 15, Left = 20, Right = 10 }, // Adjusted Left padding for shear slant
                        Shear = OsuGame.SHEAR,
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 10),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = "Saved Mod Presets",
                                        Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 16),
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                    },
                                    new OsuScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        ScrollbarVisible = false,
                                        Child = presetFlow = new FillFlowContainer<BeatmapModPresetPanel>
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 5),
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            beatmap.BindValueChanged(_ => updateSubscription(), true);
            ruleset.BindValueChanged(_ => updateSubscription());
        }

        public void SaveCurrentMods(string name)
        {
            var modsToSave = selectedMods.Value.Where(m => m.Type != ModType.System).ToList();

            if (!modsToSave.Any()) return;

            string currentHash = beatmap.Value.BeatmapInfo.MD5Hash;
            var ruleset = this.ruleset.Value;

            realm.Write(r =>
            {
                var dbRuleset = r.Find<RulesetInfo>(ruleset.ShortName) ?? ruleset;

                r.Add(new BeatmapModPreset
                {
                    BeatmapMD5Hash = currentHash,
                    Ruleset = dbRuleset,
                    Mods = modsToSave,
                    Name = name // <--- Save the name here
                });
            });

            presetFlow.FlashColour(Colour4.White, 300);
        }

        private void updateSubscription()
        {
            realmSubscription?.Dispose();
            presetFlow.Clear();

            if (beatmap.Value == null)
                return;

            string currentHash = beatmap.Value.BeatmapInfo.MD5Hash;

            Schedule(() => {
                var hashStr = currentHash != "" ? $"BeatmapMD5Hash == {currentHash} AND" : "";
                realmSubscription = realm.RegisterForNotifications(
                    r => r.All<BeatmapModPreset>().Filter("BeatmapMD5Hash == $0 && Ruleset.ShortName == $1", currentHash, ruleset.Value.ShortName)// .Where(p => p.BeatmapMD5Hash == currentHash && p.Ruleset == uleset.Value)
                    ,
                    (sender, changes) =>
                    {
                        if (changes == null)
                        {
                            foreach (var p in sender) addPanel(p);
                            return;
                        }

                        foreach (var i in changes.DeletedIndices.OrderByDescending(i => i))
                        {
                            if (i < presetFlow.Count) presetFlow.Remove(presetFlow.Children[i], true);
                        }

                        foreach (var i in changes.InsertedIndices)
                        {
                            if (i <= presetFlow.Count) addPanel(sender[i]);
                        }

                        foreach (var i in changes.ModifiedIndices)
                        {
                            if (i < presetFlow.Count) presetFlow.Children[i].FlashColour(Colour4.White, 500);
                        }
                    }
                );
            });
        }

        private void addPanel(BeatmapModPreset preset)
        {
            presetFlow.Add(new BeatmapModPresetPanel(preset));
        }

        protected override void PopIn() => this.FadeIn(300, Easing.OutQuint).MoveToX(0, 300, Easing.OutQuint);
        protected override void PopOut() => this.FadeOut(300, Easing.OutQuint).MoveToX(0, 300, Easing.OutQuint);

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();
        }
    }

    public partial class BeatmapModPresetPanel : OsuClickableContainer, IHasContextMenu
    {
        private readonly BeatmapModPreset preset;
        private readonly BindableBool active = new BindableBool();

        private string exportName = string.Empty;
        private string exportRulesetShortName = string.Empty;
        private List<Mod> exportMods = new List<Mod>();

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

        [Resolved]
        private MOsuRealmAccess realm { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        // --- NEW DEPENDENCIES FOR EXPORT ---
        [Resolved]
        private Clipboard clipboard { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;
        // -----------------------------------

        private Box background = null!;
        private StarRatingDisplay starRatingDisplay = null!;
        private CancellationTokenSource? cancellationTokenSource;

        public BeatmapModPresetPanel(BeatmapModPreset preset)
        {
            this.preset = preset;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            Height = 50;
            CornerRadius = 5;
            Masking = true;

            var mods = preset.Mods.ToList();

            exportName = preset.Name;
            exportRulesetShortName = preset.Ruleset?.ShortName ?? ruleset.Value.ShortName;
            exportMods = mods;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background4,
                },

                new OsuSpriteText
                {
                    Text = string.IsNullOrEmpty(preset.Name) ? "Untitled" : preset.Name,
                    Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 20),
                    Padding = new MarginPadding { Left = 25 },
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Spacing = new Vector2(20, 0),
                    Padding = new MarginPadding { Right = 5 },
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8),
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(2),
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    ChildrenEnumerable = mods.Select(m => new ModIcon(m)
                                    {
                                        Scale = new Vector2(0.4f),
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    })
                                },
                                starRatingDisplay = new StarRatingDisplay(default, size: StarRatingDisplaySize.Small)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                            }
                        },
                    }
                },
                new HoverLayer()
            };

            Action = toggleSelection;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            selectedMods.BindValueChanged(_ => updateActiveState(), true);
            active.BindValueChanged(val =>
            {
                background.FadeColour(val.NewValue ? colourProvider.Content2 : colourProvider.Background4, 200);
            }, true);

            cancellationTokenSource = new CancellationTokenSource();

            var beatmapInfo = beatmap.Value.BeatmapInfo;
            var presetRuleset = preset.Ruleset ?? ruleset.Value;

            var safeBeatmapInfo = osu.Game.Database.RealmObjectExtensions.Detach(beatmapInfo);
            var safeRulesetInfo = osu.Game.Rulesets.MOsu.Database.MOsuRealmExtensions.Detach(presetRuleset);
            var safeMods = preset.Mods.ToList();

            difficultyCache.GetDifficultyAsync(safeBeatmapInfo, safeRulesetInfo, safeMods, cancellationTokenSource.Token)
                .ContinueWith(task => Schedule(() =>
                {
                    if (task.IsCanceled) return;
                    var difficulty = task.GetResultSafely();
                    if (difficulty != null)
                    {
                        starRatingDisplay.Current.Value = difficulty.Value;
                    }
                }), cancellationTokenSource.Token);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            cancellationTokenSource?.Cancel();
        }

        private void toggleSelection()
        {
            if (active.Value)
            {
                var newMods = selectedMods.Value.Where(m => !preset.Mods.Any(pm => pm.Equals(m)));
                selectedMods.Value = newMods.ToArray();
            }
            else
            {
                var presetMods = preset.Mods;
                var systemMods = selectedMods.Value.Where(mod => mod.Type == ModType.System);
                var compatibleSystemMods = systemMods.Where(sm =>
                    !sm.IncompatibleMods.Any(t => presetMods.Any(pm => t.IsInstanceOfType(pm))));

                selectedMods.Value = presetMods.Concat(compatibleSystemMods).ToArray();
            }
        }

        private void updateActiveState()
        {
            var currentNonSystem = selectedMods.Value.Where(m => m.Type != ModType.System);
            var presetMods = preset.Mods;
            active.Value = new HashSet<Mod>(presetMods).SetEquals(currentNonSystem);
        }

        public MenuItem[] ContextMenuItems => new MenuItem[]
        {
            new OsuMenuItem("Export Preset", MenuItemType.Standard, () =>
            {
                try
                {
                    var exportData = new List<object>
                    {
                        new
                        {
                            Name = exportName,
                            RulesetShortName = exportRulesetShortName,
                            Mods = exportMods.Select(m => new APIMod(m)).ToList()
                        }
                    };

                    string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);
                    clipboard.SetText(json);

                    notifications.Post(new SimpleNotification
                    {
                        Text = $"Preset '{exportName}' copied to clipboard!"
                    });
                }
                catch (System.Exception ex)
                {
                    notifications.Post(new SimpleErrorNotification
                    {
                        Text = $"Failed to export preset: {ex.Message}"
                    });
                }
            }),
            new OsuMenuItem("Delete Preset", MenuItemType.Destructive, () =>
            {
                realm.Write(r =>
                {
                    if (preset.IsManaged)
                        r.Remove(preset);
                });
            })
        };

        private partial class HoverLayer : Box
        {
            [BackgroundDependencyLoader]
            private void load()
            {
                RelativeSizeAxes = Axes.Both;
                Colour = Colour4.White;
                Alpha = 0;
                Blending = BlendingParameters.Additive;
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                this.FadeTo(0.1f, 200);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                this.FadeOut(200);
                base.OnHoverLost(e);
            }
        }
    }

    internal partial class LocalWedgeBackground : InputBlockingContainer
    {
        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    Blending = BlendingParameters.Additive,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.6f,
                    Alpha = 0.5f,
                    Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0)),
                },
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.7f,
                    Colour = colourProvider.Background5.Opacity(0.9f),
                },
                new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.3f,
                    Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(0.9f), colourProvider.Background5.Opacity(0.6f)),
                },
            };
        }
    }
}
