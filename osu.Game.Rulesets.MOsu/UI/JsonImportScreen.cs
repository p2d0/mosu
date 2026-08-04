using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI
{
    /// <summary>
    /// Imports mod presets or beatmap collections from a JSON file, auto-detecting the format.
    /// </summary>
    public partial class JsonImportScreen : OsuScreen
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
                                Text = "Import",
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
            currentFileText.Text = "Reading file...";

            // Run the import in a task to prevent UI freeze during JSON parsing. The shared processors
            // post their own notifications; the screen exits once the import is underway.
            Task.Run(() =>
            {
                try
                {
                    string json = File.ReadAllText(path);

                    switch (JsonImportTypeDetector.Detect(json))
                    {
                        case JsonImportType.Presets:
                            new ModPresetImportProcessor(realm, notifications, action => Schedule(action))
                                .Import(json, count =>
                                {
                                    if (count > 0)
                                        this.Exit(); // keep screen open on all-duplicates
                                });
                            break;

                        case JsonImportType.Collections:
                            _ = new CollectionImportProcessor(realm, notifications, api, beatmapManager, action => Schedule(action))
                                .Import(json, onCollectionsImported: () => this.Exit());
                            break;

                        default:
                            Schedule(() =>
                            {
                                notifications?.Post(new SimpleErrorNotification { Text = "Selected file is not valid presets or collections JSON." });
                                importButton.Enabled.Value = true;
                            });
                            break;
                    }
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
    }
}
