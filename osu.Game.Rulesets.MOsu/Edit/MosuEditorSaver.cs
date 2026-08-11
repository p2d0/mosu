// Saves the editor beatmap to its .osu file, including the delta gimmick sections.
//
// The core save path (BeatmapManager.save -> LegacyBeatmapEncoder) rejects non-legacy
// rulesets and cannot write the gimmick sections, so MOsu performs its own write:
// encode the standard sections with the legacy encoder (presented as osu-mode), append
// the gimmick sections, then store the file in the realm file store and update hashes.

using System;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
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
    }
}
