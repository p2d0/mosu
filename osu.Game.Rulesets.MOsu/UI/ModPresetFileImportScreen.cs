using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2; // For OsuFileSelector
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;
using osuTK;
using osu.Game.Rulesets.Mods;
using Realms;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class ModPresetFileImportScreen : OsuScreen
    {
        public override bool HideOverlaysOnEnter => true;

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
                    // 1. File Selector filtered to .json
                    fileSelector = new OsuFileSelector(validFileExtensions: new[] { ".json" })
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0.65f
                    },
                    // 2. Right side info panel
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
                                Text = "Import Presets",
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

            Task.Run(() =>
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var transferObjects = JsonConvert.DeserializeObject<List<ModPresetTransferObject>>(json);

                    if (transferObjects == null || transferObjects.Count == 0)
                    {
                        Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = "No presets found in file." }));
                        return;
                    }

                    int importedCount = 0;

                    realm.Write(r =>
                    {
                        var osuRulesetInfo = r.Find<RulesetInfo>("mosu");
                        if (osuRulesetInfo == null) return;

                        foreach (var dto in transferObjects)
                        {
                            // Duplicate check
                            bool exists = r.All<ModPreset>()
                                .Filter("Name == $0 && Ruleset.ShortName == $1 && DeletePending == false", dto.Name, "mosu")
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

                    Schedule(() =>
                    {
                        if (importedCount > 0)
                        {
                            notifications?.Post(new SimpleNotification { Text = $"Imported {importedCount} presets!" });
                            this.Exit(); // Close screen on success
                        }
                        else
                        {
                            notifications?.Post(new SimpleNotification { Text = "All presets in file were duplicates." });
                        }
                    });
                }
                catch (Exception ex)
                {
                    Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Import failed: {ex.Message}" }));
                }
            });
        }
    }
}
