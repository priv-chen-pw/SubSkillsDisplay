using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using EntityStates;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using UnityEngine;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Extra skills of survivors that other mods add (from content packs other than the game's own). Vanilla
    /// survivors are left to <see cref="ExtraSkillCatalog"/>'s own rules.
    ///
    /// A skill counts when it is named, no loadout offers it, and the survivor's mod shows it is swapped in:
    /// <list type="bullet">
    /// <item>a state one of the survivor's loadout skills enters names it (a static field, or a field its
    /// entity state configuration sets): it then follows that skill's unlock, like a vanilla extra skill;</item>
    /// <item>a component on the survivor's body names it;</item>
    /// <item>the mod's code passes it to <c>SetSkillOverride</c> (<see cref="OverrideCallScanner"/>).</item>
    /// </list>
    /// A skill a mod only creates (unused, or switched off in its config) is left out. The slot a skill replaces
    /// comes from the code that swaps it in, or from the name of the field holding it (primarySkillOverride).
    /// </summary>
    internal static class ModdedExtraSkills
    {
        private sealed class ModdedSurvivor
        {
            internal BodyIndex bodyIndex;
            internal GameObject body;

            /// <summary>Its name token without the trailing BODY_NAME or NAME, which its mod's skill tokens usually start with.</summary>
            internal string tokenStem;

            /// <summary>Namespaces of the states its loadout skills enter.</summary>
            internal HashSet<string> stateNamespaces;

            /// <summary>The mod code it comes from: assemblies in the plugins folder defining its states or body components.</summary>
            internal HashSet<Assembly> assemblies;

            /// <summary>Skills shown whatever the loadout, with the slot each replaces (None if unknown).</summary>
            internal readonly Dictionary<SkillDef, SkillSlot> unlinked = new Dictionary<SkillDef, SkillSlot>();

            internal void AddUnlinked(SkillDef skill, SkillSlot slot)
            {
                if (!unlinked.TryGetValue(skill, out SkillSlot known) || known == SkillSlot.None)
                {
                    unlinked[skill] = slot;
                }
            }
        }

        private static readonly string[] slotWords = { "primary", "secondary", "utility", "special" };

        private static Dictionary<BodyIndex, ModdedSurvivor> survivors;
        private static HashSet<SkillDef> loadoutChoices;
        private static readonly Dictionary<Type, (SkillDef skill, SkillSlot slot)[]> skillsNamedByState = new Dictionary<Type, (SkillDef, SkillSlot)[]>();

        internal static void Add(SurvivorLoadout body, List<ExtraSkill> found)
        {
            if (survivors == null)
            {
                FindSurvivors();
            }

            if (!survivors.TryGetValue(body.bodyIndex, out ModdedSurvivor survivor))
            {
                return;
            }

            // Skills the states of loadout skills name: any picked one puts it in the loadout, any unlocked one
            // unlocks it, and it is shown as given by the picked one if there is one.
            Dictionary<SkillDef, (SkillSlot slot, string sourceToken, bool selected, bool unlocked)> links = new Dictionary<SkillDef, (SkillSlot, string, bool, bool)>();
            foreach (GenericSkill genericSkill in body.slots)
            {
                foreach ((SkillDef choice, bool selected, bool unlocked) in body.Variants(genericSkill))
                {
                    foreach ((SkillDef named, SkillSlot namedSlot) in SkillsNamedBy(choice.activationState.stateType).Where(entry => IsCandidate(entry.skill)))
                    {
                        SkillSlot slot = namedSlot != SkillSlot.None ? namedSlot
                            : survivor.unlinked.TryGetValue(named, out SkillSlot known) ? known
                            : SkillSlot.None;
                        links[named] = links.TryGetValue(named, out var link)
                            ? (link.slot != SkillSlot.None ? link.slot : slot, selected ? choice.skillNameToken : link.sourceToken, link.selected || selected, link.unlocked || unlocked)
                            : (slot, choice.skillNameToken, selected, unlocked);
                    }
                }
            }

            foreach (KeyValuePair<SkillDef, (SkillSlot slot, string sourceToken, bool selected, bool unlocked)> link in links)
            {
                found.Add(new ExtraSkill(link.Key, link.Value.slot, link.Value.sourceToken, link.Value.selected, link.Value.unlocked));
            }

            foreach (KeyValuePair<SkillDef, SkillSlot> skill in survivor.unlinked.Where(entry => !links.ContainsKey(entry.Key)))
            {
                found.Add(new ExtraSkill(skill.Key, skill.Value, null, true, true));
            }
        }

        /// <summary>Finds the modded survivors and the skills their bodies and code swap in. Runs once, after the game has loaded.</summary>
        private static void FindSurvivors()
        {
            survivors = new Dictionary<BodyIndex, ModdedSurvivor>();
            loadoutChoices = new HashSet<SkillDef>(SkillCatalog.allSkillFamilies
                .Where(family => family)
                .SelectMany(family => family.variants)
                .Select(variant => variant.skillDef)
                .Where(skill => skill));

            foreach (ReadOnlyContentPack pack in ContentManager.allLoadedContentPacks.Where(pack => !IsVanilla(pack.identifier)))
            {
                foreach (SurvivorDef survivorDef in pack.survivorDefs)
                {
                    BodyIndex bodyIndex = survivorDef && !survivorDef.hidden && survivorDef.bodyPrefab
                        ? BodyCatalog.FindBodyIndex(survivorDef.bodyPrefab)
                        : BodyIndex.None;
                    if (bodyIndex != BodyIndex.None && !survivors.ContainsKey(bodyIndex))
                    {
                        survivors[bodyIndex] = Describe(survivorDef, bodyIndex);
                    }
                }
            }

            if (survivors.Count == 0)
            {
                return;
            }

            // Skills each mod's code swaps in, given to the survivor whose body they were read from, the mod's
            // only survivor, or the one the skill's name token or state namespace points to.
            foreach (Assembly assembly in survivors.Values.SelectMany(survivor => survivor.assemblies).Distinct())
            {
                List<ModdedSurvivor> owners = survivors.Values.Where(survivor => survivor.assemblies.Contains(assembly)).ToList();
                List<OverrideCallScanner.Found> swapped;
                try
                {
                    swapped = OverrideCallScanner.Scan(assembly, owners.Select(owner => owner.body).ToArray());
                }
                catch (Exception e)
                {
                    Log.Warning($"Could not read {assembly.GetName().Name}'s code for skill overrides: {e.Message}");
                    continue;
                }

                foreach (OverrideCallScanner.Found swap in swapped.Where(swap => IsCandidate(swap.skill)))
                {
                    ModdedSurvivor owner = swap.body ? owners.FirstOrDefault(survivor => survivor.body == swap.body)
                        : owners.Count == 1 ? owners[0]
                        : Owner(swap.skill, owners);
                    owner?.AddUnlinked(swap.skill, swap.slot);
                }
            }

            // Components on each body that name skills.
            foreach (ModdedSurvivor survivor in survivors.Values)
            {
                foreach (MonoBehaviour component in survivor.body.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
                {
                    if (component && !(component is GenericSkill))
                    {
                        foreach ((SkillDef skill, SkillSlot slot) in SkillsIn(component).Where(entry => IsCandidate(entry.skill)))
                        {
                            survivor.AddUnlinked(skill, slot);
                        }
                    }
                }
            }

            Log.Info($"Searched {survivors.Count} modded survivor(s) for extra skills; found {survivors.Values.Sum(survivor => survivor.unlinked.Count)} outside their loadout states.");
        }

        private static ModdedSurvivor Describe(SurvivorDef survivorDef, BodyIndex bodyIndex)
        {
            GameObject body = BodyCatalog.GetBodyPrefab(bodyIndex);
            CharacterBody characterBody = BodyCatalog.GetBodyPrefabBodyComponent(bodyIndex);
            Type[] stateTypes = (BodyCatalog.GetBodyPrefabSkillSlots(bodyIndex) ?? Array.Empty<GenericSkill>())
                .Where(slot => slot && slot.skillFamily)
                .SelectMany(slot => slot.skillFamily.variants)
                .Select(variant => variant.skillDef ? variant.skillDef.activationState.stateType : null)
                .Where(type => type != null)
                .ToArray();
            IEnumerable<Type> componentTypes = body.GetComponentsInChildren<MonoBehaviour>(includeInactive: true)
                .Where(component => component)
                .Select(component => component.GetType());

            return new ModdedSurvivor
            {
                bodyIndex = bodyIndex,
                body = body,
                tokenStem = TokenStem(characterBody ? characterBody.baseNameToken : null) ?? TokenStem(survivorDef.displayNameToken),
                stateNamespaces = new HashSet<string>(stateTypes.Select(type => type.Namespace).Where(name => !string.IsNullOrEmpty(name))),
                assemblies = new HashSet<Assembly>(stateTypes.Concat(componentTypes).Select(type => type.Assembly).Where(IsModCode)),
            };
        }

        /// <summary>A skill that could be an extra one: named, and no loadout's choice.</summary>
        private static bool IsCandidate(SkillDef skill)
        {
            if (!skill || loadoutChoices.Contains(skill))
            {
                return false;
            }

            string token = skill.skillNameToken;
            return !string.IsNullOrEmpty(token) && Language.GetString(token) != token;
        }

        /// <summary>The one survivor the skill's name token starts with the stem of (the longest), or else whose states share the skill's namespace.</summary>
        private static ModdedSurvivor Owner(SkillDef skill, List<ModdedSurvivor> candidates)
        {
            List<ModdedSurvivor> byToken = candidates
                .Where(survivor => survivor.tokenStem != null && skill.skillNameToken.StartsWith(survivor.tokenStem, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(survivor => survivor.tokenStem.Length)
                .ToList();
            if (byToken.Count == 1 || (byToken.Count > 1 && byToken[0].tokenStem.Length > byToken[1].tokenStem.Length))
            {
                return byToken[0];
            }

            string stateNamespace = skill.activationState.stateType?.Namespace;
            List<ModdedSurvivor> byState = stateNamespace == null
                ? new List<ModdedSurvivor>()
                : candidates.Where(survivor => survivor.stateNamespaces.Contains(stateNamespace)).ToList();
            return byState.Count == 1 ? byState[0] : null;
        }

        /// <summary>"ROB_HENRY_BODY_NAME" → "ROB_HENRY_", "HENRY_NAME" → "HENRY_".</summary>
        private static string TokenStem(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            int end = token.LastIndexOf("BODY", StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                end = token.LastIndexOf("NAME", StringComparison.OrdinalIgnoreCase);
            }

            return end >= 3 ? token.Substring(0, end) : null;
        }

        /// <summary>The slot a field's name points to (primarySkillOverride → Primary), or None.</summary>
        private static SkillSlot SlotFromName(string name)
        {
            for (int i = 0; i < slotWords.Length; i++)
            {
                if (name.IndexOf(slotWords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return (SkillSlot)i;
                }
            }

            return SkillSlot.None;
        }

        /// <summary>The game's own packs; RoR2.LegacyModContent is where older mods put their content.</summary>
        private static bool IsVanilla(string identifier) =>
            identifier != null && identifier.StartsWith("RoR2.", StringComparison.Ordinal) && identifier != "RoR2.LegacyModContent";

        /// <summary>An assembly loaded from the plugins folder.</summary>
        private static bool IsModCode(Assembly assembly)
        {
            if (assembly.IsDynamic || assembly == typeof(ModdedExtraSkills).Assembly)
            {
                return false;
            }

            try
            {
                string location = assembly.Location;
                return !string.IsNullOrEmpty(location)
                    && System.IO.Path.GetFullPath(location).StartsWith(System.IO.Path.GetFullPath(Paths.PluginPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Skills a state names in its own fields (it and its base types up to <see cref="EntityState"/>): static
        /// fields, and instance fields as its entity state configuration sets them.
        /// </summary>
        private static (SkillDef skill, SkillSlot slot)[] SkillsNamedBy(Type stateType)
        {
            if (stateType == null)
            {
                return Array.Empty<(SkillDef, SkillSlot)>();
            }

            if (skillsNamedByState.TryGetValue(stateType, out var cached))
            {
                return cached;
            }

            List<(SkillDef, SkillSlot)> named = new List<(SkillDef, SkillSlot)>();
            try
            {
                List<FieldInfo> fields = new List<FieldInfo>();
                for (Type type = stateType; type != null && type != typeof(EntityState); type = type.BaseType)
                {
                    fields.AddRange(type
                        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(field => OverrideCallScanner.HoldsSkills(field.FieldType)));
                }

                // A fresh state gets its configuration in the EntityState constructor; it is never entered.
                EntityState state = fields.Any(field => !field.IsStatic) ? EntityStateCatalog.InstantiateState(stateType) : null;
                foreach (FieldInfo field in fields)
                {
                    object value = field.IsStatic ? field.GetValue(null) : state != null ? field.GetValue(state) : null;
                    named.AddRange(Skills(value).Select(skill => (skill, SlotFromName(field.Name))));
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Could not read the skills {stateType.FullName} names: {e.Message}");
            }

            var result = named.Where(entry => entry.Item1).Distinct().ToArray();
            skillsNamedByState[stateType] = result;
            return result;
        }

        /// <summary>
        /// Skills in a component's own instance fields, and one level into the data it holds (structs or plain
        /// classes, alone or in arrays and lists, such as vanilla's skill → override pairs).
        /// </summary>
        private static IEnumerable<(SkillDef skill, SkillSlot slot)> SkillsIn(MonoBehaviour component)
        {
            const BindingFlags instanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            List<(SkillDef, SkillSlot)> skills = new List<(SkillDef, SkillSlot)>();
            try
            {
                for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(instanceFields | BindingFlags.DeclaredOnly))
                    {
                        if (OverrideCallScanner.HoldsSkills(field.FieldType))
                        {
                            skills.AddRange(Skills(field.GetValue(component)).Select(skill => (skill, SlotFromName(field.Name))));
                            continue;
                        }

                        Type dataType = DataType(field.FieldType);
                        FieldInfo[] skillFields = dataType?.GetFields(instanceFields).Where(inner => OverrideCallScanner.HoldsSkills(inner.FieldType)).ToArray();
                        if (skillFields == null || skillFields.Length == 0)
                        {
                            continue;
                        }

                        object value = field.GetValue(component);
                        IEnumerable items = value is IEnumerable list ? list : new[] { value };
                        foreach (object item in items)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            foreach (FieldInfo inner in skillFields)
                            {
                                SkillSlot slot = SlotFromName(inner.Name);
                                skills.AddRange(Skills(inner.GetValue(item)).Select(skill => (skill, slot != SkillSlot.None ? slot : SlotFromName(field.Name))));
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Debug($"Could not read the skills {component.GetType().FullName} names: {e.Message}");
            }

            return skills;
        }

        /// <summary>A struct or plain class a field holds, alone or as the element of an array or list; null for anything else.</summary>
        private static Type DataType(Type type)
        {
            Type element = type.IsArray ? type.GetElementType()
                : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) ? (type.GetGenericArguments().Length == 1 ? type.GetGenericArguments()[0] : null)
                : type;
            bool isData = element != null && !element.IsPrimitive && !element.IsEnum && element != typeof(string)
                && !typeof(UnityEngine.Object).IsAssignableFrom(element) && !typeof(Delegate).IsAssignableFrom(element)
                && (element.IsValueType || element.IsClass) && element.Namespace?.StartsWith("System", StringComparison.Ordinal) != true
                && element.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) != true;
            return isData ? element : null;
        }

        private static IEnumerable<SkillDef> Skills(object value) =>
            value is SkillDef skill ? new[] { skill }
            : value is IEnumerable list ? list.OfType<SkillDef>()
            : Enumerable.Empty<SkillDef>();
    }
}
