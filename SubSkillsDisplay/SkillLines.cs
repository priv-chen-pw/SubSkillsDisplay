using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace SubSkillsDisplay
{
    /// <summary>
    /// A line of skills under a title, one per skill type (Primary, Secondary...) as in the Loadout tab. It holds
    /// one row per loadout skill that turns into something, stacked (CHEF's Sear and Ice Box).
    /// </summary>
    internal sealed class SkillLine
    {
        internal readonly string titleToken;
        internal readonly List<List<SkillEntry>> rows = new List<List<SkillEntry>>();

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
        /// Extra skills of <paramref name="body"/> grouped in lines: Primary, Secondary, Utility and Special (see
        /// <see cref="AddTurningInto"/>), then the other lines (a loadout row's title, or Misc) in the order they
        /// come up.
        /// </summary>
        internal static List<SkillLine> FromExtras(SurvivorLoadout body, IEnumerable<ExtraSkill> extras)
        {
            List<SkillLine> lines = new List<SkillLine>();
            foreach (IGrouping<string, ExtraSkill> group in extras.GroupBy(extra => extra.LineKey).OrderBy(group => Order(group.First().slot)))
            {
                ExtraSkill first = group.First();
                SkillLine line = new SkillLine(first.slot != SkillSlot.None ? TitleFor(first.slot) : first.lineToken ?? MiscTitleToken);
                line.AddTurningInto(body, first.slot, group);
                lines.Add(line);
            }

            return lines;
        }

        /// <summary>
        /// Adds a row for each loadout skill <paramref name="extras"/> replace in <paramref name="slot"/>: the
        /// skill, an arrow, and the extras it turns into. Extras of no slot replace nothing, so they are added alone.
        /// </summary>
        internal void AddTurningInto(SurvivorLoadout body, SkillSlot slot, IEnumerable<ExtraSkill> extras)
        {
            foreach (IGrouping<SkillDef, ExtraSkill> group in extras.GroupBy(extra => extra.replaces ? extra.replaces : body.Selected(slot)))
            {
                List<SkillEntry> row = new List<SkillEntry>();
                if (group.Key)
                {
                    row.Add(SkillEntry.Of(group.Key));
                    row.Add(SkillEntry.Arrow);
                }

                row.AddRange(group.Select(SkillEntry.Of));
                rows.Add(row);
            }
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
        /// <summary>
        /// A Loadout button's size. The lines are drawn at this size, or smaller ones as a whole (see
        /// <see cref="FitTile"/>), with the gaps and the arrow shrinking along.
        /// </summary>
        internal const float Tile = 76f;

        /// <summary>The smallest tile <see cref="FitTile"/> shrinks to; lines that still don't fit run over.</summary>
        private const float MinTile = 32f;

        /// <summary>A Loadout row's height is its button plus this (96).</summary>
        private const float LinePadding = 20f;

        /// <summary>Left of the buttons is the line's title (the Loadout row's padding: 128 left, 8 right).</summary>
        private const float TitleWidth = 136f;

        /// <summary>Between buttons, as in a Loadout row.</summary>
        private const float ButtonSpacing = 16f;

        /// <summary>Between the rows of one line.</summary>
        private const float RowSpacing = 12f;

        private const string ArrowAddress = "RoR2/Base/UI/texBasicArrowRight.png";

        /// <summary>The arrow's width; its sprite is 38×62, so it is drawn about 40 px high.</summary>
        private const float ArrowWidth = 24f;

        private static Sprite arrow;
        private static bool arrowLoaded;

        /// <summary>A line's height with buttons of size <paramref name="tile"/>.</summary>
        internal static float LineHeight(SkillLine line, float tile) =>
            LinePadding + line.rows.Count * tile + Mathf.Max(0, line.rows.Count - 1) * RowSpacing * tile / Tile;

        /// <summary>The lines' height, <paramref name="spacing"/> apart.</summary>
        internal static float Height(ICollection<SkillLine> lines, float tile, float spacing) =>
            lines.Sum(line => LineHeight(line, tile)) + Mathf.Max(0, lines.Count - 1) * spacing;

        /// <summary>
        /// The largest tile, up to a Loadout button's, at which every row fits in <paramref name="width"/> and the
        /// lines, <paramref name="spacing"/> apart, in <paramref name="height"/>.
        /// </summary>
        internal static float FitTile(ICollection<SkillLine> lines, float width, float height, float spacing)
        {
            // Every size but the title's and the lines' padding scales with the tile.
            float tile = Tile;
            foreach (List<SkillEntry> row in lines.SelectMany(line => line.rows).Where(row => row.Count > 0))
            {
                float rowWidth = row.Sum(entry => entry.isArrow ? ArrowWidth : Tile) + Mathf.Max(0, row.Count - 1) * ButtonSpacing;
                tile = Mathf.Min(tile, Tile * (width - TitleWidth) / rowWidth);
            }

            float fixedHeight = lines.Count * LinePadding + Mathf.Max(0, lines.Count - 1) * spacing;
            float scaledHeight = Height(lines, Tile, spacing) - fixedHeight;
            if (scaledHeight > 0f)
            {
                tile = Mathf.Min(tile, Tile * (height - fixedHeight) / scaledHeight);
            }

            return Mathf.Clamp(tile, MinTile, Tile);
        }

        /// <param name="tile">The buttons' size, from <see cref="FitTile"/>.</param>
        /// <returns>The rows made, to destroy on the next rebuild.</returns>
        internal static List<GameObject> Build(RectTransform container, IEnumerable<SkillLine> lines, Color color,
            LanguageTextMeshController hoverText, UILayerKey requiredTopLayer, float tile)
        {
            float scale = tile / Tile;
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

                // Fixed, so lines that don't fit run over the panel rather than squeeze over each other.
                LayoutElement height = row.GetComponent<LayoutElement>();
                if (height)
                {
                    height.minHeight = height.preferredHeight = LineHeight(line, tile);
                }

                // The line's rows, stacked where the Loadout row has its buttons.
                Transform buttons = row.transform.Find("ButtonContainer") ?? row.transform;
                Transform stack = AddLayout<VerticalLayoutGroup>("Rows", buttons, RowSpacing * scale);
                foreach (List<SkillEntry> entries in line.rows)
                {
                    Transform rowButtons = AddLayout<HorizontalLayoutGroup>("Row", stack, ButtonSpacing * scale);
                    foreach (SkillEntry entry in entries)
                    {
                        if (entry.isArrow)
                        {
                            AddArrow(rowButtons, color, tile);
                        }
                        else
                        {
                            AddButton(buttonPrefab, rowButtons, entry, color, hoverText, requiredTopLayer, tile);
                        }
                    }
                }

                // Vanilla keeps the spacer after the buttons, so they sit at the start of the line.
                buttons.Find("Spacer")?.SetAsLastSibling();
            }

            return rows;
        }

        /// <summary>A layout, left-aligned, sizing its children to what they ask for.</summary>
        private static Transform AddLayout<T>(string name, Transform parent, float spacing) where T : HorizontalOrVerticalLayoutGroup
        {
            GameObject layoutObject = new GameObject(name, typeof(RectTransform));
            layoutObject.transform.SetParent(parent, worldPositionStays: false);

            T layout = layoutObject.AddComponent<T>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layoutObject.transform;
        }

        private static void AddButton(GameObject buttonPrefab, Transform parent, SkillEntry entry, Color color,
            LanguageTextMeshController hoverText, UILayerKey requiredTopLayer, float tile)
        {
            GameObject buttonObject = Object.Instantiate(buttonPrefab, parent, worldPositionStays: false);
            HGButton button = buttonObject.GetComponent<HGButton>();
            if (!button)
            {
                return;
            }

            // Exactly the tile. The prefab squares itself from its height, while the row squeezes its width when
            // short of room: buttons then kept their full width at squeezed spacing, overlapping.
            LayoutElement size = buttonObject.GetComponent<LayoutElement>();
            if (size)
            {
                size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = tile;
            }

            AspectRatioFitter square = buttonObject.GetComponent<AspectRatioFitter>();
            if (square)
            {
                square.enabled = false;
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

            // No floating tooltip: the description box beside the lines already shows the skill. The tooltip opens
            // from its own pointer and selection events, so removing it leaves only the box.
            TooltipProvider tooltip = buttonObject.GetComponent<TooltipProvider>();
            if (tooltip)
            {
                Object.Destroy(tooltip);
            }
        }

        /// <summary>The game's right arrow in the line's colour, between a skill and what it turns into.</summary>
        private static void AddArrow(Transform parent, Color color, float tile)
        {
            if (!arrowLoaded)
            {
                arrowLoaded = true;
                arrow = Addressables.LoadAssetAsync<Sprite>(ArrowAddress).WaitForCompletion();
                if (!arrow)
                {
                    Log.Warning($"Could not load {ArrowAddress}; skill lines are shown without arrows.");
                }
            }

            if (!arrow)
            {
                return;
            }

            GameObject arrowObject = new GameObject("Arrow", typeof(RectTransform));
            arrowObject.transform.SetParent(parent, worldPositionStays: false);

            Image image = arrowObject.AddComponent<Image>();
            image.sprite = arrow;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;

            // As high as the buttons: the sprite's own 62 px would make a row of small buttons taller.
            LayoutElement size = arrowObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = ArrowWidth * tile / Tile;
            size.minHeight = size.preferredHeight = tile;
        }
    }
}
