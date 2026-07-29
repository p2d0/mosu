// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

        private static string? extractModString(string content)
        {
            // Look for patterns like "+HD HR DT" or "+HDHRDT" or "+HD+HR-DT"
            // Match sequences starting with + or - followed by mod acronyms
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(content, @"(?:[+\-][A-Z]{2,4}(?:\s*[+\-]?[A-Z]{2,4})*)");
            return match.Success ? match.Value : null;
        }

        private void applyModString(string modString)
        {
            if (selectedMods == null || currentRuleset == null) return;

            var rulesetInstance = currentRuleset.Value.CreateInstance();
            var newMods = new List<Mod>();
            var errors = new List<string>();

            // Parse mod string: split on spaces and +/-
            string[] tokens = modString.Split(new[] { ' ', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var modType = rulesetInstance.AllMods.FirstOrDefault(m => m.Acronym.Equals(token, StringComparison.OrdinalIgnoreCase));
                if (modType != null)
                {
                    try
                    {
                        newMods.Add(modType.CreateInstance());
                    }
                    catch
                    {
                        errors.Add($"{token} failed");
                    }
                }
                else
                {
                    errors.Add($"{token} not found");
                }
            }

            if (newMods.Count > 0)
            {
                // Check compatibility
                var incompatible = newMods.Where(m1 => newMods.Any(m2 => m1 != m2 && m1.IncompatibleMods.Contains(m2.GetType()))).Select(m => m.Acronym).Distinct().ToList();
                if (incompatible.Count > 0)
                {
                    notifications?.Post(new SimpleErrorNotification
                    {
                        Text = $"Incompatible mods: {string.Join(", ", incompatible)}"
                    });
                }
                else
                {
                    selectedMods.Value = newMods.ToArray();
                    notifications?.Post(new SimpleNotification
                    {
                        Text = $"Applied mods: {string.Join(", ", newMods.Select(m => m.Acronym))}"
                    });
                }
            }

            if (errors.Count > 0)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = $"Unknown mods: {string.Join(", ", errors)}"
                });
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
                var modString = extractModString(Message.DisplayContent);
                if (!string.IsNullOrEmpty(modString))
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
        }

        private void addModLinks()
        {
            if (selectedMods == null || currentRuleset == null) return;

            var contentField = typeof(osu.Game.Overlays.Chat.ChatLine).GetField("drawableContentFlow", BindingFlags.Instance | BindingFlags.NonPublic);
            var drawableContentFlow = contentField?.GetValue(this) as osu.Game.Graphics.Containers.LinkFlowContainer;
            if (drawableContentFlow == null) return;

            var modString = extractModString(Message.DisplayContent);
            if (string.IsNullOrEmpty(modString)) return;

            // Check if there's a preset link with settings
            var preset = extractPresetFromLinks(Message.Links);

            // Add mod string as clickable link at end of message
            drawableContentFlow.AddText(" ");
            if (preset != null)
            {
                drawableContentFlow.AddLink($"[{modString}]", () => applyPreset(preset));
            }
            else
            {
                drawableContentFlow.AddLink($"[{modString}]", () => applyModString(modString));
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
