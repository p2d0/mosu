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
        private const double test_timeout = 120000;

        private readonly TestBrowser browser;
        private int testIndex;
        private bool testTimedOut;
        private readonly List<Type> filteredTestTypes;
        private readonly string? filter;

        private Type? loadableTestType => testIndex >= 0 ? filteredTestTypes.ElementAtOrDefault(testIndex) : null;

        // state for method-level filtering (runs a single [Test] method without the browser running the whole scene)
        private List<MethodInfo>? currentMethods;
        private int methodIndex;
        private TestScene? activeScene;

        private static readonly string SCREENSHOT_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "screenshots"));

        public ScreenshotTestRunner(TestBrowser browser, string? filter = null)
        {
            this.browser = browser;
            this.filter = filter;

            // Always run at 2x playback (same knob as the toolbar's Rate slider) so automated runs finish faster.
            var playbackRateField = typeof(TestBrowser).GetField("PlaybackRate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (playbackRateField?.GetValue(browser) is BindableDouble playbackRate)
                playbackRate.Value = 2;

            filteredTestTypes = browser.TestTypes
                .Where(t => !typeof(PlayerTestScene).IsAssignableFrom(t)
                         && !typeof(Player).IsAssignableFrom(t)
                         && !typeof(ModTestScene).IsAssignableFrom(t))
                .Where(t => t.Name != "TestSceneOsuGame")
                .Where(t => filter == null || matches(t, filter))
                .ToList();

            int totalTests = filteredTestTypes.Sum(t => getTestMethods(t).Count());
            if (filter != null)
                Console.WriteLine($"[ScreenshotTestRunner] Filter: {filter} \u2192 {filteredTestTypes.Count} test types, {totalTests} test methods");
            else
                Console.WriteLine($"[ScreenshotTestRunner] {filteredTestTypes.Count} test types, {totalTests} test methods");
        }

        /// <summary>
        /// A type matches the filter if the filter matches its name (file-level) or any of its test method names (testcase-level).
        /// </summary>
        private static bool matches(Type type, string filter)
        {
            if (type.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;

            return getTestMethods(type).Any(m => methodMatches(m, filter));
        }

        private static bool methodMatches(MethodInfo method, string filter)
        {
            if (method.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;

            // the browser displays method names without the "Test" prefix, so allow matching that form too.
            if (method.Name.StartsWith("Test", StringComparison.Ordinal))
                return method.Name.Substring(4).Contains(filter, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static IEnumerable<MethodInfo> getTestMethods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null
                     && m.Name != nameof(TestScene.TestConstructor)
                     && m.GetCustomAttribute<IgnoreAttribute>() == null);

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

            var matchingMethods = filter == null ? null : getTestMethods(testType).Where(m => methodMatches(m, filter)).ToList();

            if (matchingMethods != null && matchingMethods.Count > 0)
            {
                currentMethods = matchingMethods;
                methodIndex = 0;
                runSingleMethod(testType);
            }
            else
                runSceneViaBrowser(testType);
        }

        /// <summary>
        /// Run the whole test scene through the browser (all of its test methods).
        /// </summary>
        private void runSceneViaBrowser(Type testType)
        {
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

        /// <summary>
        /// Run a single [Test] method of the scene without the browser (which would otherwise run every method in the file).
        /// </summary>
        private void runSingleMethod(Type testType)
        {
            if (currentMethods == null || methodIndex >= currentMethods.Count)
            {
                advanceToNext();
                return;
            }

            var method = currentMethods[methodIndex];
            string testName = $"{testType.Name}.{method.Name}";
            Console.WriteLine($"[ScreenshotTestRunner] Running: {testName} ({testIndex + 1}/{filteredTestTypes.Count})");

            testTimedOut = false;
            var timeoutDelegate = new ScheduledDelegate(() =>
            {
                if (!testTimedOut)
                {
                    testTimedOut = true;
                    Console.WriteLine($"[ScreenshotTestRunner] Timeout for {testName}");
                    takeScreenshotImmediate(testType.Name);
                    finishCurrentMethod();
                }
            }, test_timeout);
            Scheduler.Add(timeoutDelegate);

            var scene = (TestScene)Activator.CreateInstance(testType);
            activeScene = scene;
            scene.OnLoadComplete += _ => Scheduler.Add(() =>
            {
                if (testTimedOut)
                {
                    finishCurrentMethod();
                    return;
                }

                try
                {
                    // replicate TestBrowser.finishLoad: run setup steps, then the test method, then teardown steps.
                    invokeAttributed(scene, typeof(SetUpStepsAttribute));
                    invokeAttributed(scene, typeof(SetUpAttribute));
                    method.Invoke(scene, null);
                    invokeAttributed(scene, typeof(TearDownStepsAttribute));

                    scene.RunAllSteps(
                        () => Scheduler.Add(() =>
                        {
                            timeoutDelegate.Cancel();
                            if (testTimedOut) return;
                            Console.WriteLine($"[ScreenshotTestRunner] Completed: {testName}");
                            Scheduler.AddDelayed(finishCurrentMethod, time_between_tests);
                        }),
                        (s, e) => Console.WriteLine($"[ScreenshotTestRunner] Step error in {testName}: {e.Message}"),
                        null);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[ScreenshotTestRunner] Failed {testName}: {e.Message}");
                    timeoutDelegate.Cancel();
                    Scheduler.AddDelayed(finishCurrentMethod, time_between_tests);
                }
            });

            AddInternal(scene);
        }

        private void finishCurrentMethod()
        {
            if (activeScene?.Parent != null)
            {
                RemoveInternal(activeScene, true);
                activeScene = null;
            }

            if (currentMethods != null && ++methodIndex < currentMethods.Count && loadableTestType != null)
                runSingleMethod(loadableTestType);
            else
            {
                currentMethods = null;
                advanceToNext();
            }
        }

        private static void invokeAttributed(TestScene scene, Type attributeType)
        {
            foreach (var m in scene.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                    .Where(m => m.GetCustomAttribute(attributeType, true) != null))
                m.Invoke(scene, null);
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
