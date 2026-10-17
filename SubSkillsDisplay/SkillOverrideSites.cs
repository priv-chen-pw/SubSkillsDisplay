using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Finds, in compiled code, where the skills a module swaps in come from: for every call to
    /// <c>GenericSkill.SetSkillOverride</c> and every store into a vanilla override holder, the instruction that
    /// loads the skill (a field, or a getter), followed back through wrapper methods and array indexing, and the
    /// slot it goes into when that can be told. Only reads IL, so it is testable outside the game;
    /// <see cref="OverrideCallScanner"/> reads the values.
    /// </summary>
    internal static class SkillOverrideSites
    {
        /// <summary>A place a swapped-in skill is loaded from, and the slot it replaces ("Primary"...), or null if unknown.</summary>
        internal readonly struct Site
        {
            internal readonly MethodDefinition method;
            internal readonly Instruction load;
            internal readonly string slot;

            internal Site(MethodDefinition method, Instruction load, string slot)
            {
                this.method = method;
                this.load = load;
                this.slot = slot;
            }
        }

        /// <summary>Vanilla fields a mod can fill to have vanilla code swap the skill in, with the slot each fills.</summary>
        private static readonly Dictionary<string, string> overrideHolderFields = new Dictionary<string, string>
        {
            ["EntityStates.SkillStateOverrideData::primarySkillOverride"] = "Primary",
            ["EntityStates.SkillStateOverrideData::secondarySkillOverride"] = "Secondary",
            ["EntityStates.SkillStateOverrideData::utilitySkillOverride"] = "Utility",
            ["EntityStates.SkillStateOverrideData::specialSkillOverride"] = "Special",
            ["RoR2.SkillTransformationController/SkillDefRelationship::overrideSkillDef"] = null,
        };

        private static readonly string[] slotNames = { "Primary", "Secondary", "Utility", "Special" };

        /// <summary>How many wrapper methods a skill is followed back through.</summary>
        private const int MaximumWrapperDepth = 2;

        /// <summary>Where the skills the module swaps in are loaded from: ldsfld, ldfld, call or callvirt.</summary>
        internal static List<Site> Find(ModuleDefinition module)
        {
            List<MethodDefinition> methods = module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody).ToList();

            // Each of the module's methods with the calls to it, to follow a skill through wrapper methods.
            Dictionary<MethodDefinition, List<(MethodDefinition caller, Instruction call)>> callSites =
                new Dictionary<MethodDefinition, List<(MethodDefinition, Instruction)>>();
            foreach (MethodDefinition method in methods)
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    // A call to a method of this module carries the method's definition itself.
                    if (IsCall(instruction) && instruction.Operand is MethodDefinition target)
                    {
                        if (!callSites.TryGetValue(target, out List<(MethodDefinition, Instruction)> calls))
                        {
                            callSites[target] = calls = new List<(MethodDefinition, Instruction)>();
                        }

                        calls.Add((method, instruction));
                    }
                }
            }

            List<Site> sites = new List<Site>();
            foreach (MethodDefinition method in methods)
            {
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    // slot.SetSkillOverride(object source, SkillDef skillDef, SkillOverridePriority priority): the
                    // skill is one below the top of the stack, the slot three.
                    if (IsCall(instruction) && instruction.Operand is MethodReference called
                        && called.Name == "SetSkillOverride" && called.DeclaringType.FullName == "RoR2.GenericSkill")
                    {
                        Follow(method, instruction, 1, 3, null, callSites, 0, sites);
                    }
                    else if (instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference field
                        && overrideHolderFields.TryGetValue(field.DeclaringType.FullName + "::" + field.Name, out string slot))
                    {
                        Follow(method, instruction, 0, -1, slot, callSites, 0, sites);
                    }
                }
            }

            return sites.GroupBy(site => (site.method, site.load)).Select(group => group.First()).ToList();
        }

        /// <summary>
        /// Records where the skill <paramref name="skillFromTop"/> places below the top before
        /// <paramref name="consumer"/> is loaded from, with its slot: <paramref name="knownSlot"/>, or else read from
        /// the slot instance <paramref name="slotFromTop"/> places below the top (-1: none).
        /// </summary>
        private static void Follow(MethodDefinition method, Instruction consumer, int skillFromTop, int slotFromTop, string knownSlot,
            Dictionary<MethodDefinition, List<(MethodDefinition caller, Instruction call)>> callSites, int depth, List<Site> sites)
        {
            string slot = knownSlot;
            int slotParameter = -1;
            if (slot == null && slotFromTop >= 0)
            {
                slot = SlotAt(method, consumer, slotFromTop, out slotParameter);
            }

            foreach (Instruction producer in Producers(method, consumer, skillFromTop))
            {
                switch (producer.OpCode.Code)
                {
                    case Code.Ldsfld:
                    case Code.Ldfld:
                    case Code.Call:
                    case Code.Callvirt:
                        sites.Add(new Site(method, producer, slot));
                        break;

                    case Code.Ldelem_Ref:
                        // array[index]: the array, whose every skill counts.
                        Follow(method, producer, 1, -1, slot, callSites, depth, sites);
                        break;

                    default:
                        // A parameter of a wrapper method: what its callers pass in, with the slot they pass if
                        // the slot is a parameter too.
                        int parameter = ParameterIndex(method, producer);
                        if (parameter >= 0 && depth < MaximumWrapperDepth && callSites.TryGetValue(method, out List<(MethodDefinition caller, Instruction call)> calls))
                        {
                            int count = method.Parameters.Count;
                            foreach ((MethodDefinition caller, Instruction call) in calls)
                            {
                                Follow(caller, call, count - 1 - parameter, slot == null && slotParameter >= 0 ? count - 1 - slotParameter : -1,
                                    slot, callSites, depth + 1, sites);
                            }
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// The slot of the GenericSkill <paramref name="fromTop"/> places below the top before
        /// <paramref name="consumer"/>: a GenericSkill field named after its slot (<c>skillLocator.primary</c>), or
        /// <c>skillLocator.GetSkill(SkillSlot.X)</c>. Null if unknown or ambiguous; <paramref name="parameter"/> is
        /// set when the slot is a parameter of the method.
        /// </summary>
        private static string SlotAt(MethodDefinition method, Instruction consumer, int fromTop, out int parameter)
        {
            parameter = -1;
            string slot = null;
            foreach (Instruction producer in Producers(method, consumer, fromTop))
            {
                string found = null;
                if ((producer.OpCode.Code == Code.Ldfld || producer.OpCode.Code == Code.Ldsfld) && producer.Operand is FieldReference field
                    && field.FieldType.FullName == "RoR2.GenericSkill")
                {
                    found = slotNames.FirstOrDefault(name => field.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (IsCall(producer) && producer.Operand is MethodReference called && called.Name == "GetSkill"
                    && called.DeclaringType.FullName == "RoR2.SkillLocator")
                {
                    int index = Producers(method, producer, 0).Select(ConstantInt).FirstOrDefault(value => value.HasValue) ?? -1;
                    found = index >= 0 && index < slotNames.Length ? slotNames[index] : null;
                }
                else
                {
                    parameter = ParameterIndex(method, producer);
                }

                if (found == null)
                {
                    return null;
                }

                if (slot != null && slot != found)
                {
                    return null;
                }

                slot = found;
            }

            return slot;
        }

        private static int? ConstantInt(Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1: return -1;
                case Code.Ldc_I4_0: return 0;
                case Code.Ldc_I4_1: return 1;
                case Code.Ldc_I4_2: return 2;
                case Code.Ldc_I4_3: return 3;
                case Code.Ldc_I4_S: return (sbyte)instruction.Operand;
                case Code.Ldc_I4: return (int)instruction.Operand;
                default: return null;
            }
        }

        /// <summary>
        /// The instructions that may have pushed the value <paramref name="fromTop"/> places below the top of the
        /// stack just before <paramref name="consumer"/>. Walks back over each instruction's stack effect, along
        /// every path into a merge point, so both values of a conditional are found.
        /// </summary>
        internal static List<Instruction> Producers(MethodDefinition method, Instruction consumer, int fromTop)
        {
            // Where each instruction can be jumped to from.
            Dictionary<Instruction, List<Instruction>> jumpsTo = new Dictionary<Instruction, List<Instruction>>();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                IEnumerable<Instruction> targets = instruction.Operand is Instruction target ? new[] { target }
                    : instruction.Operand as Instruction[] ?? Enumerable.Empty<Instruction>();
                foreach (Instruction jumpTarget in targets)
                {
                    if (!jumpsTo.TryGetValue(jumpTarget, out List<Instruction> sources))
                    {
                        jumpsTo[jumpTarget] = sources = new List<Instruction>();
                    }

                    sources.Add(instruction);
                }
            }

            List<Instruction> producers = new List<Instruction>();
            HashSet<(Instruction, int)> visited = new HashSet<(Instruction, int)>();
            Stack<(Instruction after, int above)> pending = new Stack<(Instruction, int)>();
            pending.Push((consumer, fromTop));
            while (pending.Count > 0 && visited.Count < 256)
            {
                (Instruction after, int above) = pending.Pop();
                if (!visited.Add((after, above)))
                {
                    continue;
                }

                // What runs just before: the previous instruction unless it never falls through, and every jump here.
                List<Instruction> predecessors = new List<Instruction>();
                Instruction previous = after.Previous;
                if (previous != null && previous.OpCode.FlowControl != FlowControl.Branch
                    && previous.OpCode.FlowControl != FlowControl.Return && previous.OpCode.FlowControl != FlowControl.Throw)
                {
                    predecessors.Add(previous);
                }

                if (jumpsTo.TryGetValue(after, out List<Instruction> jumps))
                {
                    predecessors.AddRange(jumps);
                }

                foreach (Instruction instruction in predecessors)
                {
                    int pushes = Pushes(instruction);
                    int pops = Pops(instruction);
                    if (above < pushes)
                    {
                        producers.Add(instruction);
                    }
                    else if (pops >= 0)
                    {
                        pending.Push((instruction, above + pops - pushes));
                    }
                }
            }

            return producers;
        }

        private static int Pushes(Instruction instruction)
        {
            switch (instruction.OpCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 0;
                case StackBehaviour.Push1_push1:
                    return 2;
                case StackBehaviour.Varpush:
                    return instruction.Operand is MethodReference method && method.ReturnType.MetadataType != MetadataType.Void ? 1 : 0;
                default:
                    return 1;
            }
        }

        /// <summary>How many values the instruction takes, or -1 where the walk can't continue.</summary>
        private static int Pops(Instruction instruction)
        {
            switch (instruction.OpCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    return 0;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                    return 1;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                    return 2;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;
                case StackBehaviour.Varpop:
                    if (instruction.Operand is MethodReference method)
                    {
                        return method.Parameters.Count + (method.HasThis && instruction.OpCode.Code != Code.Newobj ? 1 : 0);
                    }

                    return -1;
                default:
                    return -1;
            }
        }

        private static bool IsCall(Instruction instruction) =>
            instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt;

        /// <summary>The parameter <paramref name="load"/> loads (ldarg), counting from 0 without "this", or -1.</summary>
        private static int ParameterIndex(MethodDefinition method, Instruction load)
        {
            int index;
            switch (load.OpCode.Code)
            {
                case Code.Ldarg_0: index = 0; break;
                case Code.Ldarg_1: index = 1; break;
                case Code.Ldarg_2: index = 2; break;
                case Code.Ldarg_3: index = 3; break;
                case Code.Ldarg_S:
                case Code.Ldarg:
                    return load.Operand is ParameterDefinition parameter ? parameter.Index : -1;
                default:
                    return -1;
            }

            return method.HasThis ? index - 1 : index;
        }
    }
}
