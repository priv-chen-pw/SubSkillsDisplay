using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SubSkillsDisplay
{
    /// <summary>A line of skills under a title, one per skill type (Primary, Secondary...) as in the Loadout tab.</summary>
    internal sealed class SkillLine
    {
        internal readonly string titleToken;
        internal readonly List<SkillEntry> entries = new List<SkillEntry>();

        internal SkillLine(string titleToken)
        {
            this.titleToken = titleToken;
        }

        internal const string MiscTitleToken = "LOADOUT_SKILL_MISC";

        /// <summary>The Loadout tab's title for a skill type.</summary>
        internal static string TitleFor(SkillSlot slot)
        {
            switch (slot)
            {
                case SkillSlot.Primary: return "LOADOUT_SKILL_PRIMARY";
                case SkillSlot.Secondary: return "LOADOUT_SKILL_SECONDARY";
                case SkillSlot.Utility: return "LOADOUT_SKILL_UTILITY";
                case SkillSlot.Special: return "LOADOUT_SKILL_SPECIAL";
                default: return MiscTitleToken;
            }
        }

        /// <summary>
        /// Extra skills grouped in lines: Primary, Secondary, Utility and Special, then the other lines (a loadout
        /// row's title, or Misc) in the order they come up.
        /// </summary>
        internal static List<SkillLine> FromExtras(IEnumerable<ExtraSkill> extras)
        {
            List<SkillLine> lines = new List<SkillLine>();
            foreach (IGrouping<string, ExtraSkill> group in extras.GroupBy(extra => extra.LineKey).OrderBy(group => Order(group.First().slot)))
            {
                ExtraSkill first = group.First();
                SkillLine line = new SkillLine(first.slot != SkillSlot.None ? TitleFor(first.slot) : first.lineToken ?? MiscTitleToken);
                line.entries.AddRange(group.Select(SkillEntry.Of));
                lines.Add(line);
            }

            return lines;
        }

        private static int Order(SkillSlot slot) => slot == SkillSlot.None ? 4 : (int)slot;
    }

    /// <summary>
    /// Builds <see cref="SkillLine"/>s with the Loadout tab's own row and button prefabs, so they look like its
    /// lines, minus what makes those choosable: no selected highlight, every icon at full brightness, and clicks
    /// do nothing. Hovering an icon writes its text into a description box, as the Loadout tab does.
    /// </summary>
    internal static class SkillLines
    {
        /// <summary>Height of a Loadout row.</summary>
        internal const float LineHeight = 96f;

        /// <returns>The rows made, to destroy on the next rebuild.</returns>
        internal static List<GameObject> Build(RectTransform container, IEnumerable<SkillLine> lines, Color color,
            LanguageTextMeshController hoverText, UILayerKey requiredTopLayer)
        {
            List<GameObject> rows = new List<GameObject>();
            GameObject rowPrefab = LoadoutPanelController.rowPrefab;
            GameObject buttonPrefab = LoadoutPanelController.loadoutButtonPrefab;
            if (!rowPrefab || !buttonPrefab)
            {
                Log.Warning("The Loadout tab's row prefabs aren't loaded; skill lines can't be shown.");
                return rows;
            }

            foreach (SkillLine line in lines)
            {
                GameObject row = Object.Instantiate(rowPrefab, container, worldPositionStays: false);
                row.name = "SkillLine";
                rows.Add(row);

                Transform highlight = row.transform.Find("ButtonSelectionHighlight, Checkbox");
                if (highlight)
                {
                    Object.Destroy(highlight.gameObject);
                }

                Transform label = row.transform.Find("SlotLabel");
                if (label)
                {
                    LanguageTextMeshController labelText = label.GetComponent<LanguageTextMeshController>();
                    if (labelText)
                    {
                        labelText.token = line.titleToken;
                    }

                    HGTextMeshProUGUI labelGraphic = label.GetComponent<HGTextMeshProUGUI>();
                    if (labelGraphic)
                    {
                        labelGraphic.color = color;
                    }
                }

                Transform buttons = row.transform.Find("ButtonContainer") ?? row.transform;
                foreach (SkillEntry entry in line.entries)
                {
                    AddButton(buttonPrefab, buttons, entry, color, hoverText, requiredTopLayer);
                }

                // Vanilla keeps the spacer after the buttons, so they sit at the start of the line.
                buttons.Find("Spacer")?.SetAsLastSibling();
            }

            return rows;
        }

        private static void AddButton(GameObject buttonPrefab, Transform parent, SkillEntry entry, Color color,
            LanguageTextMeshController hoverText, UILayerKey requiredTopLayer)
        {
            GameObject buttonObject = Object.Instantiate(buttonPrefab, parent, worldPositionStays: false);
            HGButton button = buttonObject.GetComponent<HGButton>();
            if (!button)
            {
                return;
            }

            if (button.targetGraphic is Image image)
            {
                image.sprite = entry.icon;
            }

            button.updateTextOnHover = true;
            button.hoverLanguageTextMeshController = hoverText;
            button.hoverToken = entry.HoverText(color);
            button.requiredTopLayer = requiredTopLayer;

            // Hoverable and selectable with a gamepad, but nothing to pick.
            button.interactable = true;
            button.disablePointerClick = true;
            button.disableGamepadClick = true;
            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TooltipProvider tooltip = buttonObject.GetComponent<TooltipProvider>();
            if (tooltip)
            {
                tooltip.titleColor = color;
                tooltip.overrideTitleText = entry.Title;
                tooltip.overrideBodyText = entry.Description;
            }
        }
    }
}
