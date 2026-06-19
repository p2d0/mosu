using System;
using System.Linq;
using osu.Framework;
using osu.Framework.Platform;
using osu.Game.Tests;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public static class VisualTestRunner
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Environment.SetEnvironmentVariable("OSU_DISABLE_ERROR_REPORTING", "1");

            bool auto = args.Contains("--auto");

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu"))
            {
                if (auto)
                    host.Run(new AutomatedVisualTestGame());
                else
                    host.Run(new OsuTestBrowser());
                return 0;
            }
        }
    }
}
