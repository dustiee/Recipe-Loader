using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Xml.Serialization;
using HarmonyLib;

using static RecipeLoader.LogTools;

namespace RecipeLoader;

// Forces parse to use a cached XmlSerializer instead of making a new one single time
// This patch doesn't do anything other than making the method more performant
//
// All it does it change this:
// RecipeXml recipeXml = (RecipeXml)new XmlSerializer(typeof(RecipeXml)).Deserialize(text);
// into effectively
// RecipeXml recipeXml = (RecipeXml)_cachedSerializer.Deserialize(text)

// ...and it makes the entire execution of GetBuiltinRecipes about 20% faster.
//  From testing on my device with 2 recipes generating ~40,000 extra recipes:
//  Old   | With transpiler
// 3.366s | 2.719s  
// 3.426s | 2.68s   


[HarmonyPatch(typeof(RecipeManager))]
[HarmonyPatch(
    typeof(RecipeManager),
    nameof(RecipeManager.parse),
    [typeof(TextReader)]
)]
internal static class RecipeManager_parse_optimizationPatch
{
  private static readonly XmlSerializer _cachedSerializer = new(typeof(RecipeXml));
  private static XmlSerializer GetCachedSerializer() => _cachedSerializer;

  static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
  {
    Debug("Running Transpiler...");
    try
    {

      CodeMatcher matcher = new(instructions, generator);
      matcher.Start();

      return matcher.MatchForward(
           false,
       new CodeMatch(OpCodes.Ldtoken, typeof(RecipeXml)),
       new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Type), nameof(Type.GetTypeFromHandle))),
       new CodeMatch(OpCodes.Newobj, AccessTools.Constructor(typeof(XmlSerializer), [typeof(Type)])),
       new CodeMatch(OpCodes.Ldarg_1),
       new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(XmlSerializer), nameof(XmlSerializer.Deserialize), [typeof(TextReader)])),
       new CodeMatch(OpCodes.Castclass, typeof(RecipeXml)))
       .ThrowIfNotMatch("Could not find XmlSerializer block")
       .RemoveInstructions(6)
   .Insert(
       new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RecipeManager_parse_optimizationPatch), nameof(GetCachedSerializer))),
       new CodeInstruction(OpCodes.Ldarg_1),
       new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(XmlSerializer), nameof(XmlSerializer.Deserialize), [typeof(TextReader)])),
       new CodeInstruction(OpCodes.Castclass, typeof(RecipeXml))
   )
   .InstructionEnumeration();
    }
    catch (Exception ex)
    {
      Fatal($"Transpiler failed: {ex}");
      return instructions;
    }

  }
}
