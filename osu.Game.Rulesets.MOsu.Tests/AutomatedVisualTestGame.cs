using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using SixLabors.ImageSharp;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class AutomatedVisualTestGame : OsuGameBase
    {
        private DependencyContainer dependencies = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<INotificationOverlay>(new StubNotificationOverlay());
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Add(new ScreenshotTestRunner(new TestBrowser()));
        }
    }

    public class StubNotificationOverlay : INotificationOverlay
    {
        public void Post(Overlays.Notifications.Notification notification) { }
        public void Hide() { }
        public IBindable<int> UnreadCount { get; } = new Bindable<int>(0);
        public bool HasOngoingOperations => false;
        public IEnumerable<Overlays.Notifications.Notification> AllNotifications => Array.Empty<Overlays.Notifications.Notification>();
    }

    public partial class ScreenshotTestRunner : CompositeDrawable
    {
        private const double time_between_tests = 500;
        private const double test_timeout = 5000;

        private readonly TestBrowser browser;
        private int testIndex;
        private bool testTimedOut;
        private readonly List<Type> filteredTestTypes;

        private Type loadableTestType => testIndex >= 0 ? filteredTestTypes.ElementAtOrDefault(testIndex) : null;

        private static readonly string SCREENSHOT_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "screenshots"));

        public ScreenshotTestRunner(TestBrowser browser)
        {
            this.browser = browser;
            filteredTestTypes = browser.TestTypes
                .Where(t => !typeof(PlayerTestScene).IsAssignableFrom(t)
                         && !typeof(Player).IsAssignableFrom(t)
                         && !typeof(ModTestScene).IsAssignableFrom(t))
                .Where(t => t.Name != "TestSceneOsuGame")
                .ToList();
        }

        [Resolved]
        private GameHost host { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            AddInternal(browser);
            Directory.CreateDirectory(SCREENSHOT_DIR);
            host.ExceptionThrown += _ => true;
            Scheduler.AddDelayed(runNext, 1000);
        }

        private void runNext()
        {
            if (loadableTestType == null)
            {
                Scheduler.AddDelayed(host.Exit, time_between_tests);
                return;
            }

            string testName = loadableTestType.Name;

            if (browser.CurrentTest?.GetType() != loadableTestType)
            {
                testTimedOut = false;

                try
                {
                    browser.LoadTest(loadableTestType, () =>
                    {
                        if (testTimedOut) return;
                        Scheduler.Add(takeScreenshot(testName, advanceToNext));
                    });
                }
                catch
                {
                    advanceToNext();
                    return;
                }

                Scheduler.AddDelayed(() =>
                {
                    if (!testTimedOut && loadableTestType != null && browser.CurrentTest?.GetType() == loadableTestType)
                    {
                        testTimedOut = true;
                        takeScreenshot(testName, advanceToNext).Invoke();
                    }
                }, test_timeout);
            }
            else
            {
                Scheduler.Add(takeScreenshot(testName, advanceToNext));
            }
        }

        private void advanceToNext()
        {
            testIndex++;
            Scheduler.AddDelayed(runNext, time_between_tests);
        }

        private Action takeScreenshot(string testName, Action onCompletion)
        {
            return () =>
            {
                if (host.Window == null)
                {
                    onCompletion?.Invoke();
                    return;
                }

                host.TakeScreenshotAsync().ContinueWith(t =>
                {
                    try
                    {
                        using (var image = t.GetAwaiter().GetResult())
                        {
                            string path = Path.Combine(SCREENSHOT_DIR, $"{testName}.png");
                            using (var stream = File.Create(path))
                                image.SaveAsPng(stream);
                        }
                    }
                    catch { }

                    Scheduler.Add(() => onCompletion?.Invoke());
                });
            };
        }
    }
}
