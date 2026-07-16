using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Extensions;
using osu.Game.Rulesets.MOsu.UI.Chat;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osuTK;
using osuTK.Graphics;
using System.Linq;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class MOsuIcon : CompositeDrawable
    {
        // CreateIcon() is called many times by core. Only the first instance should run injection.
        private static bool _injected;

        private readonly OsuRuleset ruleset;

        public MOsuIcon(OsuRuleset ruleset)
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
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = Color4.Black,
                    Text = "M",
                    Font = OsuFont.Default.With(size: 32)
                }
            };

            if (!_injected)
            {
                _injected = true;
                Schedule(LoadInjection);
            }
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
    internal partial class MOsuSystemManager : Component
    {
        [Resolved]
        private OsuGame game { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private readonly OsuRuleset ruleset;

        public MOsuSystemManager(OsuRuleset ruleset)
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
            var mosuRealm = host.Dependencies.Get<MOsuRealmAccess>();
            if (mosuRealm == null)
            {
                mosuRealm = new MOsuRealmAccess(host.Storage);
                host.Dependencies.Cache(mosuRealm);
            }

            var userManager = host.Dependencies.Get<LocalUserManager>();
            if (userManager == null)
            {
                userManager = new LocalUserManager(ruleset, realm, mosuRealm, api);
                host.Dependencies.Cache(userManager);
            }

            await Task.Yield();

            Schedule(() =>
            {
                var waveContainer = game.GetWaveOverlayPlacementContainer();
                var toolbarContainer = game.GetToolbarContainer();

                if (waveContainer == null || toolbarContainer == null)
                {
                    Schedule(StepUI);
                    return;
                }

                StepUI();

                void StepUI()
                {
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

                    var existingOverlay = waveContainer!.Children.OfType<LocalUserProfileOverlay>().FirstOrDefault();
                    if (existingOverlay == null)
                    {
                        existingOverlay = host.Dependencies.Get<LocalUserProfileOverlay>();
                        if (existingOverlay == null || existingOverlay.Parent == null)
                        {
                            existingOverlay = new LocalUserProfileOverlay();
                            waveContainer.Add(existingOverlay);
                            if (host.Dependencies.Get<LocalUserProfileOverlay>() == null)
                                host.Dependencies.Cache(existingOverlay);
                        }
                    }

                    if (toolbarContainer != null && !toolbarContainer.Children.OfType<ToolbarLocalUserButton>().Any())
                        toolbarContainer.Add(new ToolbarLocalUserButton());
                }
            });
        }
    }
}
