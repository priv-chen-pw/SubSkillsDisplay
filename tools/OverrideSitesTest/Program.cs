using System;
using System.Linq;
using Mono.Cecil;
using SubSkillsDisplay;

// Prints every skill load SkillOverrideSites finds in an assembly: method, slot, opcode, operand.
var path = args[0];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(path));
using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
var stopwatch = System.Diagnostics.Stopwatch.StartNew();
var sites = SkillOverrideSites.Find(module);
Console.WriteLine($"{sites.Count} loads in {stopwatch.ElapsedMilliseconds} ms");
foreach (var site in sites.OrderBy(s => s.method.DeclaringType.FullName))
    Console.WriteLine($"{site.method.DeclaringType.FullName}::{site.method.Name}  [{site.slot ?? "?"}]  {site.load.OpCode} {site.load.Operand}");
