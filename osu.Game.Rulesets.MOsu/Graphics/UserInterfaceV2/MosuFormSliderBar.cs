// MOsu's FormSliderBar, mirroring the delta fork's extension surface.
//
// The delta fork added CommitEmptyAsNaN to the core FormSliderBar; the NuGet version
// does not have it. All current toolbox usages pass false (empty commit resets to
// default, which is the stock behaviour), so the property is accepted but inert.

using System.Numerics;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Rulesets.MOsu.Graphics.UserInterfaceV2
{
    public class MosuFormSliderBar<T> : FormSliderBar<T>
        where T : struct, INumber<T>, IMinMaxValue<T>
    {
        /// <summary>
        /// Whether committing empty text should set the value to <c>NaN</c> (for float/double sliders).
        /// If <see langword="false"/>, empty commit resets to the default value (stock behaviour).
        /// </summary>
        public bool CommitEmptyAsNaN { get; init; }
    }
}
