// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Runtime.CompilerServices;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Utils
{
    public static partial class MosuHitObjectGenerationUtils
    {
        /// <summary>
        /// Remembers the original positions (and slider path control points) of a beatmap's hit objects
        /// on first use, and restores them on subsequent uses.
        /// <para>
        /// Position-editing mods run <c>ApplyToBeatmap</c> repeatedly against the same objects (live
        /// re-application in gameplay, or across mod config changes). Without restoring first, each pass
        /// would compound on the previous pass's output instead of re-randomising from the true original.
        /// The store is intentionally per-mod-instance — <c>DeepClone</c>ed mod copies each get fresh tables.
        /// </para>
        /// </summary>
        public class OriginalHitObjectStateStore
        {
            private readonly ConditionalWeakTable<OsuHitObject, object> originalPositions = new();
            private readonly ConditionalWeakTable<Slider, object> originalSliderPaths = new();

            /// <summary>
            /// Restore every <see cref="OsuHitObject"/> in the beatmap to the position it had the first
            /// time this store saw it, then re-record the current state if this is the first sighting.
            /// </summary>
            public void Restore(OsuBeatmap osuBeatmap)
            {
                foreach (var obj in osuBeatmap.HitObjects.OfType<OsuHitObject>())
                {
                    if (!originalPositions.TryGetValue(obj, out var pos))
                    {
                        pos = obj.Position;
                        originalPositions.Add(obj, pos);
                    }
                    obj.Position = (Vector2)pos;

                    if (obj is Slider slider)
                    {
                        if (!originalSliderPaths.TryGetValue(slider, out var pathState))
                        {
                            pathState = slider.Path.ControlPoints.Select(p => p.Position).ToArray();
                            originalSliderPaths.Add(slider, pathState);
                        }
                        var pts = (Vector2[])pathState;
                        for (int j = 0; j < slider.Path.ControlPoints.Count && j < pts.Length; j++)
                            slider.Path.ControlPoints[j].Position = pts[j];
                    }
                }
            }
        }
    }
}
