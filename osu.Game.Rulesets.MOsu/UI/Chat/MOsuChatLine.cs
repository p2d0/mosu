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

        [Resolved(CanBeNull = true)]
        private RulesetStore? rulesetStore { get; set; }

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

            drawableContentFlow.AddText(" ");
            drawableContentFlow.AddLink($"[{modString}]", () => applyPreset(preset), buildModTooltip(preset));
        }

        /// <summary>
        /// Resolves a preset's mods into mod instances for the preset's own ruleset (empty on failure).
        /// </summary>
        private List<Mod> resolvePresetMods(PresetExportDto preset)
        {
            if (preset.Mods.Count == 0)
                return new List<Mod>();

            var rulesetInstance = getPresetRulesetInstance(preset);
            if (rulesetInstance == null)
                return new List<Mod>();

            var mods = new List<Mod>();

            foreach (var apiMod in preset.Mods)
            {
                try
                {
                    var mod = apiMod.ToMod(rulesetInstance);
                    // unknown mods (no ruleset knows the acronym) are skipped; display falls back to raw acronyms
                    if (mod is not UnknownMod)
                        mods.Add(mod);
                }
                catch
                {
                    // skip mods that fail to resolve
                }
            }

            return mods;
        }

        /// <summary>
        /// Creates a ruleset instance from the preset's encoded ruleset, falling back to the current one.
        /// </summary>
        private Ruleset? getPresetRulesetInstance(PresetExportDto preset)
        {
            var info = rulesetStore?.GetRuleset(preset.RulesetShortName) ?? currentRuleset?.Value;
            return info?.CreateInstance();
        }

        /// <summary>
        /// Builds the displayed mod string with signs: <c>+</c> for plain mods, <c>-</c> for
        /// mods with customizations (non-default settings).
        /// </summary>
        private string buildModDisplayString(PresetExportDto preset)
        {
            var mods = resolvePresetMods(preset);
            return mods.Count == 0
                ? string.Join(" ", preset.Mods.Select(m => m.Acronym))
                : string.Join(" ", mods.Select(m => $"{(m.SettingDescription.Any() ? "-" : "+")}{m.Acronym}"));
        }

        /// <summary>
        /// Builds a hover tooltip describing the mods of a preset, including their
        /// customizations (non-default settings) like the main game's mod tooltip.
        /// </summary>
        private string? buildModTooltip(PresetExportDto preset)
        {
            var mods = resolvePresetMods(preset);
            if (mods.Count == 0)
                return null;

            var lines = new List<string>();

            foreach (var mod in mods)
            {
                lines.Add($"{mod.Acronym} — {mod.Name}");

                foreach (var (setting, value) in mod.SettingDescription)
                    lines.Add($"  {setting}: {value}");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Decodes a mod preset from the <c>osu://preset/</c> link in a chat message.
        /// </summary>
        private PresetExportDto? extractPresetFromLinks(List<Link> links)
        {
            var presetLink = links.FirstOrDefault(l => l.Url.StartsWith("osu://preset/"));
            if (presetLink == null) return null;

            try
            {
                byte[] data = Convert.FromBase64String(presetLink.Url["osu://preset/".Length..]);

                using var ms = new MemoryStream(data);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var sr = new StreamReader(gz);

                var presets = JsonConvert.DeserializeObject<List<PresetExportDto>>(sr.ReadToEnd());
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

            // Switch to the preset's ruleset instead of refusing to apply it.
            if (preset.RulesetShortName != currentRuleset.Value.ShortName)
            {
                var rulesetInfo = rulesetStore?.GetRuleset(preset.RulesetShortName);
                if (rulesetInfo == null)
                    return;

                currentRuleset.Value = rulesetInfo;
            }

            var mods = resolvePresetMods(preset);
            if (mods.Count == 0)
            {
                notifications?.Post(new SimpleErrorNotification { Text = "Failed to resolve preset mods." });
                return;
            }

            selectedMods.Value = mods;
            notifications?.Post(new SimpleNotification
            {
                Text = $"Applied mods: {string.Join(", ", mods.Select(m => m.Acronym))}"
            });
        }

        private class PresetExportDto
        {
            public string Name { get; set; } = string.Empty;
            public string RulesetShortName { get; set; } = string.Empty;
            public List<APIMod> Mods { get; set; } = new List<APIMod>();
        }
    }
}
