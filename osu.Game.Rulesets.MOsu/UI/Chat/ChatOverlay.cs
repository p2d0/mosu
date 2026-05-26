// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Chat;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class ChatOverlay : osu.Game.Overlays.ChatOverlay
    {
        public new LocalisableString Title => "TESTINGO";

        protected override osu.Game.Overlays.Chat.DrawableChannel CreateDrawableChannel(Channel newChannel) => new MOsuDrawableChannel(newChannel);

        protected override void LoadComplete()
        {
            // Replace textBar before base.LoadComplete() wires up events
            var textBarField = typeof(osu.Game.Overlays.ChatOverlay).GetField("textBar", BindingFlags.Instance | BindingFlags.NonPublic);
            var oldTextBar = (ChatTextBar?)textBarField?.GetValue(this);

            if (oldTextBar != null)
            {
                var parent = oldTextBar.Parent as Container;
                int index = parent?.IndexOf(oldTextBar) ?? -1;

                var newTextBar = new MOsuChatTextBar
                {
                    RelativeSizeAxes = Axes.X,
                };

                var wrapper = new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = oldTextBar.Anchor,
                    Origin = oldTextBar.Origin,
                    Padding = oldTextBar.Padding,
                    Child = newTextBar,
                };

                parent?.Remove(oldTextBar, false);
                parent?.Add(wrapper);

                textBarField?.SetValue(this, newTextBar);
            }

            base.LoadComplete();
        }
    }
}
