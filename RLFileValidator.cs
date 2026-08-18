using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using static RecipeLoader.LogTools;
using static RecipeLoader.API;
using BepInEx;

namespace RecipeLoader;

// This file should only answer the question "given a recipe directive file, is it valid?"
// It's responsible for ensuring all the assumptions about the file in FileParser are valid

internal static class FileValidator
{

  private const int _maxReasonableRecipeDepth = 2; // starting from root

  private static readonly Dictionary<string, string[]> _validElementsAndAttributes = new()
  {
    // Key: Element | Value: Valid Attributes
    ["recipe"] = ["name", "hidden", "lockedInCreative", "hiddenUntilUnlocked", "compareIngredientData", "mana"],
    ["craftstation"] = ["type"],
    ["category"] = ["type"],
    ["ignorethis"] = [],
    ["overwrite"] = [],
    ["allowMixed"] = [],
    ["locked"] = [],
    ["quest"] = ["npc"],
    ["achieve"] = ["title"],
    ["row"] = [],
    ["item"] = ["name", "group", "count", "exactCount", "data", "inheritData", "compareData"],
    ["result"] = ["name", "count", "exactCount", "data"],
    ["timed"] = ["minutes", "seconds", "cost"],
  };

  // Pure helpers

  // We just ignore unknowns but warn them so they're easier to debug
  private static void CheckForUnknownElementsAndAttributes(XElement seed, List<string> warnings)
  {

    List<XElement> frontier = [seed];
    int iteration = 0;

    while (iteration <= _maxReasonableRecipeDepth && frontier.Count > 0)
    {
      List<XElement> knownElements = [];

      // frontier elements
      foreach (XElement element in frontier)
      {
        string elementName = element.Name.ToString();
        if (!_validElementsAndAttributes.TryGetValue(elementName, out string[] validAttributes))
        {
          warnings.Add($"Unknown element named '{elementName}'");
          continue;
        }

        knownElements.Add(element);

        foreach (XAttribute attribute in element.Attributes())
        {
          string attributeName = attribute.Name.ToString();
          if (!validAttributes.Contains(attributeName))
          {
            warnings.Add($"'{elementName}' contains unknown attribute '{attributeName}' ");
          }
        }
      }

      // New frontier
      List<XElement> newFrontier = [];
      foreach (XElement element in knownElements)
      {
        newFrontier.AddRange(element.Elements());
      }
      frontier = newFrontier;

      iteration++;

    } // End while

    if (frontier.Count > 0)
    {
      warnings.Add("Recipe has elements beyond depth 2 starting from root. They will be ignored.");
    }

    return;

  }

  private static bool DoesItemExistIngame(string exact)
  {
    return InvDatabase.FindByName(exact) != null;
  }

  private static bool IsAnEmptyItem(XElement item)
  {
    return item.Attribute("group") == null && item.Attribute("name") == null;
  }

  private static bool AttributeHasValidNumericValue(XAttribute? attribute)
  {
    if (attribute == null)
    {
      return false;
    }

    if (!float.TryParse(attribute.Value,
              NumberStyles.Float,
              CultureInfo.InvariantCulture,
          out float outputNumber))
    {
      return false;
    }

    if (!float.IsNormal(outputNumber))
    {
      return false;
    }
    if (outputNumber < 0)
    {
      return false;
    }

    return true;
  }

  // Impure helpers

  private static bool TryGetValidGroupOrName(XElement item, List<string> errors, out string? name)
  {
    XAttribute? itemGroupAtr = item.Attribute("group");
    XAttribute? itemNameAtr = item.Attribute("name");

    // Group overwrites "name" if available, otherwise use name
    if (itemGroupAtr != null)
    {
      itemGroupAtr.Value = itemGroupAtr.Value.Trim();
      List<string>? group = RLItemGroups.GetGroup(itemGroupAtr.Value);
      if (group == null)
      {
        errors.Add($"Invalid group! Got {itemGroupAtr.Value}, which does not map to a valid group.");
      }
      else
      {
        name = itemGroupAtr.Value;
        return true;
      }
    }

    if (itemNameAtr != null)
    {
      string itemName = itemNameAtr.Value;
      if (!DoesItemExistIngame(itemName))
      {
        errors.Add($"Recipe has a non-existent item: {itemName}");
      }
      else
      {
        name = itemName;
        return true;
      }

    }
    name = null;
    return false;
  }


  // Validators

  private static void ValidateRoot(XElement root, List<string> errors)
  {
    if (root.Name != "recipe")
    {
      errors.Add($"Invalid root name. Expected 'recipe', got {root.Name}");
    }


    string? rootNameAttribute = root.Attribute("name")?.Value;
    if (string.IsNullOrWhiteSpace(rootNameAttribute))
    {
      errors.Add("Root is either missing the 'name' attribute, or its empty");
    }

    return;

  }

