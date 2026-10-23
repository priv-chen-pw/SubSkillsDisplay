using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Adds an "Extra Skills" tab to character select, last after Loadout. It lists every extra skill of the
    /// survivor that the player has unlocked, in lines per skill type like the Loadout tab's, under a line
    /// explaining what they are. Hovering one shows its description and keywords. Nothing in it can be picked.
    ///
    /// The tab button and the panel are copies of the Skills tab's, and the lines use the Loadout tab's
    /// prefabs, so fonts, layout and the hover description box come from the game.
    /// </summary>
    internal static class ExtraSkillsTab
    {
        private const string HeaderName = "SubSkillsDisplayExtra";

        private static bool logPending;

        internal static void Init(bool logExtraSkills)
        {
            logPending = logExtraSkills;

            On.RoR2.UI.CharacterSelectController.Awake += (orig, self) =>
            {
                orig(self);
                Build(self);

                // By now every catalog, the language strings and the player's profile are loaded.
                UserProfile profile = LocalUserManager.GetFirstLocalUser()?.userProfile;
                if (logPending && profile != null)
                {
                    logPending = false;
                    ExtraSkillCatalog.LogAll(profile);
                }
            };

            // Vanilla rebuilds the Skills tab here on every survivor, loadout, unlock or entitlement change.
            On.RoR2.UI.CharacterSelectController.RebuildLocal += (orig, self, deselectButtons) =>
            {
                orig(self, deselectButtons);
                ExtraSkillsView view = self.GetComponent<ExtraSkillsView>();
                if (view)
                {
                    view.Rebuild(self);
                }
            };
        }

        private static void Build(CharacterSelectController controller)
        {
            RectTransform skillPanel = controller.skillStripContainer;
            HGHeaderNavigationController navigation = null;
            int skillsHeaderIndex = -1;
            if (skillPanel)
            {
                foreach (HGHeaderNavigationController candidate in controller.GetComponentsInChildren<HGHeaderNavigationController>(includeInactive: true))
                {
                    skillsHeaderIndex = candidate.headers.FindIndex(header => header.headerRoot == skillPanel.gameObject);
                    if (skillsHeaderIndex >= 0)
                    {
                        navigation = candidate;
                        break;
                    }
                }
            }

            if (!navigation)
            {
                Log.Warning("Character select has no Skills tab to model the Extra Skills tab on; skipping it.");
                return;
            }

            ModTokens.Register();

            HGHeaderNavigationController.Header skillsHeader = navigation.headers[skillsHeaderIndex];

            // The button goes last, after Loadout (the tab whose panel is the loadout editor).
            int loadoutHeaderIndex = navigation.headers.FindIndex(header => header.headerRoot && header.headerRoot.GetComponent<LoadoutPanelController>());
            MPButton lastButton = navigation.headers[loadoutHeaderIndex >= 0 ? loadoutHeaderIndex : navigation.headers.Count - 1].headerButton;

            // The Skills tab's panel, its strips (the skill templates) set aside for lines that keep their height.
            GameObject panel = Object.Instantiate(skillPanel.gameObject, skillPanel.parent, worldPositionStays: false);
            panel.name = "ExtraSkillPanel";
            panel.SetActive(false);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            if (layout)
            {
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
            }

            GameObject stripTemplate = UiHierarchy.Counterpart(skillPanel, panel.transform, controller.skillStripPrefab);
            HGButton stripButton = stripTemplate ? stripTemplate.GetComponent<HGButton>() : null;
            TMP_Text descriptionTemplate = stripTemplate ? stripTemplate.transform.Find("Inner/SkillDescriptionPanel/SkillDescription")?.GetComponent<TMP_Text>() : null;
            TMP_Text intro = descriptionTemplate ? UiHierarchy.AddText(descriptionTemplate, panel.transform, "Intro") : null;
            if (intro)
            {
                intro.transform.SetAsFirstSibling();
            }

            MPButton skillsButton = skillsHeader.headerButton;
            GameObject buttonObject = Object.Instantiate(skillsButton.gameObject, skillsButton.transform.parent, worldPositionStays: false);
            buttonObject.name = "GenericMenuButton (Extra Skills)";
            buttonObject.transform.SetSiblingIndex(lastButton.transform.GetSiblingIndex() + 1);

            MPButton button = buttonObject.GetComponent<MPButton>();

            // The copy would still select the Skills tab if it kept the original's click listener.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => navigation.ChooseHeaderByButton(button));

            LanguageTextMeshController label = buttonObject.GetComponent<LanguageTextMeshController>();
            if (label)
            {
                label.token = ModTokens.ExtraTab;
            }

            // Character select paints the tab buttons in the survivor's colour; paint the copy along with them.
            controller.primaryColorImages = WithCopies(controller.primaryColorImages, skillsButton.transform, buttonObject.transform);
            controller.primaryColorTexts = WithCopies(controller.primaryColorTexts, skillsButton.transform, buttonObject.transform);

            navigation.headers.Add(new HGHeaderNavigationController.Header
            {
                headerButton = button,
                headerName = HeaderName,
                tmpHeaderText = UiHierarchy.Counterpart(skillsButton.transform, buttonObject.transform, skillsHeader.tmpHeaderText),
                headerRoot = panel,
                AreConsolePlatformsSupported = skillsHeader.AreConsolePlatformsSupported,
                isPrimaryPlayerOnly = skillsHeader.isPrimaryPlayerOnly,
            });

            controller.gameObject.AddComponent<ExtraSkillsView>().Init((RectTransform)panel.transform, intro,
                stripButton ? stripButton.hoverLanguageTextMeshController : null,
                stripButton ? stripButton.requiredTopLayer : null);

            Log.Info("Added the Extra Skills tab to character select.");
        }

        /// <summary>
        /// <paramref name="painted"/> plus the copy's counterpart of every entry inside <paramref name="original"/>.
        /// </summary>
        private static T[] WithCopies<T>(T[] painted, Transform original, Transform copy) where T : Component
        {
            if (painted == null)
            {
                return null;
            }

            List<T> result = new List<T>(painted);
            foreach (T graphic in painted)
            {
                T counterpart = graphic && graphic.transform.IsChildOf(original) ? UiHierarchy.Counterpart(original, copy, graphic) : null;
                if (counterpart)
                {
                    result.Add(counterpart);
                }
            }

            return result.ToArray();
        }

        /// <summary>The tab of one character select screen: its panel, explanation and lines.</summary>
        internal class ExtraSkillsView : MonoBehaviour
        {
            private RectTransform panel;
            private TMP_Text intro;
            private LanguageTextMeshController hoverText;
            private UILayerKey requiredTopLayer;
            private List<GameObject> rows = new List<GameObject>();

            internal void Init(RectTransform panel, TMP_Text intro, LanguageTextMeshController hoverText, UILayerKey requiredTopLayer)
            {
                this.panel = panel;
                this.intro = intro;
                this.hoverText = hoverText;
                this.requiredTopLayer = requiredTopLayer;
            }

            internal void Rebuild(CharacterSelectController controller)
            {
                List<SkillLine> lines = new List<SkillLine>();
                Color bodyColor = Color.gray;

                LocalUser localUser = controller.localUser;
                NetworkUser networkUser = localUser?.currentNetworkUser;
                SurvivorDef survivor = networkUser ? networkUser.GetSurvivorPreference() : null;

                // A survivor the player hasn't unlocked shows nothing (vanilla doesn't let it be picked anyway).
                if (survivor && localUser.userProfile != null && SurvivorCatalog.SurvivorIsUnlockedOnThisClient(survivor.survivorIndex))
                {
                    BodyIndex bodyIndex = SurvivorCatalog.GetBodyIndexFromSurvivorIndex(survivor.survivorIndex);
                    CharacterBody body = BodyCatalog.GetBodyPrefabBodyComponent(bodyIndex);
                    if (body)
                    {
                        bodyColor = body.bodyColor;
                    }

                    // Every unlocked loadout choice's extra skills, whichever is picked, each after the skill it
                    // replaces (built here, while the loadout that picks those is still ours).
                    Loadout loadout = Loadout.RequestInstance();
                    try
                    {
                        localUser.userProfile.CopyLoadout(loadout);
                        SurvivorLoadout survivorLoadout = SurvivorLoadout.FromLoadout(bodyIndex, loadout, localUser.userProfile);
                        lines = SkillLine.FromExtras(survivorLoadout, ExtraSkillCatalog.For(survivorLoadout, includeOtherLoadouts: true));
                    }
                    finally
                    {
                        Loadout.ReturnInstance(loadout);
                    }
                }

                foreach (GameObject row in rows)
                {
                    Destroy(row);
                }

                rows = SkillLines.Build(panel, lines, bodyColor, hoverText, requiredTopLayer);
                if (intro)
                {
                    intro.SetText(Language.GetString(lines.Count > 0 ? ModTokens.ExtraIntro : ModTokens.ExtraNone));
                    intro.transform.SetAsFirstSibling();
                }
            }
        }
    }
}
