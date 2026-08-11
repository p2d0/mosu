// Post-import ruleset reassignment: the legacy .osu/.osz/.olz format can only express
// the four built-in rulesets (Mode: 0-3), so a custom ruleset beatmap cannot be imported
// as such through the regular flow. MOsu re-imports gimmicked maps under its own ruleset
// by rewriting the realm beatmap info after import.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Gimmicks;
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    public static class MosuImportUtils
    {
        /// <summary>
        /// Whether the given .olz/.osz/.osu file contains any delta gimmick sections.
        /// </summary>
        public static bool FileHasGimmicks(string path)
        {
            try
            {
                if (path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = File.OpenRead(path);
                    using var reader = new StreamReader(stream);
                    return hasGimmicks(reader);
                }

                using var archive = ZipFile.OpenRead(path);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    if (hasGimmicks(reader))
                        return true;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu] Failed to scan {path} for gimmicks: {e.Message}");
            }

            return false;
        }

        private static bool hasGimmicks(TextReader reader)
        {
            var (sections, hitObjectGimmicks) = MosuGimmickParser.Parse(reader);
            return sections.Sections.Count > 0 || hitObjectGimmicks.Entries.Count > 0;
        }

        /// <summary>
        /// Reassigns every beatmap in the given imported sets to the mosu ruleset.
        /// </summary>
        public static void AssignMosuRuleset(RealmAccess realm, IEnumerable<Live<BeatmapSetInfo>> importedSets)
        {
            var sets = importedSets.ToList();
            if (sets.Count == 0)
                return;

            realm.Write(r =>
            {
                var mosuRuleset = r.Find<RulesetInfo>(new MosuRuleset().ShortName);
                if (mosuRuleset == null)
                {
                    Logger.Log("[MOsu] Could not find mosu ruleset in realm for import reassignment.");
                    return;
                }

                foreach (var set in sets)
                {
                    foreach (var beatmap in set.Value.Beatmaps)
                        beatmap.Ruleset = mosuRuleset;

                    Logger.Log($"[MOsu] Assigned mosu ruleset to {set.Value.Beatmaps.Count} beatmaps in {set.Value}.");
                }
            });
        }
    }
}
