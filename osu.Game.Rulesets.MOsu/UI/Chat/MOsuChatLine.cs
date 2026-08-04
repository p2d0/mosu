// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.MOsu.UI.Chat
{
    public partial class MOsuChatLine : osu.Game.Overlays.Chat.ChatLine, IHasContextMenu
    {
        public MOsuChatLine(Message message) : base(message) { }

        /// <summary>
        /// Whether a chat message contains mods (an <c>osu://preset/</c> link) and therefore
        /// benefits from being displayed as an <see cref="MOsuChatLine"/>.
        /// </summary>
        public static bool ContainsMods(Message message)
            => message.Links.Any(l => l.Url.StartsWith("osu://preset/"));

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

                var items = new List<MenuItem>();

                // Try extracting preset from invisible link in the message
                var preset = extractPresetFromLinks(Message.Links);
                if (preset != null)
                {
                    items.Add(new OsuMenuItem("Apply Mod Preset", MenuItemType.Highlighted, () => applyPreset(preset)));
                }

                return items.ToArray();
            }
        }

        private long? lastMessageId;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            lastMessageId = Message.Id;
            ScheduleAfterChildren(addModLinks);
        }

        protected override void Update()
        {
            base.Update();
            if (selectedMods != null && currentRuleset != null)
            {
                // Message ID changed means content was rebuilt (e.g., server response)
                if (Message.Id != lastMessageId)
                {
                    lastMessageId = Message.Id;
                    // Call synchronously since content is already built
                    addModLinks();
                }
            }
        }

        private void addModLinks()
        {
            if (selectedMods == null || currentRuleset == null) return;

            var contentField = typeof(osu.Game.Overlays.Chat.ChatLine).GetField("drawableContentFlow", BindingFlags.Instance | BindingFlags.NonPublic);
            var drawableContentFlow = contentField?.GetValue(this) as osu.Game.Graphics.Containers.LinkFlowContainer;
            if (drawableContentFlow == null) return;

            // The mods come from the preset link, not from parsing the display text.
            var preset = extractPresetFromLinks(Message.Links);
            if (preset == null)
                return;

            string modString = buildModDisplayString(preset);

            // Add mod string as clickable link at end of message, with a hover tooltip
            // describing the mods and their customizations (non-default settings).
            // The tooltip must never prevent the link from being added.
            string? tooltip = null;
            try
            {
                tooltip = buildModTooltip(preset);
            }
            catch
            {
                // tooltip failures must not hide the mod link
            }

            drawableContentFlow.AddText(" ");
            drawableContentFlow.AddLink($"[{modString}]", () => applyPreset(preset), tooltip);
        }

        /// <summary>
        /// Builds the displayed mod string with signs: <c>+</c> for plain mods, <c>-</c> for
        /// mods with customizations (non-default settings).
        /// </summary>
        private string buildModDisplayString(PresetExportDto preset)
        {
            if (currentRuleset == null || preset.Mods.Count == 0)
                return string.Join(" ", preset.Mods.Select(m => m.Acronym));

            try
            {
                var rulesetInstance = currentRuleset.Value.CreateInstance();
                return string.Join(" ", preset.Mods.Select(m =>
                {
                    var mod = m.ToMod(rulesetInstance);
                    return $"{(mod.SettingDescription.Any() ? "-" : "+")}{mod.Acronym}";
                }));
            }
            catch
            {
                return string.Join(" ", preset.Mods.Select(m => m.Acronym));
            }
        }

        /// <summary>
        /// Builds a hover tooltip describing the mods of a preset, including their
        /// customizations (non-default settings) like the main game's mod tooltip.
        /// </summary>
        private string? buildModTooltip(PresetExportDto preset)
        {
            if (currentRuleset == null || preset.Mods.Count == 0)
                return null;

            try
            {
                var rulesetInstance = currentRuleset.Value.CreateInstance();
                var lines = new List<string>();

                foreach (var apiMod in preset.Mods)
                {
                    try
                    {
                        var mod = apiMod.ToMod(rulesetInstance);
                        lines.Add($"{mod.Acronym} — {mod.Name}");

                        foreach (var (setting, value) in mod.SettingDescription)
                            lines.Add($"  {setting}: {value}");
                    }
                    catch
                    {
                        // skip mods that fail to resolve
                    }
                }

                return lines.Count == 0 ? null : string.Join("\n", lines);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds a hover tooltip describing the mods in a mod string, including their
        /// customizations (non-default settings) like the main game's mod tooltip.
        /// </summary>
        private PresetExportDto? extractPresetFromLinks(List<Link> links)
        {
            var presetLink = links.FirstOrDefault(l => l.Url.StartsWith("osu://preset/"));
            if (presetLink == null) return null;

            string base64 = presetLink.Url["osu://preset/".Length..];
            try
            {
                byte[] data = Convert.FromBase64String(base64);
                string json;

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

                var modString = string.Join(", ", mods.Select(m => m.Acronym));
                notifications?.Post(new SimpleNotification
                {
                    Text = $"Applied mods: {modString}"
                });
            }
            catch (Exception ex)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = $"Failed to apply mods: {ex.Message}"
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
