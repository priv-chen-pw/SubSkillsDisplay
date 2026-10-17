using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityStates;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace SubSkillsDisplay
{
    /// <summary>
    /// The skills a mod's code swaps in: <see cref="SkillOverrideSites"/> finds where the code loads them from,
    /// and this reads them there, without running the mod's code beyond getters and fresh entity states.
    /// Found this way, a skill is known to replace skills, unlike one the mod merely creates.
    /// </summary>
    internal static class OverrideCallScanner
    {
        /// <summary>A skill the code swaps in, the slot it replaces (None if unknown), and the body it was read from when that is known.</summary>
        internal readonly struct Found
        {
            internal readonly SkillDef skill;
            internal readonly SkillSlot slot;
            internal readonly GameObject body;

            internal Found(SkillDef skill, SkillSlot slot, GameObject body)
            {
                this.skill = skill;
                this.slot = slot;
                this.body = body;
            }
        }

        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <param name="bodies">The survivor bodies whose components instance fields are read from.</param>
        internal static List<Found> Scan(Assembly assembly, GameObject[] bodies)
        {
            List<Found> found = new List<Found>();
            using (ModuleDefinition module = ModuleDefinition.ReadModule(assembly.Location))
            {
                foreach (SkillOverrideSites.Site site in SkillOverrideSites.Find(module))
                {
                    try
                    {
                        SkillSlot slot = Enum.TryParse(site.slot, out SkillSlot parsed) ? parsed : SkillSlot.None;
                        Read(assembly, bodies, site.load, slot, found);
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"Could not read a skill override in {site.method.FullName}: {e.Message}");
                    }
                }
            }

            return found;
        }

        private static void Read(Assembly assembly, GameObject[] bodies, Instruction load, SkillSlot slot, List<Found> found)
        {
            switch (load.OpCode.Code)
            {
                case Code.Ldsfld:
                    Add(ToReflection(assembly, load.Operand as FieldReference)?.GetValue(null), slot, null, found);
                    break;

                case Code.Ldfld:
                    FieldInfo field = ToReflection(assembly, load.Operand as FieldReference);
                    if (field != null)
                    {
                        ReadFromInstances(assembly, bodies, field.DeclaringType, instance => field.GetValue(instance), slot, found);
                    }

                    break;

                default:
                    // A getter: a property, or a parameterless method returning the skill.
                    MethodInfo getter = ToReflection(assembly, load.Operand as MethodReference);
                    if (getter != null && getter.GetParameters().Length == 0 && HoldsSkills(getter.ReturnType))
                    {
                        if (getter.IsStatic)
                        {
                            Add(getter.Invoke(null, null), slot, null, found);
                        }
                        else
                        {
                            ReadFromInstances(assembly, bodies, getter.DeclaringType, instance => getter.Invoke(instance, null), slot, found);
                        }
                    }

                    break;
            }
        }

        /// <summary>
        /// Instance values: from a fresh entity state (its configuration applied in the constructor), or from
        /// each matching component on the survivor bodies, remembering which body it came from.
        /// </summary>
        private static void ReadFromInstances(Assembly assembly, GameObject[] bodies, Type type, Func<object, object> read, SkillSlot slot, List<Found> found)
        {
            if (typeof(EntityState).IsAssignableFrom(type))
            {
                IEnumerable<Type> stateTypes = type.IsAbstract
                    ? assembly.GetTypes().Where(candidate => !candidate.IsAbstract && type.IsAssignableFrom(candidate))
                    : new[] { type };
                foreach (Type stateType in stateTypes)
                {
                    Add(read(EntityStateCatalog.InstantiateState(stateType)), slot, null, found);
                }
            }
            else if (typeof(Component).IsAssignableFrom(type))
            {
                foreach (GameObject body in bodies)
                {
                    foreach (Component component in body.GetComponentsInChildren(type, includeInactive: true))
                    {
                        Add(read(component), slot, body, found);
                    }
                }
            }
        }

        private static void Add(object value, SkillSlot slot, GameObject body, List<Found> found)
        {
            if (value is SkillDef skill)
            {
                found.Add(new Found(skill, slot, body));
            }
            else if (value is IEnumerable list)
            {
                found.AddRange(list.OfType<SkillDef>().Where(item => item).Select(item => new Found(item, slot, body)));
            }
        }

        private static FieldInfo ToReflection(Assembly assembly, FieldReference field) =>
            field == null ? null : ToReflection(assembly, field.DeclaringType)?.GetField(field.Name, Declared);

        private static MethodInfo ToReflection(Assembly assembly, MethodReference method)
        {
            Type type = method == null ? null : ToReflection(assembly, method.DeclaringType);
            return type?.GetMethods(Declared)
                .FirstOrDefault(candidate => candidate.Name == method.Name && candidate.GetParameters().Length == method.Parameters.Count);
        }

        /// <summary>The runtime type of a type reference, looked up in the mod and then every loaded assembly.</summary>
        private static Type ToReflection(Assembly assembly, TypeReference type)
        {
            if (type == null || type.IsGenericInstance)
            {
                return null;
            }

            string name = type.FullName.Replace('/', '+');
            return assembly.GetType(name)
                ?? AppDomain.CurrentDomain.GetAssemblies().Select(candidate => candidate.GetType(name)).FirstOrDefault(match => match != null);
        }

        internal static bool HoldsSkills(Type type) =>
            typeof(SkillDef).IsAssignableFrom(type)
            || (type.IsArray && typeof(SkillDef).IsAssignableFrom(type.GetElementType()))
            || (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) && type.GetGenericArguments().Any(typeof(SkillDef).IsAssignableFrom));
    }
}
