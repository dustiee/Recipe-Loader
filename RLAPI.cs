using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using UnityEngine;
using static RecipeLoader.LogTools;

namespace RecipeLoader;

// NOTE:
//I don't really intend for Recipe Loader to have much of a public API, the methods here are exposed because they can be useful
//for some really specific things like the General Exporter utility to convert recips into Recipe Loader ones,
//but aside from that the main purpose of Recipe Loader is to load pre-made recipes in specified.
// directories.It's not supposed to interact with other plugins very much at run-time.
//
// NOTE:
// Q: Why am I inferring station categories? They're paired with stations in Achievement Manager.
// A: Because I need to know what station we're using when Recipe Manager is instantiated, which is before Achievement
// Manager. Patching AddBuiltInRecipes with the inference system seems much easier since we can remove recipes 
// before they get parsed and add our own, so we get existing logic to handle those and dont need to manually remove 
// recipes later from matchers, lists and categories. There may be a better way of doing this though!
// NOW, my question is why does AchievementManager contain RecipeManagers, and why RecipeManagers don't store 
// their own category, and why the AchievementManager contains a class called "Recipe Data" that actually stores 
// Recipe Managers and their "category", and why there's four recipe managers being instantiated,

/// <summary>
/// Contains methods for:
///  - Inferring station types 
///  - Mapping between StationType and string 
///  - Converting TextAsset recipes into RecipeLoader specific xmls
/// </summary>
public class API
{
  private static readonly Dictionary<string, InvBaseItem.CreativeCategory> _categoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "blocks", InvBaseItem.CreativeCategory.Blocks },
        { "block", InvBaseItem.CreativeCategory.Blocks },
        { "items", InvBaseItem.CreativeCategory.Items },
        { "item", InvBaseItem.CreativeCategory.Items },
        { "weapons", InvBaseItem.CreativeCategory.Weapons },
        { "weapon", InvBaseItem.CreativeCategory.Weapons },
        { "tools", InvBaseItem.CreativeCategory.Tools },
        { "tool", InvBaseItem.CreativeCategory.Tools },
        { "armor", InvBaseItem.CreativeCategory.Armor },
        { "armors", InvBaseItem.CreativeCategory.Armor },
        { "animals", InvBaseItem.CreativeCategory.Animals },
        { "animal", InvBaseItem.CreativeCategory.Animals },
        { "plants", InvBaseItem.CreativeCategory.Plants },
        { "plant", InvBaseItem.CreativeCategory.Plants },
        { "vehicles", InvBaseItem.CreativeCategory.Vehicles },
        { "vehicle", InvBaseItem.CreativeCategory.Vehicles },
        { "hide", InvBaseItem.CreativeCategory.Hide },
        { "foods", InvBaseItem.CreativeCategory.Foods },
        { "food", InvBaseItem.CreativeCategory.Foods },
    };

  /// <summary>
  /// Indicates the type of crafting station.
  /// </summary>
  public enum StationType
  {
    /// <summary>
    /// Block "Craft Table", 3 x 3 grid (Row x Column)
    /// </summary>
    CraftTable,

    /// <summary>
    /// Block "Furnace", 2 x 1 grid (Row x Column) , timed recipes.
    /// </summary>
    Furnace,

    /// <summary>
    /// Block "Cauldron", 3 x 1 grid (Row x Column), timed recipes.
    /// </summary>
    Cauldron,

  }
  private static readonly HashSet<string> _craftTerms =
    [
        "craft",
        "crafttable",
        "craftable",
        "craft_table",
        "crafting_table",
    ];


  // INFO:
  // Below lists are used to infer the category of a categorizedRecipes list so we can add
  // appropriate ones into different stations. Add more items to make this more specific, but
  // be aware this makes it more prone to breaking if that recipe ever gets removed.
  // I am doing this because the categories/RecipeManagers dont actually carry any information
  // regarding what station they're for and I need to know that during the prefix.
  //
  // As fragile as this is, it's probably fine long-term.

  // NOTE: The hints are only applied to BASE GAME RECIPES for this framework, not after modified ones have already been added.

  // An axe requires a 3x3 grid. A craft table recipe would also work fine becuase you aren't making one with
  // a furnace or cauldron!
  internal static readonly List<string> CRAFT_TABLE_HINTS = ["Axe"];

  // Smelting iron is basically the hallmark of a furnace, might need to change this if you end up being
  // able to make them via craft table later
  internal static readonly List<string> FURNACE_HINTS = ["Iron Bar"];

  // Cauldron was basically designed specifically for potions, doubt a recipe for this will be added to the
  // furnace or craft table
  internal static readonly List<string> CAULDRON_HINTS = ["Health Potion"];

  /// <summary>
  /// Given a list of CategorizedRecipes, infers the StationType.
  /// Returns null if inference failed.
  /// </summary>
  public static StationType? InferStation(
      List<RecipeManager.CategorizedRecipes> categorizedRecipes
  )
  {
    // Flatten list, since we have a list of "categorized" recipes at the moment
    List<TextAsset> recipeGroup = [.. categorizedRecipes
        .Where(cr => cr.recipes != null)
        .SelectMany(cr => cr.recipes)
        .Where(r => r != null)];

    if (recipeGroup.Count == 0)
    {
      Debug("This station has no recipes. I believe this is a COLORING BLOCK, but I dont need that");
      return null;
    }

    Debug($"Inferring category for recipe group with {recipeGroup.Count} recipes");

    List<string> recipeTitles = [.. recipeGroup
        .Select(recipe =>
        {
            using var reader = new StringReader(recipe.text);
            RecipeXml recipeXml =
                (RecipeXml)new XmlSerializer(typeof(RecipeXml)).Deserialize(reader);

            return recipeXml.name;
        })];

    if (CRAFT_TABLE_HINTS.All(hint =>
        recipeTitles.Any(title => title.Contains(hint))))
    {
      Debug("Looks like a CRAFT TABLE");
      return StationType.CraftTable;
    }

    if (FURNACE_HINTS.All(hint =>
        recipeTitles.Any(title => title.Contains(hint))))
    {
      Debug("Looks like a FURNACE");
      return StationType.Furnace;
    }

    if (CAULDRON_HINTS.All(hint =>
        recipeTitles.Any(title => title.Contains(hint))))
    {
      Debug("Looks like a CAULDRON");
      return StationType.Cauldron;
    }

    Warn(
        "I don't know what this station is! Notify the mod author to update inferences. Mod-specific functionality may be degraded."
    );

    return null;
  }

  /// <summary>
  /// Maps a string to an InvBaseItem.CreativeCategory
  /// Input is case in-sensitive. Valid values are the same as those specified in ExampleStructure/RFREcipes/README.md, Element "category", Attribute "name"
  /// Returns null if input is invalid.
  ///</summary>
  public static InvBaseItem.CreativeCategory? StringToCategoryMapper(string input)
  {
    input = input.ToLower().Trim();
    if (_categoryMap.TryGetValue(input, out var category))
    {
      return category;
    }
    else
    {
      return null;
    }
  }

  /// <summary>
  /// Maps a string to a StationType that can be converted back via StationToStringMapper.
  /// Returns null if a string doesn't map to any existing types..
  ///</summary>
  public static StationType? StringToStationMapper(string input)
  {
    input = input.ToLower().Trim();

    if (_craftTerms.Contains(input))
    {
      return StationType.CraftTable;
    }
    if (input == "furnace")
    {
      return StationType.Furnace;
    }
    if (input == "cauldron")
    {
      return StationType.Cauldron;
    }

    return null;
  }

  /// <summary>
  /// Maps a StationType to a string that can be converted back via StringToStationMapper.
  /// Returns null if an unsupported type is passed.
  ///</summary>
  public static string? StationToStringMapper(StationType type)
  {
    return type switch
    {
      StationType.CraftTable => "craft_table",
      StationType.Furnace => "furnace",
      StationType.Cauldron => "cauldron",
      _ => null,
    };
  }

  /// <summary>
  /// Returns the (rows, columns) of a station.
  /// </summary>
  public static (int rows, int columns) GetStationRowsColumns(StationType type)
  {
    return type switch
    {
      StationType.CraftTable => (3, 3),
      StationType.Furnace => (2, 1),
      StationType.Cauldron => (3, 1),
      _ => throw new ArgumentException($"Got a bad station! Got {type}"),
    };
  }

  /// <summary>
  /// Tries to convert an in-game recipe into an immediate form usable by Recipe Loader.
  /// Returns null if failed.
  /// </summary>
  public static string? TextAssetRecipeToRecipeLoaderRepresentation
    (StationType station, InvBaseItem.CreativeCategory category, TextAsset textAssetRecipe)
  {

    // Helpers 
    //
    static XElement OldItemElementToNew(XElement? oldItem)
    {
      XElement resultItem = new("item");
      if (oldItem == null)
      {
        return resultItem;
      }

      // empty item?
      XAttribute? itemNameAtr = oldItem.Attribute("name");
      if (itemNameAtr == null)
      {
        return resultItem;
      }

      // name
      string itemName = itemNameAtr.Value;
      XAttribute newItemNameAtr = new("name", itemName);
      resultItem.Add(newItemNameAtr);

      // count

      int actualItemDur = InvDatabase.FindByName(itemName)?.durability ?? 1;
      XAttribute oldItemCountAtr = oldItem.Attribute("count");
      XAttribute newItemCountAtr = new("count", 1); // 1 is the default if missing
      if (oldItemCountAtr == null)
      {
        if (actualItemDur == 1)
        {
          resultItem.Add(new XAttribute("exactCount", 1)); // default for non-tools should be exactCount
        }
        else
        {
          resultItem.Add(newItemCountAtr); // If it's something with durability, use "count" to reflect 
          // the actual in-game recipe

        }
        return resultItem;
      }

      if (!int.TryParse(oldItemCountAtr.Value, out int oldCountVal))
      {
        resultItem.Add(newItemCountAtr);
        return resultItem;
      }

      if (actualItemDur <= 0)
      {
        resultItem.Add(newItemCountAtr);
        return resultItem;
      }

      // Try representing in exactCount where possible
      if (oldCountVal % actualItemDur == 0)
      {
        newItemCountAtr = new("exactCount", oldCountVal / actualItemDur);
      }
      else
      {
        newItemCountAtr = new("count", oldCountVal);
      }

      resultItem.Add(newItemCountAtr);

      // Other attributes 
      XAttribute? oldDataAtr = oldItem.Attribute("data");
      if (oldDataAtr != null)
      {
        resultItem.Add(
            new XAttribute(
              "data",
              oldDataAtr.Value
              )
            );
      }

      XAttribute? oldinheritDataAtr = oldItem.Attribute("inheritData");
      if (oldinheritDataAtr != null)
      {
        resultItem.Add(
            new XAttribute(
              "inheritData",
              oldinheritDataAtr.Value
              )
            );
      }

      XAttribute? oldcompareDataAtr = oldItem.Attribute("compareData");
      if (oldcompareDataAtr != null)
      {
        resultItem.Add(
            new XAttribute(
              "compareData",
              oldcompareDataAtr.Value
              )
            );
      }

      return resultItem;
    }

    // Method logic starts here

    string text = textAssetRecipe.text;
    var cleaned = new StringBuilder(text.Length);

    foreach (char c in text)
    {
      if (XmlConvert.IsXmlChar(c))
        cleaned.Append(c);
    }

    text = cleaned.ToString();

    XDocument original;
    try
    {
      original = XDocument.Parse(text);
    }
    catch
    {
      return null;
    }

    XElement? oRoot = original.Root;
    if (oRoot == null)
    {
      return null;
    }

    XElement root = new(
        "recipe",
        oRoot.Attributes()
    );

    // Station 

    XElement stationElem = new("craftstation",
        new XAttribute("type", StationToStringMapper(station)));
    root.Add(stationElem);

    // Category
    XElement categoryElement = new("category",
        new XAttribute("type", category.ToString()));
    root.Add(categoryElement);

    // Locked 

    XElement? oldLocked = oRoot.Element("locked");
    if (oldLocked != null)
    {
      root.Add(new XElement(oldLocked));
    }

    // Rows + Items 

    // Rows
    foreach (XElement curRow in oRoot.Elements("row"))
    {
      XElement newRowElem = new("row");

      // Their items
      foreach (XElement curItem in curRow.Elements("item"))
      {
        newRowElem.Add(OldItemElementToNew(curItem));
      } // end items

      root.Add(newRowElem);

    } // end rows

    // Result
    // NOTE: In RecipeXml, "result" is identical to an item in a row (just named "result") so we can use the same helper
    // for this
    XElement recipeElem = OldItemElementToNew(oRoot.Element("result"));
    recipeElem.Name = "result";

    root.Add(recipeElem);

    // Timed
    XElement? oldTimed = oRoot.Element("timed");
    if (oldTimed != null)
    {
      XAttribute? oldMinutes = oldTimed.Attribute("minutes");

      if (oldMinutes != null &&
          oldMinutes.Value != null &&
          int.TryParse(oldMinutes.Value, out int oldMinutesInt))
      {

        XAttribute newSeconds = new("seconds", oldMinutesInt * 6); // 6 is intentional, as each minute corresponds to 6 seconds in-game
        XElement newTimed = new(oldTimed);
        newTimed.Attribute("minutes")?.Remove();
        newTimed.Add(newSeconds);
        root.Add(newTimed);
      }
    }

    XDocument custom = new(root);

    return custom.ToString();
  }
}
