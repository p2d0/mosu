// "Import delta beatmap" settings button: imports a .olz/.osz/.osu and, when the map
// contains delta gimmick sections, registers it under the mosu ruleset so the mosu
// editor (gimmick toolbox etc.) engages for it.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.MOsu.Database;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class ImportDeltaBeatmapButton : SettingsButtonV2, IHasPopover
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Text = "Import delta beatmap";
            TooltipText = "Import a .olz / .osz / .osu; maps with gimmicks are registered as mosu";
            Action = () =>
            {
                if (this.FindClosestParent<PopoverContainer>()?.CurrentTarget == this)
                    this.HidePopover();
                else
                    this.ShowPopover();
            };
        }

        public Popover GetPopover() => new ImportDeltaBeatmapPopover();
    }

    public partial class ImportDeltaBeatmapPopover : OsuPopover
    {
        private static readonly string[] valid_extensions = { ".olz", ".osz", ".osu" };

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private OsuFileSelector fileSelector = null!;
        private RoundedButton importButton = null!;

        public ImportDeltaBeatmapPopover()
        {
            AutoSizeAxes = Axes.Both;
            Origin = Anchor.TopCentre;

            Child = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                Width = 460,
                Spacing = new Vector2(10f),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "Import delta beatmap",
                        Font = OsuFont.GetFont(weight: FontWeight.Bold),
                    },
                    new OsuSpriteText
                    {
                        Text = "Maps containing gimmick sections are registered under the mosu ruleset.",
                        Font = OsuFont.GetFont(size: 12),
                    },
                    fileSelector = new OsuFileSelector(validFileExtensions: valid_extensions)
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 260,
                    },
                    importButton = new RoundedButton
                    {
                        Text = "Import",
                        Height = 40,
                        RelativeSizeAxes = Axes.X,
                        Enabled = { Value = false },
                        Action = import,
                    },
                }
            };

            fileSelector.CurrentFile.BindValueChanged(file =>
            {
                bool valid = file.NewValue != null && valid_extensions.Contains(file.NewValue.Extension, StringComparer.OrdinalIgnoreCase);
                importButton.Enabled.Value = valid;
            }, true);
        }

        private void import()
        {
            var file = fileSelector.CurrentFile.Value;
            if (file == null)
                return;

            this.HidePopover();

            string path = file.FullName;

            var notification = new ProgressNotification
            {
                State = ProgressNotificationState.Active,
                Text = $"Importing {Path.GetFileName(path)}...",
                CompletionText = "Imported!",
            };
            notifications.Post(notification);

            bool hasGimmicks = MosuImportUtils.FileHasGimmicks(path);

            Task.Run(async () =>
            {
                try
                {
                    var result = await beatmapManager.Import(notification, new[] { new ImportTask(path) });

                    if (hasGimmicks && result.Any())
                    {
                        MosuImportUtils.AssignMosuRuleset(realm, result);
                        notification.CompletionText = "Imported and registered as mosu!";
                    }
                }
                catch (Exception ex)
                {
                    notification.State = ProgressNotificationState.Cancelled;
                    Logger.Error(ex, "Delta beatmap import failed");
                    Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Import failed: {ex.Message}" }));
                }
            });
        }
    }
}
