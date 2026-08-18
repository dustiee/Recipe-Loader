using BepInEx;
using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using static RecipeLoader.LogTools;
using System.Threading;

namespace RecipeLoader;

/// <summary>
/// Loads recipes from nearby directories into the game.
/// </summary>
[BepInPlugin("dev.dustie.recipeloader", "RecipeLoader", "1.0.0")]
public class RecipeLoader : BaseUnityPlugin
{
  internal static ManualLogSource? Log;

  private static ConfigEntry<int>? _configMaxSaneHiddenRecipesPerRecipe;
  private static ConfigEntry<int>? _configMaxHiddenRecipesTotal;
  private static ConfigEntry<bool>? _configItemCountWarn;
  private static ConfigEntry<bool>? _configQuieterTests;
  private static ConfigEntry<bool>? _configMuteDebug;


  internal static int MaxSaneHiddenRecipesPerRecipe
  {
    get
    {
      if (_configMaxSaneHiddenRecipesPerRecipe?.Value == null)
      {
        return 0;
      }
      if (_configMaxSaneHiddenRecipesPerRecipe.Value == -1)
      {
        return int.MaxValue;
      }
      return _configMaxSaneHiddenRecipesPerRecipe.Value;
    }
  }

  internal static int MaxHiddenRecipesTotal
  {
    get
    {
      if (_configMaxHiddenRecipesTotal?.Value == null)
      {
        return 0;
      }
      if (_configMaxHiddenRecipesTotal.Value == -1)
      {
        return int.MaxValue;
      }
      return _configMaxHiddenRecipesTotal.Value;
    }
  }

  internal static bool ItemCountWarn
  {
    get => (_configItemCountWarn?.Value == null) || _configItemCountWarn.Value;
  }

  internal static bool QuieterTests
  {
    get => (_configQuieterTests?.Value == null) || _configQuieterTests.Value;
  }

  internal static bool MuteDebug
  {
    get => (_configMuteDebug?.Value == null ? false : _configMuteDebug.Value);
  }



  private void Awake()
  {
    Log = Logger;

    Harmony harmony = new("dev.dustie.recipeloader");
    harmony.PatchAll();

    // Directives get collected when InvDatabase is ready, see patch below

    Configure();
  }


  private void Configure()
  {

    _configMaxSaneHiddenRecipesPerRecipe = Config.Bind(
        "Options",
        "Max Hidden Recipes Per Recipe Before Warning",
        1500,
        "The max amount of hidden recipes a single recipe using groups can be expanded out into before logging a warning.\n" +
        "Set to -1 to use the maximum value for int (2,147,483,647) "
        );

    _configMaxHiddenRecipesTotal = Config.Bind(
        "Options",
        "Max Hidden Recipes in Total",
        40_000,
        "The max amount of hidden recipes that can be generated overall.\n" +
        "40,000 is reasonable enough. Any more than that and the recipe parsing has a very noticable impact during loading.\n" +
        "Set to -1 to use the maximum value for int (2,147,483,647) "
        );


    _configItemCountWarn = Config.Bind(
        "Options",
        "Item Count Warn",
        true,
        "Logs a warning if a recipe has items that are missing count or exactCount attributes."
        );

    _configQuieterTests = Config.Bind(
        "Options",
        "Quieter Tests",
        true,
        "Decreases the amount of spam in the debug channel from automatic group validity tests. Only prints results instead."
        );

    _configMuteDebug = Config.Bind(
        "Options",
        "Mute Debug",
        true,
        "Recipe Loader will no longer output anything to the debug channel. Warnings, Errors and Info is still printed."
        );



  }

}

[HarmonyPatch(typeof(InvDatabase), "Awake")]
[HarmonyPriority(Priority.Last)]
internal static class PrepareDirectives
{
  private static int _databases = 0;
  [HarmonyPostfix]
  internal static void Patch(InvDatabase __instance)
  {
    int count = Interlocked.Increment(ref _databases);
    if (count == InvDatabase.list.Length) // All databases initialized
    {
      DirectiveCollector.PopulateRecipeDirectiveLists();
    }

  }
}
