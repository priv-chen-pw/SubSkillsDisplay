using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using RoR2.Skills;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Adds "Abilities" to the pause menu during a run, after Inventory. It opens a panel with your character's
    /// skills in lines per skill type (Passive, Primary, Secondary, Utility, Special), each skill followed by
    /// what it turns into with your loadout, built like the Loadout tab's lines. Hovering one shows its
    /// description and keywords. Like Settings it is a submenu of the pause screen: Back, or Cancel on a gamepad,
    /// returns to the pause menu; Escape closes the pause screen as it does from Settings.
    /// </summary>
    internal static class PauseAbilitiesPanel
    {
        private const string CharacterSelectAddress = "RoR2/Base/UI/CharacterSelectUIMain.prefab";
        private const string PanelName = "SubSkillsDisplayAbilitiesPanel";

        private const float ColumnWidth = 640f;

        /// <summary>The description box beside the lines, a little narrower than vanilla's 600 px.</summary>
        private const float HoverBoxWidth = 520f;

        private const float BackButtonWidth = 320f;
        private const float BackButtonHeight = 48f;

        /// <summary>Above the pause screen (1000), as vanilla's Settings submenu (1005), so Cancel goes back here.</summary>
        private const int LayerPriority = 1005;

        private static CharacterSelectController template;
        private static UILayer layer;

        internal static void Init()
        {
            On.RoR2.UI.PauseScreenController.Awake += (orig, self) =>
            {
                orig(self);
                AddButton(self);
            };

            // Runs after Awake each time the pause screen opens; vanilla hides Inventory here outside a run.
            On.RoR2.UI.PauseScreenController.OnEnable += (orig, self) =>
            {
                orig(self);
                AbilitiesButton button = self.GetComponent<AbilitiesButton>();
                if (button && button.buttonObject)
                {
                    button.buttonObject.SetActive(Run.instance && Run.instance.isRunning && self.localUser?.cachedMaster);
                }
            };
        }

        /// <summary>Remembers the pause screen's Abilities button.</summary>
        private class AbilitiesButton : MonoBehaviour
        {
            internal GameObject buttonObject;
        }

        private static void AddButton(PauseScreenController controller)
        {
            GameObject inventory = controller.inventoryButtonObject;
            if (!inventory)
            {
                Log.Warning("The pause menu has no Inventory button to put Abilities after; skipping it.");
                return;
            }

            ModTokens.Register();

            GameObject buttonObject = Object.Instantiate(inventory, inventory.transform.parent, worldPositionStays: false);
            buttonObject.name = "GenericMenuButton (Abilities)";
            buttonObject.transform.SetSiblingIndex(inventory.transform.GetSiblingIndex() + 1);

            MPButton button = buttonObject.GetComponent<MPButton>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Open(controller));

            LanguageTextMeshController label = buttonObject.GetComponent<LanguageTextMeshController>();
            if (label)
            {
                label.token = ModTokens.PauseAbilities;
            }

            controller.gameObject.AddComponent<AbilitiesButton>().buttonObject = buttonObject;
        }

        private static void Open(PauseScreenController controller)
        {
            LocalUser localUser = controller.localUser;
            CharacterMaster master = localUser?.cachedMaster;
            if (!master)
            {
                return;
            }

            // The living body holds the run's loadout; while dead, the profile's loadout is the same one.
            CharacterBody liveBody = master.GetBody();
            Loadout loadout = null;
            SurvivorLoadout body;
            BodyIndex bodyIndex;
            if (liveBody)
            {
                body = SurvivorLoadout.FromBody(liveBody, localUser.userProfile);
                bodyIndex = liveBody.bodyIndex;
            }
            else
            {
                bodyIndex = master.bodyPrefab ? BodyCatalog.FindBodyIndex(master.bodyPrefab) : BodyIndex.None;
                loadout = Loadout.RequestInstance();
                localUser.userProfile?.CopyLoadout(loadout);
                body = SurvivorLoadout.FromLoadout(bodyIndex, loadout, localUser.userProfile);
            }

            try
            {
                List<SkillLine> lines = Lines(body, ExtraSkillCatalog.For(body, includeOtherLoadouts: false));

                // The prefab's name and colour: a body in another form (the Anima Harvester) renames itself.
                CharacterBody prefabBody = BodyCatalog.GetBodyPrefabBodyComponent(bodyIndex);
                string title = prefabBody ? Language.GetString(prefabBody.baseNameToken) : string.Empty;
                Color color = prefabBody ? prefabBody.bodyColor : Color.white;

                GameObject panel = Build(controller, title, color, lines);
                if (!panel)
                {
                    return;
                }

                Object.Destroy(controller.submenuObject);
                controller.submenuObject = panel;
                controller.pausePanel.gameObject.SetActive(false);
                controller.gameMenuPanel.gameObject.SetActive(true);
                controller.mainPanel.gameObject.SetActive(false);
                panel.SetActive(true);

                // The layer key looks up its event system in Awake; make sure it found one.
                UILayerKey key = panel.GetComponent<UILayerKey>();
                if (!key._eventSystem)
                {
                    key._eventSystem = panel.GetComponent<MPEventSystemLocator>().eventSystem;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Could not open the Abilities panel: {e}");

                // Don't leave a half-built panel behind.
                Transform leftover = controller.rootPanel.Find(PanelName);
                if (leftover && leftover.gameObject != controller.submenuObject)
                {
                    Object.Destroy(leftover.gameObject);
                }
            }
            finally
            {
                if (loadout != null)
                {
                    Loadout.ReturnInstance(loadout);
                }
            }
        }

        /// <summary>
        /// The passive line, then Primary to Special (the loadout's skill, then what it turns into), then the other
        /// lines of extra skills (Operator's drones, modded skills of unknown type).
        /// </summary>
        private static List<SkillLine> Lines(SurvivorLoadout body, List<ExtraSkill> extras)
        {
            List<SkillLine> lines = new List<SkillLine>();
            if (!body.IsValid)
            {
                return lines;
            }

            SkillLine passive = new SkillLine(ModTokens.PassiveLine);
            if (body.skillLocator.passiveSkill.enabled)
            {
                passive.entries.Add(SkillEntry.Of(body.skillLocator.passiveSkill));
            }

            passive.entries.AddRange(body.slots
                .Where(slot => !slot.hideInCharacterSelect && body.KindOf(slot) == SkillSlot.None)
                .Select(body.Selected)
                .Where(skill => skill)
                .Select(SkillEntry.Of));
            if (passive.entries.Count > 0)
            {
                lines.Add(passive);
            }

            foreach (SkillSlot kind in new[] { SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Utility, SkillSlot.Special })
            {
                SkillLine line = new SkillLine(SkillLine.TitleFor(kind));
                SkillDef skill = body.Selected(kind);
                if (skill)
                {
                    line.entries.Add(SkillEntry.Of(skill));
                }

                line.entries.AddRange(extras.Where(extra => extra.slot == kind).Select(SkillEntry.Of));
                if (line.entries.Count > 0)
                {
                    lines.Add(line);
                }
            }

            lines.AddRange(SkillLine.FromExtras(extras.Where(extra => extra.slot == SkillSlot.None)));
            return lines;
        }

        /// <summary>
        /// The panel, built inactive under the pause screen: the survivor's name, a line saying what it shows, the
        /// skill lines, and Back.
        /// </summary>
        private static GameObject Build(PauseScreenController controller, string title, Color color, List<SkillLine> lines)
        {
            CharacterSelectController source = Template();
            RectTransform skillPanel = source ? source.skillStripContainer : null;
            if (!skillPanel)
            {
                Log.Warning("Could not load character select's skill strips; the Abilities panel can't be shown.");
                return null;
            }

            GameObject root = new GameObject(PanelName, typeof(RectTransform));
            root.SetActive(false);
            root.transform.SetParent(controller.rootPanel, worldPositionStays: false);

            RectTransform rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            // Character select's left panel: its background, then the survivor's name in its name bar.
            Transform namePanel = source.survivorName ? source.survivorName.transform.parent : null;
            Image backdrop = namePanel && namePanel.parent ? namePanel.parent.GetComponent<Image>() : null;
            if (backdrop)
            {
                Image image = root.AddComponent<Image>();
                image.sprite = backdrop.sprite;
                image.type = backdrop.type;
                image.color = backdrop.color;
                image.material = backdrop.material;
                image.pixelsPerUnitMultiplier = backdrop.pixelsPerUnitMultiplier;
            }

            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UILayerKey key = root.AddComponent<UILayerKey>();
            key.layer = Layer();
            key.onBeginRepresentTopLayer = new UnityEvent();
            key.onEndRepresentTopLayer = new UnityEvent();

            HGGamepadInputEvent cancel = root.AddComponent<HGGamepadInputEvent>();
            cancel.actionName = "UICancel";
            cancel.requiredTopLayer = key;
            cancel.enabledObjectsIfActive = Array.Empty<GameObject>();
            cancel.actionEvent = new UnityEvent();
            cancel.actionEvent.AddListener(() => Object.Destroy(root));

            if (namePanel)
            {
                GameObject titleObject = Object.Instantiate(namePanel.gameObject, root.transform, worldPositionStays: false);
                TMP_Text titleText = UiHierarchy.Counterpart(namePanel, titleObject.transform, (TMP_Text)source.survivorName);
                if (titleText)
                {
                    titleText.SetText(title);
                    titleText.color = color;
                }
            }

            // A copy of character select's Skills panel (for its fonts and hover description box), holding lines.
            GameObject column = Object.Instantiate(skillPanel.gameObject, root.transform, worldPositionStays: false);
            column.name = "SkillLines";
            VerticalLayoutGroup columnLayout = column.GetComponent<VerticalLayoutGroup>();
            if (columnLayout)
            {
                columnLayout.childControlHeight = true;
                columnLayout.childForceExpandHeight = false;
            }

            GameObject stripTemplate = UiHierarchy.Counterpart(skillPanel, column.transform, source.skillStripPrefab);
            HGButton stripButton = stripTemplate ? stripTemplate.GetComponent<HGButton>() : null;
            TMP_Text descriptionTemplate = stripTemplate ? stripTemplate.transform.Find("Inner/SkillDescriptionPanel/SkillDescription")?.GetComponent<TMP_Text>() : null;
            if (descriptionTemplate)
            {
                TMP_Text intro = UiHierarchy.AddText(descriptionTemplate, root.transform, "Intro", ColumnWidth);
                intro.SetText(Language.GetString(ModTokens.PauseIntro));
                column.transform.SetAsLastSibling();
            }

            LanguageTextMeshController hoverText = stripButton ? stripButton.hoverLanguageTextMeshController : null;
            SkillLines.Build((RectTransform)column.transform, lines, color, hoverText, key);

            float padding = columnLayout ? columnLayout.padding.vertical : 0f;
            float spacing = columnLayout ? columnLayout.spacing : 0f;
            LayoutElement size = LayoutElementOf(column);
            size.minWidth = size.preferredWidth = ColumnWidth;
            size.minHeight = size.preferredHeight = padding + Mathf.Max(1, lines.Count) * SkillLines.LineHeight + Mathf.Max(0, lines.Count - 1) * spacing;

            // The box hovering writes into hangs off the lines' right edge, as in character select.
            Transform box = hoverText ? hoverText.transform : null;
            while (box && box.parent != column.transform)
            {
                box = box.parent;
            }

            if (box is RectTransform boxRect)
            {
                boxRect.sizeDelta = new Vector2(HoverBoxWidth, boxRect.sizeDelta.y);
            }

            AddBackButton(controller, root);

            // Copies of buttons from elsewhere still wait for their old screen's layer; this panel is theirs now.
            foreach (MPButton button in root.GetComponentsInChildren<MPButton>(includeInactive: true))
            {
                button.requiredTopLayer = key;
            }

            return root;
        }

        /// <summary>A copy of the pause menu's Abilities button reading Back, centred under the columns.</summary>
        private static void AddBackButton(PauseScreenController controller, GameObject root)
        {
            AbilitiesButton abilities = controller.GetComponent<AbilitiesButton>();
            if (!abilities || !abilities.buttonObject)
            {
                return;
            }

            GameObject buttonRow = new GameObject("BackRow", typeof(RectTransform));
            buttonRow.transform.SetParent(root.transform, worldPositionStays: false);
            HorizontalLayoutGroup rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            GameObject back = Object.Instantiate(abilities.buttonObject, buttonRow.transform, worldPositionStays: false);
            back.name = "GenericMenuButton (Back)";
            back.SetActive(true);

            LayoutElement size = LayoutElementOf(back);
            size.preferredWidth = BackButtonWidth;
            size.preferredHeight = BackButtonHeight;

            MPButton button = back.GetComponent<MPButton>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Object.Destroy(root));

            // Where a gamepad starts in the panel.
            button.defaultFallbackButton = true;

            LanguageTextMeshController label = back.GetComponent<LanguageTextMeshController>();
            if (label)
            {
                label.token = ModTokens.Back;
            }
        }

        private static LayoutElement LayoutElementOf(GameObject gameObject)
        {
            LayoutElement layoutElement = gameObject.GetComponent<LayoutElement>();
            return layoutElement ? layoutElement : gameObject.AddComponent<LayoutElement>();
        }

        /// <summary>Character select's prefab, for its strips; it shares its bundle with the pause screen.</summary>
        private static CharacterSelectController Template()
        {
            if (template)
            {
                return template;
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(CharacterSelectAddress);
            GameObject prefab = handle.WaitForCompletion();
            template = prefab ? prefab.GetComponent<CharacterSelectController>() : null;
            if (!template)
            {
                Log.Error($"Could not load {CharacterSelectAddress} ({handle.Status}): {handle.OperationException?.Message ?? "no character select controller on it"}");
            }

            return template;
        }

        private static UILayer Layer()
        {
            if (!layer)
            {
                layer = ScriptableObject.CreateInstance<UILayer>();
                layer.name = "uilSubSkillsDisplayAbilities";
                layer.priority = LayerPriority;
                layer.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }

            return layer;
        }
    }
}