  private static void ValidateCategory(XElement root, List<string> errors, List<string> warnings)
  {
    XElement? categoryElement = root.Element("category");

    if (categoryElement == null)
    {
      errors.Add("Element 'category' doesn't exist");
      return;
    }
    if (root.Elements("category").Count() > 1)
    {
      warnings.Add("Recipe has more than one 'category' element. Only the first one will be used.");
    }

    XAttribute? categoryType = categoryElement.Attribute("type");
    if (categoryType == null)
    {
      errors.Add("Element 'category' does not have a type attribute");
      return;
    }


    InvBaseItem.CreativeCategory? category = StringToCategoryMapper(categoryType.Value);
    if (category == null)
    {
      errors.Add($"Element 'category' has a 'type' attribute that does not map to a valid category.");
    }

    return;

  }

  private static StationType? ValidateAndGetStation(XElement root, List<string> errors)
  {
    XElement? craftstationElement = root.Element("craftstation");
    StationType? craftstation = null;

    if (craftstationElement == null)
    {
      errors.Add("Element 'craftstation' doesn't exist");
    }
    else
    {
      XAttribute? craftstationType = craftstationElement.Attribute("type");
      if (string.IsNullOrWhiteSpace(craftstationType?.Value))
      {
        errors.Add("Element 'craftstation' does not have a 'type' attribute");
      }
      else
      {
        craftstation = StringToStationMapper(craftstationType.Value.Trim());

        if (craftstation == null)
        {
          errors.Add(
            $"Element 'craftstation' has an invalid 'type' attribute (got {craftstationType.Value.Trim()})");
        }
      }
    }
    return craftstation;
  }

  private static void ValidateRowCount(List<XElement> rows, StationType? station, List<string> errors)
  {
    if (rows.Count <= 0)
    {
      errors.Add($"Recipe has no rows.");
    }

    if (station == null)
    {
      return;
    }


    if (
        rows.Count > 3
        && (station == StationType.CraftTable || station == StationType.Cauldron)
       )
    {
      errors.Add($"Recipe has more than three rows. (got {rows.Count}, {station} Limit: 3)");
    }

    if (rows.Count > 2 && station == StationType.Furnace)
    {
      errors.Add($"Recipe has more than two rows. (got {rows.Count}, {station} Limit: 2)");
    }
    return;
  }

