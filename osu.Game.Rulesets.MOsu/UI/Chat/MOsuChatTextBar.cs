// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.Chat;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class MOsuChatTextBar : osu.Game.Overlays.Chat.ChatTextBar, IHasContextMenu
    {
        public event Action<string>? OnModsCommand;

        protected override void LoadComplete()
        {
            // Hook into chatTextBox.OnCommit to intercept /mods and /md
            var chatTextBoxField = typeof(osu.Game.Overlays.Chat.ChatTextBar).GetField("chatTextBox", BindingFlags.Instance | BindingFlags.NonPublic);
            var chatTextBox = chatTextBoxField?.GetValue(this) as osu.Game.Overlays.Chat.ChatTextBox;

            if (chatTextBox != null)
            {
                chatTextBox.OnCommit += (sender, _) =>
                {
                    if (sender.Text == "/mods" || sender.Text == "/md")
                    {
                        OnModsCommand?.Invoke(sender.Text);
                        sender.Text = string.Empty;
                    }
                };
            }

            base.LoadComplete();
        }

        [Resolved(CanBeNull = true)]
        private Bindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

        [Resolved(CanBeNull = true)]
        private Bindable<RulesetInfo>? currentRuleset { get; set; }

        [Resolved]
        private ChannelManager? channelManager { get; set; }

        public MenuItem[] ContextMenuItems
        {
            get
            {
                if (selectedMods == null || currentRuleset == null || channelManager == null)
                    return Array.Empty<MenuItem>();

                return new MenuItem[]
                {
                    new OsuMenuItem("Share Current Mods", MenuItemType.Standard, sendCurrentMods)
                };
            }
        }

        public void sendCurrentMods()
        {
            if (selectedMods == null || currentRuleset == null || channelManager == null) return;

            var mods = selectedMods.Value.Where(m => m.Type != ModType.System).ToList();

            if (mods.Count == 0) return;

            var preset = new PresetExportDto
            {
                Name = "My Mods",
                RulesetShortName = currentRuleset.Value.ShortName,
                Mods = mods.Select(m => new APIMod(m)).ToList()
            };

            string json = JsonConvert.SerializeObject(new List<PresetExportDto> { preset }, Formatting.None);
            string base64;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
                using (var sw = new StreamWriter(gz))
                    sw.Write(json);
                base64 = Convert.ToBase64String(ms.ToArray());
            }

            channelManager.PostMessage($"is playing <{currentRuleset.Value.Name}> with [\u200B](osu://preset/{base64})");
        }

        private class PresetExportDto
        {
            public string Name { get; set; } = string.Empty;
            public string RulesetShortName { get; set; } = string.Empty;
            public List<APIMod> Mods { get; set; } = new List<APIMod>();
        }
    }
}
