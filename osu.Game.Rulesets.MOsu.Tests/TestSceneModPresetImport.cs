using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Tests.Visual;
using osu.Framework.Testing;
using Realms;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneModPresetImport : OsuTestScene
    {
        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        protected override bool UseFreshStoragePerRun => true;

        private TestNotificationOverlay notifications = null!;
        private ModPresetImportProcessor processor = null!;

        private const string valid_presets_json = """
            [
                { "Name": "HD", "Description": "Hidden", "ModsJson": "[{\"Acronym\":\"HD\"}]" },
                { "Name": "DT", "Description": "Double Time", "ModsJson": "[{\"Acronym\":\"DT\"}]" },
                { "Name": "HR", "Description": "", "ModsJson": "" }
            ]
            """;

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(Realm);

            // Ensure the mosu ruleset exists in realm, as ModPresetImportProcessor imports presets against it.
            Realm.Write(r =>
            {
                if (r.Find<RulesetInfo>("mosu") == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = "mosu" });
            });

            notifications = new TestNotificationOverlay();
            processor = new ModPresetImportProcessor(Realm, notifications, action => action());
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("clear presets", () =>
            {
                Realm.Write(r => r.RemoveAll<ModPreset>());
                notifications.Posted.Clear();
            });
            AddStep("ensure mosu ruleset", () =>
            {
                // Re-seed in case a previous test removed it (storage persists across tests in the fixture).
                Realm.Write(r =>
                {
                    if (r.Find<RulesetInfo>("mosu") == null)
                        r.Add(new RulesetInfo { OnlineID = 0, ShortName = "mosu" });
                });
            });
        }

        private int presetCount() => Realm.Run(r => r.All<ModPreset>().Count());

        [Test]
        public void TestImportValidPresets()
        {
            int imported = 0;
            AddStep("import valid presets", () =>
                processor.Import(valid_presets_json, count => imported = count));

            AddAssert("three presets imported", () => imported == 3);
            AddAssert("presets exist in realm", () => presetCount() == 3);
            AddAssert("preset fields preserved", () =>
            {
                var preset = Realm.Run(r => r.All<ModPreset>().FirstOrDefault(p => p.Name == "HD"));
                return preset != null
                    && preset.Description == "Hidden"
                    && preset.ModsJson.Contains("HD")
                    && preset.Ruleset.ShortName == "mosu";
            });
            AddAssert("success notification posted", () =>
                notifications.Posted.OfType<SimpleNotification>().Any(n => n.Text.ToString().Contains("3 presets")));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_Valid"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportBrokenJson()
        {
            AddStep("import broken json", () =>
                processor.Import("{ this is not valid json !!!"));

            AddAssert("no presets imported", () => presetCount() == 0);
            AddAssert("friendly error notification posted", () =>
                notifications.Posted.OfType<SimpleErrorNotification>().Any(n => n.Text.ToString().Contains("Invalid file")));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_BrokenJson"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportEmptyJson()
        {
            AddStep("import empty array", () => processor.Import("[]"));

            AddAssert("no presets imported", () => presetCount() == 0);
            AddAssert("no-presets notification posted", () =>
                notifications.Posted.OfType<SimpleErrorNotification>().Any(n => n.Text.ToString().Contains("No presets")));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_Empty"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportNullJson()
        {
            AddStep("import null json", () => processor.Import(null!));

            AddAssert("no presets imported", () => presetCount() == 0);
            AddAssert("error notification posted", () =>
                notifications.Posted.OfType<SimpleErrorNotification>().Any(n => n.Text.ToString().Contains("Failed to import")));
        }

        [Test]
        public void TestImportDuplicatesSkipped()
        {
            int firstImported = 0;
            int secondImported = 0;

            AddStep("import once", () => processor.Import(valid_presets_json, count => firstImported = count));
            AddAssert("three imported first time", () => firstImported == 3);

            AddStep("import again", () => processor.Import(valid_presets_json, count => secondImported = count));
            AddAssert("zero imported second time", () => secondImported == 0);
            AddAssert("preset count unchanged", () => presetCount() == 3);
            AddAssert("duplicates notification posted", () =>
                notifications.Posted.OfType<SimpleNotification>().Any(n => n.Text.ToString().Contains("duplicates")));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_Duplicates"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportObjectInsteadOfArray()
        {
            // Valid JSON but wrong shape: a single object, not an array. Deserializing into List<T> throws.
            AddStep("import single object json", () =>
                processor.Import("{ \"Name\": \"HD\", \"Description\": \"Hidden\", \"ModsJson\": \"[]\" }"));

            AddAssert("no presets imported", () => presetCount() == 0);
            AddAssert("friendly error notification posted", () =>
                notifications.Posted.OfType<SimpleErrorNotification>().Any(n => n.Text.ToString().Contains("Invalid file")));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_ObjectInsteadOfArray"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportMissingRuleset()
        {
            AddStep("remove mosu ruleset", () =>
            {
                Realm.Write(r =>
                {
                    var ruleset = r.Find<RulesetInfo>("mosu");
                    if (ruleset != null)
                        r.Remove(ruleset);
                });
            });

            AddStep("import presets", () => processor.Import(valid_presets_json));
            AddAssert("no presets imported", () => presetCount() == 0);
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_MissingRuleset"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportPresetWithRulesetField()
        {
            AddStep("ensure osu ruleset", () =>
            {
                Realm.Write(r =>
                {
                    if (r.Find<RulesetInfo>("osu") == null)
                        r.Add(new RulesetInfo { OnlineID = 0, ShortName = "osu" });
                });
            });

            AddStep("import osu-tagged preset", () => processor.Import("""
                [
                    { "Name": "NM1", "Description": "osu preset", "ModsJson": "[]", "RulesetShortName": "osu" },
                    { "Name": "NM2", "Description": "mosu preset", "ModsJson": "[]" }
                ]
                """));

            AddAssert("preset lands under its own ruleset", () =>
            {
                var osuPreset = Realm.Run(r => r.All<ModPreset>().FirstOrDefault(p => p.Name == "NM1"));
                var mosuPreset = Realm.Run(r => r.All<ModPreset>().FirstOrDefault(p => p.Name == "NM2"));
                return osuPreset?.Ruleset.ShortName == "osu" && mosuPreset?.Ruleset.ShortName == "mosu";
            });
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_RulesetField"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestImportPresetWithUnknownRulesetSkipped()
        {
            AddStep("import mania-tagged preset", () => processor.Import("""
                [
                    { "Name": "MANIA1", "Description": "", "ModsJson": "[]", "RulesetShortName": "mania" },
                    { "Name": "KEEP1", "Description": "", "ModsJson": "[]" }
                ]
                """));

            AddAssert("unknown-ruleset preset skipped, mosu preset kept", () =>
            {
                var maniaPreset = Realm.Run(r => r.All<ModPreset>().FirstOrDefault(p => p.Name == "MANIA1"));
                var keptPreset = Realm.Run(r => r.All<ModPreset>().FirstOrDefault(p => p.Name == "KEEP1"));
                return maniaPreset == null && keptPreset?.Ruleset.ShortName == "mosu";
            });
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "ModPresetImport_UnknownRulesetSkipped"));
            AddWaitStep("wait for screenshot", 1);
        }
    }

    /// <summary>
    /// Minimal <see cref="INotificationOverlay"/> recording posted notifications, since test scenes don't provide one.
    /// </summary>
    public class TestNotificationOverlay : INotificationOverlay
    {
        public List<Notification> Posted { get; } = new List<Notification>();

        public void Post(Notification notification) => Posted.Add(notification);

        public void Hide()
        {
        }

        public IBindable<int> UnreadCount { get; } = new Bindable<int>();

        public IEnumerable<Notification> AllNotifications => Posted;
    }
}
