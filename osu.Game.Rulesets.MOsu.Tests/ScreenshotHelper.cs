using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Platform;
using SixLabors.ImageSharp;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public static class ScreenshotHelper
    {
        public static readonly string SCREENSHOT_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "screenshots"));

        public static void Capture(GameHost host)
        {
            if (host == null)
            {
                TestContext.WriteLine("[ScreenshotHelper] No GameHost available, skipping screenshot.");
                return;
            }

            if (host.Window == null)
            {
                TestContext.WriteLine("[ScreenshotHelper] No window (headless mode), skipping screenshot.");
                return;
            }

            try
            {
                var image = host.TakeScreenshotAsync().Result;

                var test = TestContext.CurrentContext.Test;
                string testName = test?.Name?.Replace(".", "_") ?? "unknown";
                string fixtureName = test?.ClassName?.Replace("osu.Game.Rulesets.MOsu.Tests.", "").Replace(".", "_") ?? "unknown";

                Directory.CreateDirectory(SCREENSHOT_DIR);

                string path = Path.Combine(SCREENSHOT_DIR, $"{fixtureName}_{testName}.png");

                using (image)
                using (var stream = File.Create(path))
                {
                    image.SaveAsPng(stream);
                }

                TestContext.WriteLine($"[ScreenshotHelper] Saved: {path}");
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"[ScreenshotHelper] Screenshot failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void CaptureNamed(GameHost host, string name)
        {
            if (host == null || host.Window == null)
                return;

            host.TakeScreenshotAsync().ContinueWith(t =>
            {
                try
                {
                    var image = t.GetAwaiter().GetResult();

                    Directory.CreateDirectory(SCREENSHOT_DIR);

                    string path = Path.Combine(SCREENSHOT_DIR, $"{name}.png");

                    using (image)
                    using (var stream = File.Create(path))
                    {
                        image.SaveAsPng(stream);
                    }
                }
                catch { }
            });
        }

        /// <summary>
        /// Synchronous screenshot capture that waits for completion.
        /// Use this for step-level screenshots where timing matters.
        /// </summary>
        public static void CaptureSync(GameHost host, string name)
        {
            if (host == null || host.Window == null)
                return;

            try
            {
                var image = host.TakeScreenshotAsync().Result;
                Directory.CreateDirectory(SCREENSHOT_DIR);
                string path = Path.Combine(SCREENSHOT_DIR, $"{name}.png");
                using (image)
                using (var stream = File.Create(path))
                {
                    image.SaveAsPng(stream);
                }
            }
            catch { }
        }
    }
}
