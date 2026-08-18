using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HarmonyLib;
using UnityEngine;

using static RecipeLoader.API;
using static RecipeLoader.LogTools;
using static RecipeLoader.DirectiveCollector;
using System.Diagnostics;

namespace RecipeLoader;


[HarmonyPatch(typeof(RecipeManager), nameof(RecipeManager.AddBuiltinRecipes))]
[HarmonyPriority(Priority.Last)]
internal class RecipeManager_BuiltInAdd_Prefix
{
  [HarmonyPrefix]
  static void Prefix(RecipeManager __instance, out (Stopwatch?, Stopwatch?) __state)
  {
    Stopwatch fullRecipeManagerRun = new();
    fullRecipeManagerRun.Start();

    Debug("Trying to add recipes to a Recipe Manager...");
    StationType? station = InferStation(__instance.categorizedRecipes);
    if (station == null)
    {
      Warn("Got a null station!");
      __state = (null, null);
      return;
    }

    Stopwatch timeModification = new();
    timeModification.Start();

    ForceAllCategoriesAndOrder(__instance.categorizedRecipes);
    Debug($"Using station {station}");

    List<RecipeManager.CategorizedRecipes> modifiedCatRecipesCollection = [];
    List<TextAsset> hiddenRecipesFromGroups = [];

    // NOTE: "Hide" is ordered last here by ForceAllCategoriesAndOrder so we build a list of hidden recipes 
    // and add them once we reach Hide, since then we've went through all the recipes for this station
    foreach (RecipeManager.CategorizedRecipes cRecipes in __instance.categorizedRecipes)
    {

      // PERF:
      // Make directives be grouped by StationCategoryPair / Specifier and use a dictionary  so we don't go
      // over every directive for every category and station here.
      // This naive implemention works OK but could cause issues if we need to support a large amount of directives.


      InvBaseItem.CreativeCategory category = cRecipes.category;
      StationCategoryPair statCatPair = new(station.Value, category);
      List<TextAsset> recipeList = [];

      // Get base game recipes after:
      // deletions
      // replacements (set replacement flag to true if replaced to insert them later)
      // insert overwrites
      foreach (TextAsset xmlAsset in cRecipes.recipes)
      {
        string? name = GetNameFromIngameRecipe(xmlAsset);
        if (name == null)
        {
          Warn($"Got a null name @ {station} @ {category}");
          continue;
        }

        RecipeSpecifier specifier = new(name, statCatPair);
        // Deletions
        if (DeleteRecipeDirectives.Any(d => d.Specifier == specifier))
        {
          continue;
        }
        // Replacements
        ReplacementRecipeDirective? replacement =
          ReplacementRecipeDirectives.FirstOrDefault(r => r.Specifier == specifier);
        if (replacement != null)
        {
          replacement.DoneReplacement = true;
          continue;
        }
        // Overwrites 
        if (InsertRecipeDirectives.Any(i => (i.Overwrite == true) && (i.Specifier == specifier)))
        {
          continue;
        }

        recipeList.Add(xmlAsset);
      }

      // Add replacements and inserted recipes

      // Replacements
      foreach (ReplacementRecipeDirective replacement in ReplacementRecipeDirectives)
      {
        if (replacement.DoneReplacement != true)
        {
          continue;
        }
        if (replacement.Specifier.StationCategory != statCatPair)
        {
          continue;
        }

        recipeList.Add(new TextAsset(replacement.GetCanonicalRecipeXmlString()));
        List<string>? hiddens = replacement.GetNonCanonicalRecipeXmlStrings();
        if (hiddens != null)
        {
          hiddenRecipesFromGroups.AddRange(hiddens.Select(h => new TextAsset(h)));
        }
      }
      // ReplacementRecipeDirectives.RemoveAll(
      //     replacement =>
      //         replacement.DoneReplacement == true &&
      //         replacement.Specifier.StationCategory == statCatPair
      // ); 

      // Inserts 
      HashSet<RecipeSpecifier> doneInserts = [];
      foreach (InsertRecipeDirective insert in InsertRecipeDirectives)
      {
        if (insert.Specifier.StationCategory != statCatPair)
        {
          continue;
        }
        doneInserts.Add(insert.Specifier);
        recipeList.Add(new TextAsset(insert.GetCanonicalRecipeXmlString()));
        Stopwatch watch = new();
        watch.Start();
        List<string>? hiddens = insert.GetNonCanonicalRecipeXmlStrings();
        if (hiddens != null)
        {
          EndStopwatchAndDebugPrint(watch, $"Generation of {hiddens.Count} hiddens took: ");
          hiddenRecipesFromGroups.AddRange(hiddens.Select(h => new TextAsset(h)));
        }
        else
        {
          watch.Stop();
        }
      }
      // InsertRecipeDirectives = [
      //     .. InsertRecipeDirectives.Where(i => !doneInserts.Contains(i.Specifier))
      // ];

      // Hiddens 
      if (category == InvBaseItem.CreativeCategory.Hide)
      {
        recipeList.AddRange(hiddenRecipesFromGroups);
      }

      cRecipes.recipes = [.. recipeList];

    }// foreach categorizedRecipes

    ClearNameCache();
    EndStopwatchAndDebugPrint(timeModification, "Modified RecipeManager in");

    Stopwatch builtinRecipeManagerRun = new();
    builtinRecipeManagerRun.Start();

    __state = (fullRecipeManagerRun, builtinRecipeManagerRun);

    return;

  }

