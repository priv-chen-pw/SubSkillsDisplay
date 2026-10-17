using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EntityStates;
using EntityStates.Acolyte;
using EntityStates.Chef;
using EntityStates.Drifter;
using EntityStates.Drifter.Bag;
using EntityStates.Railgunner.Backpack;
using EntityStates.Railgunner.Reload;
using EntityStates.Railgunner.Scope;
using EntityStates.Railgunner.Weapon;
using EntityStates.VoidSurvivor.CorruptMode;
using RoR2;
using RoR2.Skills;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Finds a survivor's extra skills, the ones its Skills tab leaves out, each with the slot whose skill it
    /// replaces on the HUD. The skills themselves are read from the game: the body's components and the entity
    /// state configurations, which is where the game takes them from too. Only the link between an extra skill
    /// and what gives it is written here.
    ///
    /// Covered: Acolyte, Captain, CHEF, Drifter, Operator, Railgunner and Void Fiend, and survivors from other
    /// mods (<see cref="ModdedExtraSkills"/>). Left out on purpose: skills that share their base skill's name and
    /// description (Huntress's Ballista shots, Captain's probe calls, Operator's repeat leap, Enforcer's shielded
    /// skills), and placeholders that only cancel something or say a skill is unavailable.
    /// </summary>
    internal static class ExtraSkillCatalog
    {
        /// <summary>Whether survivors from other mods are searched too (the ModdedSurvivors setting).</summary>
        internal static bool includeModdedSurvivors = true;

        /// <summary>
        /// Skills a loadout skill makes available by entering its activation state: once that state is entered,
        /// the extras replace the skills of their slots for a while.
        /// </summary>
        private sealed class StateRule
        {
            internal readonly Type parentState;
            private readonly Func<IEnumerable<(SkillSlot, SkillDef)>> resolve;
            private (SkillSlot slot, SkillDef skill)[] extras;

            internal StateRule(Type parentState, Func<IEnumerable<(SkillSlot, SkillDef)>> resolve)
            {
                this.parentState = parentState;
                this.resolve = resolve;
            }

            /// <summary>Resolved on first use, once the entity state configurations are loaded.</summary>
            internal (SkillSlot slot, SkillDef skill)[] Extras => extras ??= resolve().Where(extra => extra.Item2).ToArray();
        }

        private static readonly StateRule[] stateRules =
        {
            // Railgunner: scoping turns the primary into the scope's shot (and the reload into Active Reload),
            // charging the backpack turns it into the charged shot.
            new StateRule(typeof(WindUpScopeHeavy), ScopeShots<ActiveScopeHeavy>),
            new StateRule(typeof(WindUpScopeLight), ScopeShots<ActiveScopeLight>),
            new StateRule(typeof(ChargeSnipeSuper), () => new[] { (SkillSlot.Primary, Configured<ChargedSuper>().primaryOverride) }),
            new StateRule(typeof(ChargeSnipeCryo), () => new[] { (SkillSlot.Primary, Configured<ChargedCryo>().primaryOverride) }),

            // Drifter: with something in the bag the primary becomes Bludgeon and the utility Discard; Tornado
            // Slam turns the primary into the slam that ends it.
            new StateRule(typeof(AimRepossess), () =>
            {
                BaggedObject bag = Configured<BaggedObject>();
                return new[] { (SkillSlot.Primary, bag.primaryOverride), (SkillSlot.Utility, bag.utilityOverride) };
            }),
            new StateRule(typeof(ChargeTornadoSlam), () => new[] { (SkillSlot.Primary, Configured<TornadoSlam>().primaryOverride) }),
        };

        /// <summary>
        /// The extra skills of <paramref name="body"/>, in display order. With
        /// <paramref name="includeOtherLoadouts"/> false, only those its loadout gives; with true, also those of
        /// other loadout choices, as long as those are unlocked.
        /// </summary>
        internal static List<ExtraSkill> For(SurvivorLoadout body, bool includeOtherLoadouts)
        {
            List<ExtraSkill> found = new List<ExtraSkill>();
            if (!body.IsValid)
            {
                return found;
            }

            Collect("hidden loadout rows", () => AddHiddenSlots(body, found));
            Collect("Acolyte forms", () => AddAcolyteForms(body, found));
            Collect("CHEF boosts", () => AddChefBoosts(body, found));
            Collect("Void Fiend corruption", () => AddCorruptedSkills(body, found));
            foreach (StateRule rule in stateRules)
            {
                Collect(rule.parentState.Name, () => AddForState(body, rule.parentState, rule.Extras, null, found));
            }

            if (includeModdedSurvivors)
            {
                Collect("modded survivors", () => ModdedExtraSkills.Add(body, found));
            }

            // A skill the Loadout tab lets you pick for a visible slot is never an extra, and one reached two
            // ways (Acolyte's two dashes, Railgunner's reload from either scope) is listed once per line.
            HashSet<(string, string)> choices = new HashSet<(string, string)>();
            foreach (GenericSkill slot in body.slots.Where(slot => !slot.hideInCharacterSelect))
            {
                foreach ((SkillDef skill, bool _, bool _) in body.Variants(slot))
                {
                    choices.Add((skill.skillNameToken, skill.skillDescriptionToken));
                }
            }

            HashSet<(string, string, string)> listed = new HashSet<(string, string, string)>();
            return found
                .Where(extra => extra.unlocked && (extra.inLoadout || includeOtherLoadouts))
                .Where(extra => !choices.Contains((extra.skill.skillNameToken, extra.descriptionToken)))
                .Where(extra => listed.Add((extra.LineKey, extra.skill.skillNameToken, extra.descriptionToken)))
                .ToList();
        }

        /// <summary>
        /// Logs every survivor's extra skills across all loadouts, leaving out what <paramref name="profile"/>
        /// hasn't unlocked, as the Extra Skills tab does.
        /// </summary>
        internal static void LogAll(UserProfile profile)
        {
            Loadout loadout = Loadout.RequestInstance();
            try
            {
                StringBuilder sb = new StringBuilder("Extra skills per unlocked survivor (all unlocked loadouts):");
                foreach (SurvivorDef survivor in SurvivorCatalog.orderedSurvivorDefs)
                {
                    if (!SurvivorCatalog.SurvivorIsUnlockedOnThisClient(survivor.survivorIndex))
                    {
                        continue;
                    }

                    BodyIndex bodyIndex = SurvivorCatalog.GetBodyIndexFromSurvivorIndex(survivor.survivorIndex);
                    List<ExtraSkill> extras = For(SurvivorLoadout.FromLoadout(bodyIndex, loadout, profile), includeOtherLoadouts: true);
                    if (extras.Count == 0)
                    {
                        continue;
                    }

                    sb.Append("\n  ").Append(Language.GetString(survivor.displayNameToken)).Append(": ");
                    sb.Append(string.Join(", ", extras.Select(extra =>
                        $"{extra.LineKey}: {Language.GetString(extra.skill.skillNameToken)} [{extra.skill.skillName}] ({Language.GetString(extra.sourceToken ?? "")})")));
                }

                Log.Info(sb.ToString());
            }
            finally
            {
                Loadout.ReturnInstance(loadout);
            }
        }

        /// <summary>
        /// Loadout rows the Skills tab hides. Captain's beacons go in the lines of the skills they take over while
        /// placing them (beacon 1 the primary, beacon 2 the secondary, as SetupSupplyDrop does); Acolyte's Litany
        /// is placed by <see cref="AddAcolyteForms"/>; any other (Operator's drones, modded rows) gets a line of
        /// its own under the row's title.
        /// </summary>
        private static void AddHiddenSlots(SurvivorLoadout body, List<ExtraSkill> found)
        {
            CaptainSupplyDropController supplyDrops = body.GetComponent<CaptainSupplyDropController>();
            AcolyteFormController acolyteForm = body.GetComponent<AcolyteFormController>();
            foreach (GenericSkill slot in body.slots.Where(slot => slot.hideInCharacterSelect))
            {
                if (acolyteForm && slot == acolyteForm.transformSkillSlot)
                {
                    continue;
                }

                SkillSlot line = SkillSlot.None;
                string sourceToken = null;
                if (supplyDrops && (slot == supplyDrops.supplyDrop1Skill || slot == supplyDrops.supplyDrop2Skill))
                {
                    line = slot == supplyDrops.supplyDrop1Skill ? SkillSlot.Primary : SkillSlot.Secondary;
                    sourceToken = body.Selected(supplyDrops.prepSupplyDropSkill)?.skillNameToken;
                }

                string lineToken = string.IsNullOrEmpty(slot.loadoutTitleToken) ? null : slot.loadoutTitleToken;
                foreach ((SkillDef skill, bool selected, bool unlocked) in body.Variants(slot))
                {
                    found.Add(new ExtraSkill(skill, line, sourceToken, selected, unlocked, lineToken: lineToken));
                }
            }
        }

        /// <summary>
        /// Acolyte: with full Convictions her Litany (a loadout row the Skills tab hides: Dark Summons or Anima
        /// Echo) takes over the special (SacrificeReady). Dark Summons turns her into the Anima Harvester,
        /// swapping all four skills, and a thrown Blade Sling or Javelin turns that skill into the dash to it.
        /// </summary>
        private static void AddAcolyteForms(SurvivorLoadout body, List<ExtraSkill> found)
        {
            AcolyteBodySwapController swap = body.GetComponent<AcolyteBodySwapController>();
            if (!swap)
            {
                return;
            }

            AcolyteFormController form = body.GetComponent<AcolyteFormController>();
            GenericSkill litany = form ? form.transformSkillSlot : null;
            foreach ((SkillDef skill, bool selected, bool unlocked) in body.Variants(litany))
            {
                found.Add(new ExtraSkill(skill, SkillSlot.Special, litany.loadoutTitleToken, selected, unlocked));
            }

            List<(SkillSlot, SkillDef)> harvester = new List<(SkillSlot, SkillDef)>
            {
                (SkillSlot.Primary, swap.primarySkillOverride),
                (SkillSlot.Secondary, swap.secondarySkillOverride),
                (SkillSlot.Utility, swap.utilitySkillOverride),
                (SkillSlot.Special, swap.specialSkillOverride),
            };
            harvester.AddRange(swap.reaperProjectileSkillOverrides.Select(projectileOverride =>
                (body.KindOf(projectileOverride.skill), projectileOverride.skillOverride)));

            // Named after the form (the name the body takes while swapped) rather than the skill that summons it.
            AddForState(body, typeof(EnterBodySwap), harvester, swap.baseNameToken, found);

            // Both utilities have a dash, so it is shown whichever one is picked.
            foreach (AcolyteBodySwapController.ProjectileSkillOverrideData projectileOverride in swap.acolyteProjectileSkillOverrides)
            {
                if (projectileOverride.skillOverride && projectileOverride.skill)
                {
                    found.Add(new ExtraSkill(projectileOverride.skillOverride, body.KindOf(projectileOverride.skill),
                        body.Selected(projectileOverride.skill)?.skillNameToken, true, true));
                }
            }
        }

        /// <summary>
        /// CHEF: Yes, CHEF! boosts the next skill, swapping each skill for its boosted version. Matched to the
        /// loadout by skill name and slot, as <see cref="ChefController.GetYesChefSkillOverrideData"/> does.
        /// </summary>
        private static void AddChefBoosts(SurvivorLoadout body, List<ExtraSkill> found)
        {
            ChefController chef = body.GetComponent<ChefController>();
            if (!chef)
            {
                return;
            }

            (SkillSlot slot, SkillDef boosted)[] boosts =
            {
                (SkillSlot.Primary, chef.primarySkillOverrideDef),
                (SkillSlot.Secondary, chef.secondarySkillOverrideDef),
                (SkillSlot.Secondary, chef.secondaryAltSkillOverrideDef),
                (SkillSlot.Utility, chef.utilitySkillOverrideDef),
                (SkillSlot.Utility, chef.utilityAltSkillOverrideDef),
            };

            foreach (GenericSkill slot in body.slots)
            {
                foreach ((SkillDef yesChef, bool selected, bool unlocked) in body.Variants(slot).Where(variant => Enters(variant.skill, typeof(YesChef))))
                {
                    foreach ((SkillSlot boostedSlot, SkillDef boosted) in boosts)
                    {
                        if (!boosted)
                        {
                            continue;
                        }

                        SkillDef current = body.Selected(boostedSlot);
                        bool boostsCurrent = current && current.skillName == boosted.skillName;

                        // Both Yes, CHEF! and the skill it boosts have to be unlocked.
                        bool baseUnlocked = body.IsUnlocked(boostedSlot, boosted.skillName);
                        found.Add(new ExtraSkill(boosted, boostedSlot, yesChef.skillNameToken, selected && boostsCurrent, unlocked && baseUnlocked));
                    }
                }
            }
        }

        /// <summary>
        /// Void Fiend: at full Corruption every skill becomes its corrupted form. Those reuse the uncorrupted
        /// skill's name and description, so they are described by the base skill's "Corruption Upgrade" keyword.
        /// </summary>
        private static void AddCorruptedSkills(SurvivorLoadout body, List<ExtraSkill> found)
        {
            if (!body.GetComponent<VoidSurvivorController>())
            {
                return;
            }

            CorruptMode corruptMode = Configured<CorruptMode>();
            (SkillSlot slot, SkillDef corrupted)[] corruptions =
            {
                (SkillSlot.Primary, corruptMode.primaryOverrideSkillDef),
                (SkillSlot.Secondary, corruptMode.secondaryOverrideSkillDef),
                (SkillSlot.Utility, corruptMode.utilityOverrideSkillDef),
                (SkillSlot.Special, corruptMode.specialOverrideSkillDef),
            };

            // The passive (Void Corruption) is the slot that is none of the four.
            SkillDef passive = body.slots
                .Where(slot => !slot.hideInCharacterSelect && body.KindOf(slot) == SkillSlot.None)
                .Select(body.Selected)
                .FirstOrDefault(skill => skill);

            foreach ((SkillSlot slot, SkillDef corrupted) in corruptions)
            {
                if (!corrupted)
                {
                    continue;
                }

                SkillDef current = body.Selected(slot);
                string descriptionToken = null;
                if (current && current.skillDescriptionToken == corrupted.skillDescriptionToken && current.keywordTokens != null)
                {
                    // The survivor's own keyword, not one of the shared KEYWORD_ ones such as Stunning.
                    descriptionToken = current.keywordTokens.FirstOrDefault(token => !token.StartsWith("KEYWORD_", StringComparison.Ordinal));
                }

                found.Add(new ExtraSkill(corrupted, slot, passive ? passive.skillNameToken : null, true, true, descriptionToken));
            }
        }

        /// <summary>
        /// Adds <paramref name="extras"/> for every loadout skill that enters <paramref name="parentState"/>,
        /// attributed to that skill unless <paramref name="sourceToken"/> names something else.
        /// </summary>
        private static void AddForState(SurvivorLoadout body, Type parentState, IEnumerable<(SkillSlot slot, SkillDef skill)> extras, string sourceToken, List<ExtraSkill> found)
        {
            (SkillSlot slot, SkillDef skill)[] skills = extras.Where(extra => extra.skill).ToArray();
            foreach (GenericSkill genericSkill in body.slots)
            {
                foreach ((SkillDef parent, bool selected, bool unlocked) in body.Variants(genericSkill).Where(variant => Enters(variant.skill, parentState)))
                {
                    foreach ((SkillSlot slot, SkillDef extra) in skills)
                    {
                        found.Add(new ExtraSkill(extra, slot, sourceToken ?? parent.skillNameToken, selected, unlocked));
                    }
                }
            }
        }

        /// <summary>
        /// The scope's shot, plus Active Reload when that shot queues a reload (in vanilla only M99's does, which
        /// is also the only scope listing Active Reload among its keywords). Both replace the primary.
        /// </summary>
        private static IEnumerable<(SkillSlot, SkillDef)> ScopeShots<TActiveScope>() where TActiveScope : BaseActive, new()
        {
            SkillDef shot = Configured<TActiveScope>().primaryOverride;
            yield return (SkillSlot.Primary, shot);

            Type shotState = shot ? shot.activationState.stateType : null;
            if (shotState != null && EntityStateCatalog.InstantiateState(shotState) is BaseFireSnipe fire && fire.queueReload)
            {
                yield return (SkillSlot.Primary, Configured<Reloading>().primaryOverride);
            }
        }

        private static bool Enters(SkillDef skill, Type state)
        {
            Type activationState = skill.activationState.stateType;
            return activationState != null && (activationState == state || activationState.IsSubclassOf(state));
        }

        /// <summary>
        /// A fresh state with its entity state configuration applied (the <see cref="EntityState"/> constructor
        /// applies it), to read the skills the configuration assigns. The state is never entered.
        /// </summary>
        private static T Configured<T>() where T : EntityState, new() => new T();

        /// <summary>Runs one source, so a game patch breaking it costs only that source's skills.</summary>
        private static void Collect(string source, Action collect)
        {
            try
            {
                collect();
            }
            catch (Exception e)
            {
                Log.Warning($"Could not read the extra skills from {source}: {e}");
            }
        }
    }
}
