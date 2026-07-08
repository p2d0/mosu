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
using osu.Framework.Threading;
using osu.Framework.Testing;
using System.Threading;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.Runtime.Versioning;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class AutomatedVisualTestGame : OsuGameBase
    {
        private readonly string? testFilter;
        private DependencyContainer dependencies = null!;

        public AutomatedVisualTestGame(string? filter = null) => testFilter = filter;

        protected override Storage CreateStorage(GameHost host, Storage defaultStorage)
            => new TemporaryNativeStorage($"visual-test-{Guid.NewGuid()}");

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs<INotificationOverlay>(new StubNotificationOverlay());
            return dependencies;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Add(new ScreenshotTestRunner(new TestBrowser(), testFilter));
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
        private const double test_timeout = 60000;

        private readonly TestBrowser browser;
        private int testIndex;
        private bool testTimedOut;
        private readonly List<Type> filteredTestTypes;
        private readonly string? filter;

        private Type loadableTestType => testIndex >= 0 ? filteredTestTypes.ElementAtOrDefault(testIndex) : null;

        private static readonly string SCREENSHOT_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "screenshots"));

        public ScreenshotTestRunner(TestBrowser browser, string? filter = null)
        {
            this.browser = browser;
            this.filter = filter;
            filteredTestTypes = browser.TestTypes
                .Where(t => !typeof(PlayerTestScene).IsAssignableFrom(t)
                         && !typeof(Player).IsAssignableFrom(t)
                         && !typeof(ModTestScene).IsAssignableFrom(t))
                .Where(t => t.Name != "TestSceneOsuGame")
                .Where(t => filter == null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int totalTests = filteredTestTypes.Sum(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(m => m.GetCustomAttribute<TestAttribute>() != null && m.Name != nameof(osu.Framework.Testing.TestScene.TestConstructor)));
            if (filter != null)
                Console.WriteLine($"[ScreenshotTestRunner] Filter: {filter} \u2192 {filteredTestTypes.Count} test types, {totalTests} test methods");
            else
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
            var timeoutDelegate = new ScheduledDelegate(() =>
            {
                if (!testTimedOut)
                {
                    testTimedOut = true;
                    Console.WriteLine($"[ScreenshotTestRunner] Timeout for {testName}");
                    takeScreenshotImmediate(testName);
                    Scheduler.AddDelayed(advanceToNext, time_between_tests);
                }
            }, test_timeout);
            Scheduler.Add(timeoutDelegate);

            // Force non-interactive mode via reflection to bypass stop condition
            var interactiveField = browser.GetType().GetField("interactive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            interactiveField?.SetValue(browser, false);

            browser.LoadTest(testType, () =>
            {
                timeoutDelegate.Cancel();
                if (testTimedOut) return;
                Console.WriteLine($"[ScreenshotTestRunner] Completed: {testName}");
                Scheduler.AddDelayed(advanceToNext, time_between_tests);
            });
        }

        private void takeScreenshotImmediate(string testName)
        {
            if (host.Window == null)
            {
                Console.WriteLine($"[ScreenshotTestRunner] No window for {testName}");
                return;
            }

            try
            {
                var image = host.TakeScreenshotAsync().Result;
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
