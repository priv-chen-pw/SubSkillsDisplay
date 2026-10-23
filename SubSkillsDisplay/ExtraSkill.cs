using RoR2;
using RoR2.Skills;

namespace SubSkillsDisplay
{
    /// <summary>
    /// A skill a survivor can use in a run that character select's Skills tab never shows: a transformation's
    /// skills, a skill that temporarily replaces another one, or a choice from a loadout row the Skills tab hides.
    /// </summary>
    internal readonly struct ExtraSkill
    {
        internal readonly SkillDef skill;

        /// <summary>The skill it replaces on the HUD, which is the line it is shown in; None for a line of its own.</summary>
        internal readonly SkillSlot slot;

        /// <summary>For <see cref="SkillSlot.None"/>: the title of its line (a loadout row's title), or null for "Misc".</summary>
        internal readonly string lineToken;

        /// <summary>What gives the skill (a loadout skill, a form, a loadout row), shown after its name.</summary>
        internal readonly string sourceToken;

        /// <summary>The text to describe it with: its own description, unless it shares its base skill's.</summary>
        internal readonly string descriptionToken;

        /// <summary>Whether the loadout being shown gives this skill.</summary>
        internal readonly bool inLoadout;

        /// <summary>
        /// Whether the player has unlocked what gives it. A skill from a locked loadout choice is never shown, so
        /// nothing is spoiled.
        /// </summary>
        internal readonly bool unlocked;

        /// <summary>
        /// The loadout skill it replaces when it replaces only that one (CHEF's boosts); null when it replaces
        /// whichever skill its slot holds.
        /// </summary>
        internal readonly SkillDef replaces;

        internal ExtraSkill(SkillDef skill, SkillSlot slot, string sourceToken, bool inLoadout, bool unlocked,
            string descriptionToken = null, string lineToken = null, SkillDef replaces = null)
        {
            this.replaces = replaces;
            this.skill = skill;
            this.slot = slot;
            this.lineToken = slot == SkillSlot.None ? lineToken : null;
            this.sourceToken = sourceToken;
            this.inLoadout = inLoadout;
            this.unlocked = unlocked;
            this.descriptionToken = string.IsNullOrEmpty(descriptionToken) ? skill.skillDescriptionToken : descriptionToken;
        }

        /// <summary>Which line it goes in: one per slot, and one per title for the others.</summary>
        internal string LineKey => slot != SkillSlot.None ? slot.ToString() : lineToken ?? string.Empty;
    }
}