  [HarmonyPostfix]
  static void Postfix(RecipeManager __instance, (Stopwatch?, Stopwatch?) __state)
  {
#pragma warning disable Harmony003 // Harmony non-ref patch parameters modified
    if (__state.Item1 == null || __state.Item2 == null)
    {
      return;
    }
#pragma warning restore Harmony003 // Harmony non-ref patch parameters modified

    Debug("\n=== Timing results: ===");
    (Stopwatch full, Stopwatch builtin) = __state;

    EndStopwatchAndDebugPrint(full, "Total recipe manager run took: ");
    EndStopwatchAndDebugPrint(builtin, "Total recipe manager run took (Base execution only): ");
    Debug("\n===  ===");
  }




  private static readonly Dictionary<TextAsset, string?> _nameCache = [];

  private static string? GetNameFromIngameRecipe(TextAsset textAsset)
  {
    if (_nameCache.TryGetValue(textAsset, out string? cachedName))
      return cachedName;

    string text = textAsset.text;
    var cleaned = new StringBuilder(text.Length);

    foreach (char c in text)
    {
      if (XmlConvert.IsXmlChar(c))
        cleaned.Append(c);
    }

    text = cleaned.ToString();

    XDocument document;
    try
    {
      document = XDocument.Parse(text);
    }
    catch
    {
      _nameCache[textAsset] = null;
      return null;
    }

    string? name = document.Root?.Attribute("name")?.Value?.Trim();

    _nameCache[textAsset] = name;
    return name;
  }

  private static void ClearNameCache()
  {
    _nameCache.Clear();
  }

  private static void ForceAllCategoriesAndOrder(
      List<RecipeManager.CategorizedRecipes> categorizedRecipes)
  {
    HashSet<InvBaseItem.CreativeCategory> existing =
        [.. categorizedRecipes.Select(c => c.category)];

    // Add missing categories.
    foreach (InvBaseItem.CreativeCategory category in
             Enum.GetValues(typeof(InvBaseItem.CreativeCategory)))
    {
      if (!existing.Contains(category))
      {
        categorizedRecipes.Add(
            new RecipeManager.CategorizedRecipes
            {
              category = category,
              recipes = []
            }
        );
      }
    }

    // Force Hide to be last.
    int hideIndex = categorizedRecipes.FindIndex(
        c => c.category == InvBaseItem.CreativeCategory.Hide
    );

    if (hideIndex >= 0 && hideIndex != categorizedRecipes.Count - 1)
    {
      RecipeManager.CategorizedRecipes hide = categorizedRecipes[hideIndex];

      categorizedRecipes.RemoveAt(hideIndex);
      categorizedRecipes.Add(hide);
    }
  }

}


