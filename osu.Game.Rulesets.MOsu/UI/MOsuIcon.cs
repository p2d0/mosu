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
            // 1. The Icon strictly handles visuals.
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
                    Font = OsuFont.Default.With(size: 32) // Make sure font size fits
                },
                // 2. We attach the Logic Manager as a child.
                // It will run its lifecycle without cluttering the Icon code.
                new MOsuSystemManager(ruleset),
                // new BeatmapModsSelectInjector(),
                new ChatOverlayInjector()
            };
        }
    }

    /// <summary>
    /// Handles the injection of the UserProfileOverlay and Toolbar Buttons.
    /// Invisible and runs in the background.
    /// </summary>
    internal partial class MOsuSystemManager : Component
    {
        [Resolved]
        private OsuGame game { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        // Resolve other dependencies needed for your UserManager
        [Resolved]
        private RealmAccess realm { get; set; } = null!;
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private readonly OsuRuleset ruleset;
        private bool isInitialized;

        public MOsuSystemManager(OsuRuleset ruleset)
        {
            this.ruleset = ruleset;
        }

        protected override void Update()
        {
            base.Update();

            // 1. If we have already finished setup, stop running this logic.
            if (isInitialized) return;

            // 2. Poll the game state. We cannot inject until the game has created these containers.
            // This replaces the dangerous "Scheduler.AddDelayed(1000)".
            var waveContainer = game.GetWaveOverlayPlacementContainer();
            var toolbarContainer = game.GetToolbarContainer();

            // If the containers aren't ready yet, we simply wait for the next frame.
            if (waveContainer == null || toolbarContainer == null) return;

            // 3. Perform initialization safely.
            InitializeSystem(waveContainer, toolbarContainer);

            // 4. Mark as done so Update stops checking.
            isInitialized = true;
        }

        private void InitializeSystem(Container waveContainer, FillFlowContainer toolbarContainer)
        {
            // --- 1. Setup LocalUserManager (Singleton Logic) ---
            var userManager = host.Dependencies.Get<LocalUserManager>();
            if (userManager == null)
            {
                userManager = new LocalUserManager(ruleset, realm, api);
                host.Dependencies.Cache(userManager);
            }
            var db = host.Dependencies.Get<MOsuRealmAccess>();
            if (db == null)
            {
                db = new MOsuRealmAccess(host.Storage);
                host.Dependencies.Cache(db);
            }

            // --- 1.5 Setup Background Preset Import ---
            if (host.Dependencies.Get<BackgroundPresetImportProcessor>() == null)
            {
                var presetImporter = new BackgroundPresetImportProcessor();
                host.Dependencies.Cache(presetImporter);
                game.Add(presetImporter);
            }

            // --- 2. Setup Overlay ---
            // Check if it already exists in the container to prevent duplicates
            var existingOverlay = waveContainer.Children.OfType<LocalUserProfileOverlay>().FirstOrDefault();

            if (existingOverlay == null)
            {
                // If checking dependencies, ensure we don't grab a disposed one from a previous session
                existingOverlay = host.Dependencies.Get<LocalUserProfileOverlay>();

                if (existingOverlay == null || existingOverlay.Parent == null)
                {
                    existingOverlay = new LocalUserProfileOverlay();
                    waveContainer.Add(existingOverlay);

                    // Only cache if not already cached
                    if (host.Dependencies.Get<LocalUserProfileOverlay>() == null)
                        host.Dependencies.Cache(existingOverlay);
                }
            }

            // --- 3. Setup Toolbar Button ---
            // Check if our specific button type already exists
            bool buttonExists = toolbarContainer.Children.OfType<ToolbarLocalUserButton>().Any();

            if (!buttonExists)
            {
                var button = new ToolbarLocalUserButton();
                toolbarContainer.Add(button);
            }
        }
    }
}
