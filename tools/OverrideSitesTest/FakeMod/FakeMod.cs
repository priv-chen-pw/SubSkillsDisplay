using EntityStates;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace FakeMod
{
    // Patterns character mods use to swap skills in. Each should be found as a load of the named member.
    public static class SkillDefs
    {
        public static SkillDef transformPrimary;          // 1 static field
        public static SkillDef[] stanceSkills;            // 3 array element
        public static SkillDef UpgradedSpecial { get; set; } // 4 property getter
        public static SkillDef calmUtility, angryUtility;  // 7 ternary
        public static SkillDef dodge;                       // 9 slot through GetSkill
    }

    public class TransformState : BaseState
    {
        public SkillDef secondaryOverride;                 // 2 instance field (ESC or code)
        public static bool angry;

        public override void OnEnter()
        {
            base.OnEnter();
            skillLocator.primary.SetSkillOverride(this, SkillDefs.transformPrimary, GenericSkill.SkillOverridePriority.Upgrade);
            skillLocator.secondary.SetSkillOverride(this, secondaryOverride, GenericSkill.SkillOverridePriority.Upgrade);
            skillLocator.utility.SetSkillOverride(this, SkillDefs.stanceSkills[1], GenericSkill.SkillOverridePriority.Contextual);
            skillLocator.special.SetSkillOverride(this, SkillDefs.UpgradedSpecial, GenericSkill.SkillOverridePriority.Replacement);
            Swap(skillLocator.utility, SkillDefs.stanceSkills[0]);
            skillLocator.utility.SetSkillOverride(this, angry ? SkillDefs.angryUtility : SkillDefs.calmUtility, GenericSkill.SkillOverridePriority.Contextual);
            skillLocator.GetSkill(SkillSlot.Utility).SetSkillOverride(this, SkillDefs.dodge, GenericSkill.SkillOverridePriority.Contextual);
        }

        // 5 wrapper method
        private void Swap(GenericSkill slot, SkillDef skill) => slot.SetSkillOverride(this, skill, GenericSkill.SkillOverridePriority.Contextual);
    }

    // 6 component field
    public class FormController : MonoBehaviour
    {
        public SkillDef formSpecial;
        private CharacterBody body;

        public void EnterForm() => body.skillLocator.special.SetSkillOverride(this, formSpecial, GenericSkill.SkillOverridePriority.Upgrade);

        // 8 vanilla holder
        public SkillStateOverrideData Data() => new SkillStateOverrideData(body) { primarySkillOverride = SkillDefs.transformPrimary };
    }
}
