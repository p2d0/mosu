using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Online.Chat;
using osu.Game.Overlays.Chat;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class ChatOverlayInjector : Component
    {
        [Resolved]
        private OsuGame game { get; set; } = null!;

        private FieldInfo? chatOverlayField;
        private FieldInfo? overlayContentField;
        private FieldInfo? focusedOverlaysField;

        private osu.Game.Overlays.ChatOverlay? chatOverlay;
        private bool hasInjected;

        public ChatOverlayInjector()
        {
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var type = game.GetType();
            chatOverlayField = getFieldInHierarchy(type, "chatOverlay");
            overlayContentField = getFieldInHierarchy(type, "overlayContent");
            focusedOverlaysField = getFieldInHierarchy(type, "focusedOverlays");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Schedule(PollAndInject);
        }

        private void PollAndInject()
        {
            if (hasInjected) return;

            chatOverlay = chatOverlayField?.GetValue(game) as osu.Game.Overlays.ChatOverlay;

            if (chatOverlay == null || !chatOverlay.IsLoaded)
            {
                Schedule(PollAndInject);
                return;
            }

            injectTextBar();
            injectMOsuChatLine();

            hasInjected = true;
        }

        private static FieldInfo? getFieldInHierarchy(System.Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return field;
                type = type.BaseType!;
            }
            return null;
        }

        private void injectTextBar()
        {
            var textBarField = typeof(osu.Game.Overlays.ChatOverlay).GetField("textBar", BindingFlags.Instance | BindingFlags.NonPublic);
            var oldTextBar = textBarField?.GetValue(chatOverlay) as ChatTextBar;
            if (oldTextBar == null || oldTextBar.Parent == null) return;

            var parent = oldTextBar.Parent as Container;
            if (parent == null) return;

            var newTextBar = new MOsuChatTextBar
            {
                RelativeSizeAxes = Axes.X,
            };

            // Wire up message handling via reflection to handleChatMessage
            var handleChatMessageMethod = typeof(osu.Game.Overlays.ChatOverlay).GetMethod("handleChatMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (handleChatMessageMethod != null)
            {
                newTextBar.OnChatMessageCommitted += (message) =>
                {
                    handleChatMessageMethod.Invoke(chatOverlay, new object[] { message });
                };
            }

            // Intercept /mods and /md commands
            newTextBar.OnModsCommand += (message) =>
            {
                newTextBar.sendCurrentMods();
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

            parent.Remove(oldTextBar, false);
            parent.Add(wrapper);
            textBarField?.SetValue(chatOverlay, newTextBar);
        }

        private void injectMOsuChatLine()
        {
            var loadedChannelsField = typeof(osu.Game.Overlays.ChatOverlay).GetField("loadedChannels", BindingFlags.Instance | BindingFlags.NonPublic);
            var loadedChannels = loadedChannelsField?.GetValue(chatOverlay) as Dictionary<Channel, osu.Game.Overlays.Chat.DrawableChannel>;
            if (loadedChannels == null) return;

            foreach (var drawableChannel in loadedChannels.Values)
            {
                hookChatLineFlow(drawableChannel);
            }

            // Also hook new channels as they're loaded
            var currentChannelField = typeof(osu.Game.Overlays.ChatOverlay).GetField("currentChannel", BindingFlags.Instance | BindingFlags.NonPublic);
            var currentChannelBindable = currentChannelField?.GetValue(chatOverlay) as Bindable<Channel?>;
            if (currentChannelBindable != null)
            {
                currentChannelBindable.ValueChanged += _ =>
                {
                    foreach (var drawableChannel in loadedChannels.Values)
                    {
                        hookChatLineFlow(drawableChannel);
                    }
                };
            }
        }

        private void hookChatLineFlow(osu.Game.Overlays.Chat.DrawableChannel channel)
        {
            var chatLineFlowField = typeof(osu.Game.Overlays.Chat.DrawableChannel).GetField("ChatLineFlow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var chatLineFlow = chatLineFlowField?.GetValue(channel) as FillFlowContainer;
            if (chatLineFlow == null) return;

            // Replace existing ChatLines
            replaceChatLines(chatLineFlow);
        }

        private void replaceChatLines(FillFlowContainer flow)
        {
            foreach (var child in flow.Children.ToArray())
            {
                if (child is osu.Game.Overlays.Chat.ChatLine chatLine && !(child is MOsuChatLine) && MOsuChatLine.ContainsMods(chatLine.Message))
                {
                    var moChatLine = new MOsuChatLine(chatLine.Message);
                    moChatLine.Depth = chatLine.Depth;
                    moChatLine.Anchor = chatLine.Anchor;
                    moChatLine.Origin = chatLine.Origin;
                    flow.Remove(child, true);
                    flow.Add(moChatLine);
                }
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!hasInjected || chatOverlay == null) return;

            var loadedChannelsField = typeof(osu.Game.Overlays.ChatOverlay).GetField("loadedChannels", BindingFlags.Instance | BindingFlags.NonPublic);
            var loadedChannels = loadedChannelsField?.GetValue(chatOverlay) as Dictionary<Channel, osu.Game.Overlays.Chat.DrawableChannel>;
            if (loadedChannels == null) return;

            foreach (var drawableChannel in loadedChannels.Values)
            {
                var chatLineFlowField = typeof(osu.Game.Overlays.Chat.DrawableChannel).GetField("ChatLineFlow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var chatLineFlow = chatLineFlowField?.GetValue(drawableChannel) as FillFlowContainer;
                if (chatLineFlow == null) continue;

                // Collect replacements with their indices
                var children = chatLineFlow.Children.ToArray();
                var replacements = new List<(int index, osu.Game.Overlays.Chat.ChatLine old, MOsuChatLine @new)>();
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child is osu.Game.Overlays.Chat.ChatLine chatLine && !(child is MOsuChatLine) && MOsuChatLine.ContainsMods(chatLine.Message))
                    {
                        var moChatLine = new MOsuChatLine(chatLine.Message);
                        moChatLine.Depth = chatLine.Depth;
                        moChatLine.Anchor = chatLine.Anchor;
                        moChatLine.Origin = chatLine.Origin;
                        replacements.Add((i, chatLine, moChatLine));
                    }
                }

                if (replacements.Count == 0) continue;

                // Clear and rebuild to preserve order
                var allChildren = chatLineFlow.Children.ToArray();
                chatLineFlow.Clear(false);

                foreach (var child in allChildren)
                {
                    if (child is osu.Game.Overlays.Chat.ChatLine oldLine && replacements.Any(r => r.old == oldLine))
                    {
                        chatLineFlow.Add(replacements.First(r => r.old == oldLine).@new);
                    }
                    else
                    {
                        chatLineFlow.Add(child);
                    }
                }
            }
        }
    }
}
