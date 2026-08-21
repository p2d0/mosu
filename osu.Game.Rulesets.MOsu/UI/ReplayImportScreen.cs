// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI
{
    /// <summary>
    /// Imports a replay exported from MOsu (a legacy-format .osr). The file's ruleset byte always claims
    /// osu! (core can only encode legacy-format replays for legacy rulesets), so decode it as MOsu here to
    /// resolve custom mods (RDV2, NL, ...) instead of degrading them to UnknownMods.
    /// </summary>
    public partial class ReplayImportScreen : OsuScreen
    {
        public override bool HideOverlaysOnEnter => true;
        public override bool DisallowExternalBeatmapRulesetChanges => true;

        private OsuFileSelector fileSelector = null!;
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
        private ScoreManager scoreManager { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private IRulesetStore rulesets { get; set; } = null!;

        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                Masking = true,
                CornerRadius = 10,
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(0.9f, 0.8f),
                Children = new Drawable[]
                {
                    fileSelector = new OsuFileSelector(validFileExtensions: new[] { ".osr" })
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
                                    Child = currentFileText = new TextFlowContainer(t => t.Font = OsuFont.Default.With(size: 24))
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
            this.FadeInFromZero(duration);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            this.FadeOut(duration, Easing.OutQuint);
            return base.OnExiting(e);
        }

        private void fileChanged(ValueChangedEvent<FileInfo> selectedFile)
        {
            importButton.Enabled.Value = selectedFile.NewValue != null;
            currentFileText.Text = selectedFile.NewValue?.Name ?? "Select a .osr replay file";
        }

        private void importFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            importButton.Enabled.Value = false;
            currentFileText.Text = "Reading replay...";

            Task.Run(() =>
            {
                try
                {
                    // Buffer upfront: Parse's SerializationReader closes the stream it is handed.
                    byte[] fileBytes = File.ReadAllBytes(path);

                    // Parse yields a score whose BeatmapInfo is still managed against the beatmap realm;
                    // importing it from this thread chokes on the foreign managed object, so detach first.
                    var score = new MosuLegacyScoreDecoder(rulesets, beatmaps).Parse(new MemoryStream(fileBytes));
                    var scoreInfo = score.ScoreInfo.Detach();

                    // Mirror the attach-on-save path: a non-empty hash keeps the replay button + watch flow working.
                    if (string.IsNullOrEmpty(scoreInfo.Hash))
                        scoreInfo.Hash = $"mosu-replay-{scoreInfo.ID:N}";

                    // Read the ID before importing: import attaches the passed model to a transient realm which is
                    // closed before we return, so any property access on it afterwards throws a RealmClosedException.
                    Guid scoreId = scoreInfo.ID;

                    scoreManager.Import(scoreInfo);

                    realm.Write(r =>
                    {
                        var managed = r.Find<ScoreInfo>(scoreId);
                        if (managed != null && managed.Files.Count == 0)
                            scoreManager.AddFile(managed, new MemoryStream(fileBytes), "replay.osr", r);
                    });

                    Schedule(() =>
                    {
                        notifications?.Post(new SimpleNotification { Text = "Replay imported." });
                        this.Exit();
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Replay import failed");
                    Schedule(() =>
                    {
                        notifications?.Post(new SimpleErrorNotification
                        {
                            Text = ex is LegacyScoreDecoder.BeatmapNotFoundException
                                ? "The beatmap this replay was played on is not available locally."
                                : $"Import failed: {ex.Message}"
                        });
                        importButton.Enabled.Value = true;
                        currentFileText.Text = "Select a .osr replay file";
                    });
                }
            });
        }

        /// <summary>
        /// Decodes a legacy .osr as a MOsu score regardless of the ruleset byte stored in the file.
        /// </summary>
        private class MosuLegacyScoreDecoder : DatabasedLegacyScoreDecoder
        {
            public MosuLegacyScoreDecoder(IRulesetStore rulesets, BeatmapManager beatmaps)
                : base(rulesets, beatmaps)
            {
            }

            protected override Ruleset GetRuleset(int rulesetId) => new MosuRuleset();
        }
    }
}