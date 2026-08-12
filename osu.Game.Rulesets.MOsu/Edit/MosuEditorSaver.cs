// Saves the editor beatmap to its .osu file, including the delta gimmick sections.
//
// The core save path (BeatmapManager.save -> LegacyBeatmapEncoder) rejects non-legacy
// rulesets and cannot write the gimmick sections, so MOsu performs its own write:
// encode the standard sections with the legacy encoder (presented as osu-mode), append
// the gimmick sections, then store the file in the realm file store and update hashes.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using Realms;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public static class MosuEditorSaver
    {
        public static bool Save(EditorBeatmap editorBeatmap, RealmAccess realm, Storage storage)
        {
            if (editorBeatmap.PlayableBeatmap is not MosuBeatmap playable)
            {
                Logger.Log("[MOsu] save skipped: playable beatmap is not a MosuBeatmap");
                return false;
            }

            var info = playable.BeatmapInfo;

            // The legacy encoder only accepts rulesets with an OnlineID of 0-3 and writes the
            // beatmap as that mode; the objects are stock osu objects, so present as osu.
            var originalRuleset = info.Ruleset;
            string text;

            try
            {
                info.Ruleset = new RulesetInfo { OnlineID = 0, ShortName = "osu" };

                using var writer = new StringWriter();
                new LegacyBeatmapEncoder(playable, editorBeatmap.BeatmapSkin, editorBeatmap.Storyboard).Encode(writer);
                text = writer.ToString();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Mosu save encode failed");
                return false;
            }
            finally
            {
                info.Ruleset = originalRuleset;
            }

            // The stock encoder flattens per-object slider velocity into fresh DifficultyControlPoints
            // whose bindables still cap at 10x, so SV > 10 is silently clamped on save. Rewrite the
            // encoded [TimingPoints] SV values from the playable's true state.
            text = rewriteSliderVelocityTimingPoints(text, playable);

            text += MosuGimmickSerializer.Serialize(playable.Gimmicks.Sections, playable.Gimmicks.HitObjectGimmicks);

            Logger.Log($"[MOsu] save writing {playable.Gimmicks.HitObjectGimmicks.Entries.Count} hitobject gimmick entries");

            byte[] data = Encoding.UTF8.GetBytes(text);
            string filename = $"{info.Metadata.Artist} - {info.Metadata.Title} ({info.Metadata.Author.Username}) [{info.DifficultyName}].osu".GetValidFilename();

            try
            {
                realm.Write(r =>
                {
                    var live = r.Find<BeatmapInfo>(info.ID);
                    var set = live?.BeatmapSet;

                    if (live == null || set == null)
                    {
                        Logger.Log("[MOsu] save skipped: beatmap info not found in realm");
                        return;
                    }

                    var fileStore = new RealmFileStore(realm, storage);
                    var realmFile = fileStore.Add(new MemoryStream(data), r);

                    var existing = set.Files.FirstOrDefault(f => f.Filename == live.Path);
                    if (existing != null)
                        set.Files.Remove(existing);

                    set.Files.Add(new RealmNamedFileUsage(realmFile, filename));

                    // Path is computed as BeatmapSet.Files.First(f => f.File.Hash == Hash),
                    // so updating the hashes below re-points the beatmap at the new file.
                    live.MD5Hash = new MemoryStream(data).ComputeMD5Hash();
                    live.Hash = new MemoryStream(data).ComputeSHA2Hash();
                    live.LastLocalUpdate = DateTimeOffset.Now;
                    live.Status = BeatmapOnlineStatus.LocallyModified;
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, "Mosu save failed");
                return false;
            }

            Logger.Log($"[MOsu] saved {filename} ({data.Length} bytes, {playable.Gimmicks.Sections.Sections.Count} sections, {playable.Gimmicks.HitObjectGimmicks.Entries.Count} hitobject gimmicks)");
            return true;
        }

        /// <summary>
        /// Corrects the encoded [TimingPoints] slider velocities to the playable's true values.
        /// The stock encoder creates fresh DifficultyControlPoints (MaxValue 10) when flattening
        /// per-object SV, silently capping SV > 10 on save; delta removes the cap in core, mosu
        /// rewrites the output instead.
        /// </summary>
        private static string rewriteSliderVelocityTimingPoints(string text, MosuBeatmap playable)
        {
            var trueSliderVelocities = buildTrueSliderVelocityLookup(playable);

            if (trueSliderVelocities.Count == 0)
                return text;

            var sb = new StringBuilder();
            bool inTimingPoints = false;

            foreach (var rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    sb.AppendLine(line);
                    continue;
                }

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inTimingPoints = trimmed == "[TimingPoints]";
                    sb.AppendLine(line);
                    continue;
                }

                if (!inTimingPoints)
                {
                    sb.AppendLine(line);
                    continue;
                }

                string[] fields = line.Split(',');

                if (fields.Length >= 2
                    && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                    && double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength)
                    && beatLength < 0
                    && trueSliderVelocities.TryGetValue(time, out double trueSv)
                    && trueSv > 0)
                {
                    fields[1] = (-100 / trueSv).ToString(CultureInfo.InvariantCulture);
                    sb.AppendLine(string.Join(',', fields));
                }
                else
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }

        /// <summary>
        /// The true slider velocity at each relevant time: per-object SV on sliders (which the
        /// encoder flattens into timing points) plus the playable's own difficulty control points.
        /// </summary>
        private static Dictionary<double, double> buildTrueSliderVelocityLookup(MosuBeatmap playable)
        {
            var lookup = new Dictionary<double, double>();

            foreach (var slider in playable.HitObjects.OfType<Slider>())
            {
                if (Math.Abs(slider.SliderVelocityMultiplier - 1) > 0.0001)
                    lookup[slider.StartTime] = slider.SliderVelocityMultiplier;
            }

            foreach (var controlPoint in playable.ControlPointInfo.AllControlPoints.OfType<DifficultyControlPoint>())
            {
                if (Math.Abs(controlPoint.SliderVelocity - 1) > 0.0001)
                    lookup[controlPoint.Time] = controlPoint.SliderVelocity;
            }

            return lookup;
        }
    }
}
