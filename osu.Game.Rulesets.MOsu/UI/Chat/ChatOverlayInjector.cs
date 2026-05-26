using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
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

        private ChatOverlay? newOverlay;
        private bool hasInjected;

        public ChatOverlayInjector()
        {
            // Ensure Update runs even if this component isn't visible
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

        protected override void Update()
        {
            base.Update();

            if (hasInjected) return;

            var overlayContent = overlayContentField?.GetValue(game) as Container;
            var toolbarContainer = game.GetToolbarContainer();

            // Wait until both containers are ready
            if (overlayContent == null || toolbarContainer == null) return;

            // 1. Inject Overlay (returns false if old overlay not ready yet)
            if (!injectOverlay(overlayContent))
                return; // Try again next frame

            // 2. Inject Toolbar Button (returns false if old button not ready yet)
            if (!injectButton(toolbarContainer))
                return; // Try again next frame

            hasInjected = true;
        }

        private bool injectOverlay(Container targetContainer)
        {
            // Retrieve the old overlay using the field
            var oldOverlay = chatOverlayField?.GetValue(game) as OverlayContainer;
            var parent = oldOverlay?.Parent as Container;

            // Check if injection already happened by looking for our custom ChatOverlay in the actual parent
            if (parent != null && parent.Children.OfType<ChatOverlay>().Any())
            {
                newOverlay = parent.Children.OfType<ChatOverlay>().First();
                return true;
            }

            // Wait until the old overlay exists and has been added to the container
            if (oldOverlay == null || parent == null)
                return false;

            // Remove the old overlay and remember its position
            var childrenAfter = new List<Drawable>();
            if (oldOverlay != null)
            {
                if (parent != null)
                {
                    int oldIndex = parent.IndexOf(oldOverlay);

                    // Actually remove oldOverlay from parent (Expire alone doesn't remove it)
                    parent.Remove(oldOverlay, false);

                    // Remove children that were after oldOverlay
                    for (int i = parent.Children.Count - 1; i >= oldIndex; i--)
                    {
                        var child = parent.Children[i];
                        parent.Remove(child, false);
                        childrenAfter.Add(child);
                    }
                }

                // Also remove it from the focusedOverlays list
                if (focusedOverlaysField != null)
                {
                    var list = focusedOverlaysField.GetValue(game) as IList;
                    list?.Remove(oldOverlay);
                }
            }

            // Instantiate and add the new MOsu ChatOverlay at the old position (in the SAME parent)
            newOverlay = new ChatOverlay
            {
                Depth = oldOverlay.Depth,
            };
            (parent ?? targetContainer).Add(newOverlay);

            // Re-add children that were after the old overlay
            if (parent != null)
            {
                foreach (var child in childrenAfter.AsEnumerable().Reverse())
                    parent.Add(child);
            }

            // Update the chatOverlay field so OsuGame uses our custom overlay
            chatOverlayField?.SetValue(game, newOverlay);

            // Register the new overlay in focusedOverlays so it handles ESC properly
            if (focusedOverlaysField != null)
            {
                var list = focusedOverlaysField.GetValue(game) as IList;
                list?.Add(newOverlay);
            }

            return true;
        }

        private bool injectButton(FillFlowContainer toolbarContainer)
        {
            // Find the existing ToolbarChatButton
            var oldButton = toolbarContainer.Children.OfType<ToolbarChatButton>().FirstOrDefault();

            // Wait until the old button exists
            if (oldButton == null)
                return false;

            // Already injected?
            if (toolbarContainer.Children.OfType<MOsuToolbarChatButton>().Any())
                return true;

            // Assign explicit layout positions to all children to preserve current order
            var children = toolbarContainer.Children.ToArray();
            for (int i = 0; i < children.Length; i++)
                toolbarContainer.SetLayoutPosition(children[i], i);

            int index = toolbarContainer.IndexOf(oldButton);
            toolbarContainer.Remove(oldButton, true);

            // Create and add our custom button that controls the new overlay
            var newButton = new MOsuToolbarChatButton(newOverlay!);
            toolbarContainer.Add(newButton);
            toolbarContainer.SetLayoutPosition(newButton, index);

            return true;
        }

        private partial class MOsuToolbarChatButton : ToolbarOverlayToggleButton
        {
            protected override Anchor TooltipAnchor => Anchor.TopRight;

            public MOsuToolbarChatButton(ChatOverlay chatOverlay)
            {
                Hotkey = GlobalAction.ToggleChat;
                StateContainer = chatOverlay;
            }
        }
    }
}
