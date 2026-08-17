using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Rulesets.MOsu.Configuration;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Extensions;
using osu.Game.Rulesets.MOsu.UI.Chat;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osu.Game.Rulesets.MOsu.Delta.UI;
using osuTK;
using osuTK.Graphics;
using System.Linq;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class MOsuIcon : CompositeDrawable
    {
        // CreateIcon() is called many times by core. Only the first instance should run injection.
        private static bool _injected;

        private readonly MosuRuleset ruleset;
        private OsuSpriteText text = null!;

        public MOsuIcon(MosuRuleset ruleset)
        {
            this.ruleset = ruleset;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(32);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                },
                text = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = Color4.Black,
                    Text = "M",
                }
            };

            if (!_injected)
            {
                _injected = true;
                Schedule(LoadInjection);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Parent is null during load() (async load runs before attachment), so the
            // fillflow check only works once attached.
            if (Parent is not FillFlowContainer)
                text.Font = OsuFont.Default.With(size: 32);
        }

        private void LoadInjection()
        {
            AddRangeInternal(new Drawable[]
            {
                new MOsuSystemManager(ruleset),
                new ChatOverlayInjector()
            });
        }
    }

    /// <summary>
    /// Handles injection of overlays and toolbar buttons.
    /// All heavy work (Realm DB open, manager construction) runs off the game thread.
    /// </summary>
    internal partial class MOsuSystemManager : CompositeDrawable
    {
        [Resolved]
        private OsuGame game { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;
        [Resolved]
        private IAPIProvider api { get; set; } = null!;
        [Resolved]
        private IRulesetConfigCache configCache { get; set; } = null!;

        private readonly MosuRuleset ruleset;

        public MOsuSystemManager(MosuRuleset ruleset)
        {
            this.ruleset = ruleset;
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Task.Run(InitializeAsync);
        }

        private async Task InitializeAsync()
        {
            // Heavy work off the game thread.
            var userManager = host.Dependencies.Get<LocalUserManager>();
            if (userManager == null)
            {
                var config = configCache.GetConfigFor(ruleset) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");
                userManager = new LocalUserManager(ruleset, realm, config, api);
                host.Dependencies.Cache(userManager);
            }

            await Task.Yield();

            Schedule(StepUI);
        }

        private void StepUI()
        {
            var waveContainer = game.GetWaveOverlayPlacementContainer();
            var toolbarContainer = game.GetToolbarContainer();

            if (waveContainer == null || toolbarContainer == null)
            {
                Schedule(StepUI);
                return;
            }

            if (host.Dependencies.Get<BackgroundPresetImportProcessor>() == null)
            {
                var presetImporter = new BackgroundPresetImportProcessor();
                host.Dependencies.Cache(presetImporter);
                game.Add(presetImporter);
            }

            if (host.Dependencies.Get<BackgroundCollectionImportProcessor>() == null)
            {
                var collectionImporter = new BackgroundCollectionImportProcessor();
                host.Dependencies.Cache(collectionImporter);
                game.Add(collectionImporter);
            }

            // Pre-load overlay on bg thread.
            var overlay = host.Dependencies.Get<LocalUserProfileOverlay>();
            if (overlay == null)
            {
                overlay = waveContainer.Children.OfType<LocalUserProfileOverlay>().FirstOrDefault();
            }
            if (overlay == null)
            {
                overlay = new LocalUserProfileOverlay();
                LoadComponentAsync(overlay, o =>
                {
                    waveContainer.Add(o);
                    host.Dependencies.Cache(o);
                });
            }
            else
            {
                host.Dependencies.Cache(overlay);
            }

            // Add button immediately.
            if (!toolbarContainer.Children.OfType<ToolbarLocalUserButton>().Any())
                toolbarContainer.Add(new ToolbarLocalUserButton());

            // Hook the song select context menu once the screen is active (repeats until it is).
            Scheduler.AddDelayed(() => DeltaMenuInjector.HookSongSelectContextMenu(game, realm), 500, true);

        }
    }
}
