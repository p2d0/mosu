using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Input.Bindings;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Chat;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.MOsu.Extensions;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class ChatOverlayInjector : Component
    {
        [Resolved]
        private OsuGame game { get; set; } = null!;

        private FieldInfo? chatOverlayField;
        private FieldInfo? overlayContentField;
        private FieldInfo? focusedOverlaysField;
        private FieldInfo? newsField;
        private FieldInfo? dashboardField;
        private FieldInfo? beatmapListingField;
        private FieldInfo? changelogOverlayField;
        private FieldInfo? rankingsOverlayField;
        private FieldInfo? wikiOverlayField;
        private FieldInfo? settingsField;
        private FieldInfo? notificationsField;

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
            newsField = getFieldInHierarchy(type, "news");
            dashboardField = getFieldInHierarchy(type, "dashboard");
            beatmapListingField = getFieldInHierarchy(type, "beatmapListing");
            changelogOverlayField = getFieldInHierarchy(type, "changelogOverlay");
            rankingsOverlayField = getFieldInHierarchy(type, "rankingsOverlay");
            wikiOverlayField = getFieldInHierarchy(type, "wikiOverlay");
            settingsField = getFieldInHierarchy(type, "settings");
            notificationsField = getFieldInHierarchy(type, "notifications");
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
            injectDashboardHideHandler();
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

        private OverlayContainer? getOverlay(FieldInfo? field)
        {
            return field?.GetValue(game) as OverlayContainer;
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

        private void injectDashboardHideHandler()
        {
            chatOverlay.State.ValueChanged += state =>
            {
                if (state.NewValue != Visibility.Hidden)
                {
                    getOverlay(newsField)?.Hide();
                    getOverlay(dashboardField)?.Hide();
                    getOverlay(beatmapListingField)?.Hide();
                    getOverlay(changelogOverlayField)?.Hide();
                    getOverlay(rankingsOverlayField)?.Hide();
                    getOverlay(wikiOverlayField)?.Hide();
                    getOverlay(settingsField)?.Hide();
                    getOverlay(notificationsField)?.Hide();
                }
            };
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
                if (child is osu.Game.Overlays.Chat.ChatLine chatLine && !(child is MOsuChatLine))
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

                // Check for new ChatLines
                foreach (var child in chatLineFlow.Children.ToArray())
                {
                    if (child is osu.Game.Overlays.Chat.ChatLine chatLine && !(child is MOsuChatLine))
                    {
                        var moChatLine = new MOsuChatLine(chatLine.Message);
                        moChatLine.Depth = chatLine.Depth;
                        moChatLine.Anchor = chatLine.Anchor;
                        moChatLine.Origin = chatLine.Origin;
                        chatLineFlow.Remove(child, true);
                        chatLineFlow.Add(moChatLine);
                        return; // One per frame to avoid issues
                    }
                }
            }
        }
    }
}
