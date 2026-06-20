using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

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
        public void Post(Notification notification) { }
        public void Hide() { }
        public IBindable<int> UnreadCount { get; } = new Bindable<int>(0);
        public bool HasOngoingOperations => false;
        public IEnumerable<Notification> AllNotifications => Array.Empty<Notification>();
    }

    public partial class ScreenshotTestRunner : CompositeDrawable
    {
        private const double time_between_tests = 500;
        private const double test_timeout = 15000;

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

            int totalTests = filteredTestTypes.Sum(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(m => m.GetCustomAttribute<TestAttribute>() != null && m.Name != nameof(osu.Framework.Testing.TestScene.TestConstructor)));
            Console.WriteLine($"[ScreenshotTestRunner] {filteredTestTypes.Count} test types, {totalTests} test methods");
        }

        [Resolved]
        private GameHost host { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            AddInternal(browser);
            Directory.CreateDirectory(SCREENSHOT_DIR);
            host.ExceptionThrown += e =>
            {
                Console.WriteLine($"[ScreenshotTestRunner] Exception caught: {e.Message}");
                return true;
            };
            Scheduler.AddDelayed(runNext, 1000);
        }

        private void runNext()
        {
            var testType = loadableTestType;
            if (testType == null)
            {
                Console.WriteLine("[ScreenshotTestRunner] All tests complete.");
                Scheduler.AddDelayed(host.Exit, time_between_tests);
                return;
            }

            string testName = testType.Name;
            Console.WriteLine($"[ScreenshotTestRunner] Running: {testName} ({testIndex + 1}/{filteredTestTypes.Count})");

            testTimedOut = false;

            // Force non-interactive mode via reflection to bypass stop condition
            var interactiveField = browser.GetType().GetField("interactive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            interactiveField?.SetValue(browser, false);

            browser.LoadTest(testType, () =>
            {
                if (testTimedOut) return;
                Console.WriteLine($"[ScreenshotTestRunner] Completed: {testName}");
                Scheduler.Add(takeScreenshot(testName, advanceToNext));
            });

            Scheduler.AddDelayed(() =>
            {
                if (!testTimedOut)
                {
                    testTimedOut = true;
                    Console.WriteLine($"[ScreenshotTestRunner] Timeout for {testName}");
                    takeScreenshot(testName, advanceToNext).Invoke();
                }
            }, test_timeout);
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
                        var image = t.GetAwaiter().GetResult();
                        string path = Path.Combine(SCREENSHOT_DIR, $"{testName}.png");
                        using (image)
                        using (var stream = File.Create(path))
                            image.SaveAsPng(stream);
                        Console.WriteLine($"[ScreenshotTestRunner] Saved: {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ScreenshotTestRunner] Screenshot failed for {testName}: {ex.Message}");
                    }

                    Scheduler.Add(() => onCompletion?.Invoke());
                });
            };
        }
    }
}
