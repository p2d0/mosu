// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class MOsuChatLine : osu.Game.Overlays.Chat.ChatLine, IHasContextMenu
    {
        public MOsuChatLine(Message message) : base(message) { }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private Bindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

        [Resolved(CanBeNull = true)]
        private Bindable<RulesetInfo>? currentRuleset { get; set; }

        [Resolved(CanBeNull = true)]
        private INotificationOverlay? notifications { get; set; }

        public MenuItem[] ContextMenuItems
        {
            get
            {
                if (selectedMods == null || currentRuleset == null)
                    return Array.Empty<MenuItem>();

                // Try extracting preset from invisible link in the message
                var preset = extractPresetFromLinks(Message.Links);
                if (preset != null)
                {
                    return new MenuItem[]
                    {
                        new OsuMenuItem("Apply Mod Preset", MenuItemType.Highlighted, () => applyPreset(preset))
                    };
                }

                // Fallback: try parsing JSON from message content for backward compat
                string content = Message.Content;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    content = content.Trim();
                    try
                    {
                        var presets = JsonConvert.DeserializeObject<List<PresetExportDto>>(content);
                        if (presets != null && presets.Count > 0)
                        {
                            return new MenuItem[]
                            {
                                new OsuMenuItem("Apply Mod Preset", MenuItemType.Highlighted, () => applyPreset(presets.First()))
                            };
                        }
                    }
                    catch
                    {
                    }
                }

                return Array.Empty<MenuItem>();
            }
        }

        private PresetExportDto? extractPresetFromLinks(List<Link> links)
        {
            var presetLink = links.FirstOrDefault(l => l.Url.StartsWith("osu://preset/"));
            if (presetLink == null) return null;

            string base64 = presetLink.Url["osu://preset/".Length..];
            try
            {
                byte[] data = Convert.FromBase64String(base64);
                string json;

                // Try gzip decompression first, fall back to plain text
                try
                {
                    using var ms = new MemoryStream(data);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz);
                    json = sr.ReadToEnd();
                }
                catch
                {
                    json = Encoding.UTF8.GetString(data);
                }

                var presets = JsonConvert.DeserializeObject<List<PresetExportDto>>(json);
                return presets?.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void applyPreset(PresetExportDto preset)
        {
            if (selectedMods == null || currentRuleset == null) return;

            if (preset.RulesetShortName != currentRuleset.Value.ShortName)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = $"Preset is for {preset.RulesetShortName}, but you are playing {currentRuleset.Value.ShortName}."
                });
                return;
            }

            try
            {
                var rulesetInstance = currentRuleset.Value.CreateInstance();
                var mods = preset.Mods.Select(m => m.ToMod(rulesetInstance)).ToList();

                selectedMods.Value = mods;

                notifications?.Post(new SimpleNotification
                {
                    Text = $"Applied preset: {preset.Name}"
                });
            }
            catch (Exception ex)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = $"Failed to apply preset: {ex.Message}"
                });
            }
        }

        private class PresetExportDto
        {
            public string Name { get; set; } = string.Empty;
            public string RulesetShortName { get; set; } = string.Empty;
            public List<APIMod> Mods { get; set; } = new List<APIMod>();
        }
    }
}
