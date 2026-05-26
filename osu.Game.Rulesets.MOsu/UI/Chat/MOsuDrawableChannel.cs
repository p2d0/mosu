// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.Chat;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class MOsuDrawableChannel : osu.Game.Overlays.Chat.DrawableChannel
    {
        public MOsuDrawableChannel(Channel channel) : base(channel) { }

        protected override osu.Game.Overlays.Chat.ChatLine CreateChatLine(Message m) => new MOsuChatLine(m);
    }
}
