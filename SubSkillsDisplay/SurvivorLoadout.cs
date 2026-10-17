using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace SubSkillsDisplay
{
    /// <summary>
    /// A survivor's body and the skills its loadout picks: in character select the body prefab and the loadout
    /// being edited, in a run the player's own body, whose slots hold the run's loadout.
    /// </summary>
    internal sealed class SurvivorLoadout
    {
        internal readonly BodyIndex bodyIndex;
        internal readonly GameObject body;
        internal readonly SkillLocator skillLocator;

        /// <summary>The body's skill slots, in the order loadouts index them.</summary>
        internal readonly GenericSkill[] slots;

        /// <summary>
        /// What each slot is (primary...), by index, from the body prefab: a living Captain's locator points its
        /// primary and secondary at the beacons while he places one.
        /// </summary>
        private readonly SkillSlot[] kinds;

        private readonly Func<int, GenericSkill, SkillDef> pickedSkill;

        /// <summary>Whose unlocks decide what may be shown; null counts everything as unlocked.</summary>
        private readonly UserProfile profile;

        private SurvivorLoadout(BodyIndex bodyIndex, GameObject body, GenericSkill[] slots, Func<int, GenericSkill, SkillDef> pickedSkill, UserProfile profile)
        {
            this.bodyIndex = bodyIndex;
            this.body = body;
            this.slots = slots ?? Array.Empty<GenericSkill>();
            this.pickedSkill = pickedSkill;
            this.profile = profile;
            skillLocator = body ? body.GetComponent<SkillLocator>() : null;

            GenericSkill[] prefabSlots = BodyCatalog.GetBodyPrefabSkillSlots(bodyIndex);
            GameObject prefab = BodyCatalog.GetBodyPrefab(bodyIndex);
            SkillLocator prefabLocator = prefab ? prefab.GetComponent<SkillLocator>() : null;
            bool fromPrefab = prefabLocator && prefabSlots != null && prefabSlots.Length == this.slots.Length;
            kinds = new SkillSlot[this.slots.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                kinds[i] = fromPrefab ? prefabLocator.FindSkillSlot(prefabSlots[i])
                    : skillLocator ? skillLocator.FindSkillSlot(this.slots[i])
                    : SkillSlot.None;
            }
        }

        /// <summary>The body prefab with the skills <paramref name="loadout"/> picks for it.</summary>
        internal static SurvivorLoadout FromLoadout(BodyIndex bodyIndex, Loadout loadout, UserProfile profile)
        {
            return new SurvivorLoadout(
                bodyIndex,
                BodyCatalog.GetBodyPrefab(bodyIndex),
                BodyCatalog.GetBodyPrefabSkillSlots(bodyIndex),
                (index, slot) =>
                {
                    uint variant = loadout.bodyLoadoutManager.GetSkillVariant(bodyIndex, index);
                    SkillFamily.Variant[] variants = slot.skillFamily.variants;
                    return variant < variants.Length ? variants[variant].skillDef : null;
                },
                profile);
        }

        /// <summary>
        /// A body in a run. Its slots' base skills are the loadout's (clients get them from the server), and
        /// stay so while a form or another skill overrides them.
        /// </summary>
        internal static SurvivorLoadout FromBody(CharacterBody body, UserProfile profile)
        {
            SkillLocator locator = body.skillLocator;
            GenericSkill[] slots = locator && locator.allSkills != null ? locator.allSkills : body.GetComponents<GenericSkill>();
            return new SurvivorLoadout(body.bodyIndex, body.gameObject, slots, (index, slot) => slot.baseSkill, profile);
        }

        internal bool IsValid => body && skillLocator;

        internal T GetComponent<T>() where T : Component => body.GetComponent<T>();

        /// <summary>What <paramref name="slot"/>, a slot of this body, is: primary, secondary, utility, special or none of them.</summary>
        internal SkillSlot KindOf(GenericSkill slot)
        {
            int index = Array.IndexOf(slots, slot);
            return index >= 0 ? kinds[index] : SkillSlot.None;
        }

        /// <summary>The slot that is <paramref name="kind"/>, or null.</summary>
        internal GenericSkill SlotOf(SkillSlot kind)
        {
            int index = Array.IndexOf(kinds, kind);
            return kind != SkillSlot.None && index >= 0 ? slots[index] : null;
        }

        /// <summary>The skill the loadout picks for <paramref name="slot"/>, a slot of this body.</summary>
        internal SkillDef Selected(GenericSkill slot)
        {
            int index = Array.IndexOf(slots, slot);
            return index >= 0 && slot.skillFamily ? pickedSkill(index, slot) : null;
        }

        internal SkillDef Selected(SkillSlot kind) => Selected(SlotOf(kind));

        /// <summary>Every skill <paramref name="slot"/> can hold, whether the loadout picks it, and whether it is unlocked.</summary>
        internal IEnumerable<(SkillDef skill, bool selected, bool unlocked)> Variants(GenericSkill slot)
        {
            if (!slot || !slot.skillFamily)
            {
                yield break;
            }

            SkillDef selected = Selected(slot);
            foreach (SkillFamily.Variant variant in slot.skillFamily.variants)
            {
                if (variant.skillDef)
                {
                    yield return (variant.skillDef, variant.skillDef == selected, IsUnlocked(variant));
                }
            }
        }

        /// <summary>Whether the variant of the <paramref name="kind"/> slot named <paramref name="skillName"/> is unlocked.</summary>
        internal bool IsUnlocked(SkillSlot kind, string skillName)
        {
            GenericSkill genericSkill = SlotOf(kind);
            if (!genericSkill || !genericSkill.skillFamily)
            {
                return false;
            }

            foreach (SkillFamily.Variant variant in genericSkill.skillFamily.variants)
            {
                if (variant.skillDef && variant.skillDef.skillName == skillName && IsUnlocked(variant))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsUnlocked(SkillFamily.Variant variant) =>
            profile == null || !variant.unlockableDef || profile.HasUnlockable(variant.unlockableDef);
    }
}
