using System;
using System.IO;
using osu.Framework.Platform;
using SixLabors.ImageSharp;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public static class ScreenshotHelper
    {
        public static readonly string SCREENSHOT_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "screenshots"));

        /// <summary>
        /// Synchronous screenshot capture with explicit name.
        /// </summary>
        public static void Capture(GameHost host, string name)
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
                    image.SaveAsPng(stream);

                Console.WriteLine($"[ScreenshotHelper] Saved: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScreenshotHelper] Screenshot failed: {ex.Message}");
            }
        }
    }
}