  private static void ValidateItemsInRows(List<XElement> rows, StationType? station, List<string> errors, List<string> warnings)
  {
    if (station == null)
    {
      return;
    }

    int maxItems = station switch
    {
      StationType.CraftTable => 3,
      StationType.Furnace => 1,
      StationType.Cauldron => 1,
      _ => throw new InvalidOperationException($"The file validator doesn't handle station: {station}"),
    };

    bool nonemptyItemExists = false;
    (int row, int column) recipeMinBounds = (int.MaxValue, int.MaxValue);
    (int row, int column) recipeMaxBounds = (int.MinValue, int.MinValue);

    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
    {
      XElement row = rows[rowIndex];
      IEnumerable<XElement> items = row.Elements("item");
      List<XElement> itemList = [.. items];

      if (itemList.Count > maxItems)
      {
        errors.Add($"Row has too many items. (got {itemList.Count}. {station} Limit: {maxItems}");
      }

      for (int itemIndex = 0; itemIndex < itemList.Count; itemIndex++)
      {
        XElement item = itemList[itemIndex];

        if (IsAnEmptyItem(item))
        {
          continue;
        }

        if (TryGetValidGroupOrName(item, errors, out string? name))
        {
          nonemptyItemExists = true;

          if (recipeMinBounds.row > rowIndex) recipeMinBounds.row = rowIndex;
          if (recipeMinBounds.column > itemIndex) recipeMinBounds.column = itemIndex;
          if (recipeMaxBounds.row < rowIndex) recipeMaxBounds.row = rowIndex;
          if (recipeMaxBounds.column < itemIndex) recipeMaxBounds.column = itemIndex;
        }
        else
        {
          continue;
        }

        XAttribute? countAttribute = item.Attribute("count");
        XAttribute? exactCountAttribute = item.Attribute("exactCount");
        if (countAttribute != null && exactCountAttribute != null)
        {
          warnings.Add($"An item element with a name/group {name} has both 'count', 'exactCount'. exactCount will take priority.");
        }

        if (countAttribute == null && exactCountAttribute == null)
        {
          if (RecipeLoader.ItemCountWarn == true)
          {
            warnings.Add($"An item element with a name/group '{name}' is missing one of 'count', 'exactCount'. Fallback of 1 count will be used");
          }
        }
        else if (
            !(AttributeHasValidNumericValue(countAttribute) || AttributeHasValidNumericValue(exactCountAttribute))
           )
        {
          errors.Add($"An item element with a name/group '{name}' has no parsable 'count' or 'exactCount' attribute.");
        }

      } // end items
    } // end rows

    if (!nonemptyItemExists)
    {
      errors.Add("Recipe does not have at least 1 non-empty item");
      return;
    }

    // The bounding box tells us exactly how many columns every row inside it must provide.
    // A row inside the box with fewer items means items are missing; more means stray extras.
    // A row outside the box must have zero items (all absent), otherwise it has strays.
    int expectedColumnCount = recipeMaxBounds.column - recipeMinBounds.column + 1;

    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
    {
      XElement row = rows[rowIndex];
      List<XElement> itemList = [.. row.Elements("item")];

      bool rowIsInsideBounds = rowIndex >= recipeMinBounds.row && rowIndex <= recipeMaxBounds.row;

      if (rowIsInsideBounds)
      {
        // Count only the slice of items that corresponds to the bounding box columns.
        // Items before minColumn or after maxColumn are stray extras.
        int itemsBeforeBox = 0;
        int itemsInsideBox = 0;
        int itemsAfterBox = 0;

        for (int itemIndex = 0; itemIndex < itemList.Count; itemIndex++)
        {
          if (itemIndex < recipeMinBounds.column) itemsBeforeBox++;
          else if (itemIndex <= recipeMaxBounds.column) itemsInsideBox++;
          else itemsAfterBox++;
        }

        if (itemsBeforeBox > 0 || itemsAfterBox > 0)
        {
          errors.Add($"Row {rowIndex} has items outside the column bounding box of non-empty items.\n" +
              "Please refer to GUIDE-TO-RECIPES.md, Section Quirks for more information on this problem.");
        }

        if (itemsInsideBox != expectedColumnCount)
        {
          errors.Add($"Row {rowIndex} has {itemsInsideBox} item(s) inside the bounding box but {expectedColumnCount} were expected.\n" +
              "Please refer to GUIDE-TO-RECIPES.md, Section Quirks for more information on this problem.");
        }
      }
      else if (itemList.Count != 0)
      {

        errors.Add($"Row {rowIndex} is outside the row bounding box of non-empty items but contains an item element.\n" +
            "Please refer to GUIDE-TO-RECIPES.md, Section Quirks for more information on this problem.");
        break;
      }
    }
  }

  private static void ValidateResult(XElement root, List<string> errors, List<string> warnings)
  {
    XElement? resultElement = root.Element("result");

    if (resultElement == null)
    {
      errors.Add("Missing 'result' element");
    }
    else
    {
      // Only "name" is valid for results, a result using "group" does not make sense
      bool hasGroup = resultElement.Attribute("group") != null;
      if (hasGroup)
      {
        errors.Add("'result' has a group attribute. It doesn't accept one. ");
      }


      string? resultName = resultElement.Attribute("name")?.Value;
      if (string.IsNullOrWhiteSpace(resultName))
      {
        errors.Add("'result' has no name.");
      }
      else if (!DoesItemExistIngame(resultName))
      {
        errors.Add($"Recipe has a non-existent item in 'result': {resultName}");
      }
      XAttribute? countAttribute = resultElement.Attribute("count");
      XAttribute? exactCountAttribute = resultElement.Attribute("exactCount");


      if (countAttribute != null && exactCountAttribute != null)
      {
        warnings.Add($"The result has both 'count', 'exactCount'. exactCount will take priority.");
      }

      if (countAttribute == null && exactCountAttribute == null && RecipeLoader.ItemCountWarn == true)
      {
        warnings.Add($"The result is missing one of 'count', 'exactCount'. Fallback of 1 count will be used");
      }

      if (
          !(AttributeHasValidNumericValue(countAttribute) || AttributeHasValidNumericValue(exactCountAttribute))
         )
      {
        errors.Add($"The result is has no parsable 'count' or 'exactCount' attribute.");
      }

    }

  }

  internal static void ValidateTimed(XElement root, List<string> errors, List<string> warnings)
  {
    XElement? timedElement = root.Element("timed");

    if (timedElement == null)
    {
      errors.Add("Missing 'timed' element");
      return;
    }

    // minutes / seconds
    XAttribute? timedMinutes = timedElement.Attribute("minutes");
    XAttribute? timedSeconds = timedElement.Attribute("seconds");

    if (timedMinutes == null && timedSeconds == null)
    {
      errors.Add("'timed' element is missing both: 'minutes', 'seconds'");
    }
    else
    {

      if (
          !(AttributeHasValidNumericValue(timedMinutes) || AttributeHasValidNumericValue(timedSeconds))
          )
      {
        errors.Add("'timed' element does not have at least one parsable 'minutes' or 'seconds' attribute");
      }

      if (timedSeconds != null && timedMinutes != null)
      {
        warnings.Add($"'timed' element has both 'minutes' and 'seconds'. 'seconds' takes priority over 'minutes'.");
      }
    }

    // cost

    XAttribute? timedCost = timedElement.Attribute("cost");
    if (timedCost == null)
    {
      warnings.Add($"'timed' element has no 'cost'. This recipe will be free to skip because the default is 0.");
    }
    else
    {
      if (!AttributeHasValidNumericValue(timedCost))
      {
        errors.Add($"'timed' element has an unparsable 'cost' attribute.");
      }

    }

  }

  private static void ValidateLocked(XElement root, List<string> errors)
  {
    XElement? lockedElement = root.Element("locked");

    if (lockedElement == null)
    {
      return;
    }

    if ((lockedElement.Elements("quest")?.Count() ?? 0) > 1)
    {
      errors.Add("'locked' element has more than 1 'quest' element");
    }

    if ((lockedElement.Elements("achieve")?.Count() ?? 0) > 1)
    {
      errors.Add("'locked' element has more than 1 'achieve' element");
    }

    XElement? questElement = lockedElement.Element("quest");
    XElement? achieveElement = lockedElement.Element("achieve");

    if (questElement != null && achieveElement != null)
    {
      errors.Add("'locked' element has both 'quest' and 'achieve' elements");
    }


    if (questElement != null)
    {
      XAttribute? questNpc = questElement.Attribute("npc");

      if (questNpc == null)
      {
        errors.Add("'quest' element is missing an 'npc' attribute");
      }
      else if (questNpc.Value.IsNullOrWhiteSpace())
      {
        errors.Add($"'quest' element has an invalid 'npc' attribute (got {questNpc.Value}");
      }
    }

    if (achieveElement != null)
    {
      XAttribute? achieveTitle = achieveElement.Attribute("title");

      if (achieveTitle == null)
      {
        errors.Add("'achieve' element is missing a 'title' attribute");
      }
      else if (achieveTitle.Value.IsNullOrWhiteSpace())
      {
        errors.Add($"'achieve' element has an invalid 'title' attribute (got {achieveTitle.Value}");
      }
    }
  }

  internal static bool ValidateRecipeDirectiveFile(string filePath, string fileContent, bool isDeleteDirective)
  {

    // Helpers
    List<string> errors = [];
    List<string> warnings = [];

    bool ReturnValidationResult()
    {
      if (warnings.Count > 0)
      {
        Warn($"{filePath} : Recipe has the following warnings:\n" +
            string.Join("\n", warnings));
      }
      if (errors.Count > 0)
      {
        Warn($"{filePath} : Recipe could not be processed due to the following problems:\n"
          + string.Join("\n", errors));
        return false;
      }

      return true;
    }
    // --

    // Setup

    XDocument? fileDocument;
    try { fileDocument = XDocument.Parse(fileContent); }
    catch (Exception ex)
    {
      Warn($"{filePath} : Failed parsing file with exception : {ex.Message}");
      return false;
    }

    XElement? root = fileDocument.Root;

    // --

    // Validation begins here

    // Root
    if (root == null)
    {
      errors.Add("Root doesn't exist.");
      return false;
    }

    ValidateRoot(root, errors);

    if (root.Element("ignorethis") != null)
    {
      Print($"{filePath} : File wants to be ignored.");
      return false;
    }

    // Check for unknown elements/attributes 
    CheckForUnknownElementsAndAttributes(root, warnings);

    // Category/Station

    ValidateCategory(root, errors, warnings);
    StationType? station = ValidateAndGetStation(root, errors);

    // Skipping rest of validation for delete directives because we only need name, category and craftstation
    if (isDeleteDirective) { return ReturnValidationResult(); }


    // Rows and items

    List<XElement> rows = [.. root.Elements("row")];
    ValidateRowCount(rows, station, errors);
    ValidateItemsInRows(rows, station, errors, warnings);


    // Results

    ValidateResult(root, errors, warnings);

    // Furnace/Cauldron Timed checks

    if (station == StationType.Cauldron || station == StationType.Furnace)
    {
      ValidateTimed(root, errors, warnings);

    }


    // Locked 

    ValidateLocked(root, errors);

    // End

    return ReturnValidationResult();

  }
}
