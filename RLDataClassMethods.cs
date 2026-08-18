using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using static RecipeLoader.API;
using static RecipeLoader.LogTools;

namespace RecipeLoader;

internal static class RecipeHelper
{
  private static XDocument? _xDocumentTemplate;

  // basic stub with specifier filled in
  internal static XDocument GetBasicStub(RecipeSpecifier specifier)
  {
    if (_xDocumentTemplate == null)
    {
      _xDocumentTemplate = new XDocument(

          new XElement("recipe",
              new XAttribute("name", ""),

              new XElement("craftstation",
                  new XAttribute("type", "")),

              new XElement("category",
                  new XAttribute("type", ""))
          )
      );
    }

    XDocument document = new(_xDocumentTemplate);

    document.Root?.SetAttributeValue("name", specifier.TargetName);
    document.Root?.Element("craftstation")?.SetAttributeValue(
        "type",
        StationToStringMapper(specifier.Station));
    document.Root?.Element("category")?.SetAttributeValue(
        "type",
        specifier.Category.ToString().ToLowerInvariant());

    return document;
  }


  // Returns the document and references to the xml item elements + items in the rows. Responsibility of the caller to fill those in.
  // It's like a worksheet
  internal static (XDocument scaffold, (XElement element, Item item)?[,] itemCells) GetRecipeScaffold(GenericRecipeDirective directive)
  {
    XDocument document = GetBasicStub(directive.Specifier);
    XElement root = document.Root;

    // root attributes

    if (directive.Hidden)
    {
      root.Add(new XAttribute("hidden", "true"));
    }
    if (directive.HiddenUntilUnlocked)
    {
      root.Add(new XAttribute("hiddenUntilUnlocked", "true"));
    }
    if (directive.LockedInCreative)
    {
      root.Add(new XAttribute("lockedInCreative", "true"));
    }
    if (directive.CompareIngredientData)
    {
      root.Add(new XAttribute("compareIngredientData", "true"));
    }

    // locked

    if (directive.Locked != null)
    {
      XElement locked = new("locked");

      foreach (RecipeXml.LockedInfo info in directive.Locked.info)
      {
        if (info is RecipeXml.Quest quest)
        {
          locked.Add(
              new XElement("quest",
                  new XAttribute("npc", quest.npc))
          );
        }
        else if (info is RecipeXml.Achieve achieve)
        {
          locked.Add(
              new XElement("achieve",
                  new XAttribute("title", achieve.title))
          );
        }
      }

      root.Add(locked);
    }

    // result 

    XElement result = directive.Result.ToGameXElement("result", 0);
    root.Add(result);

    // timed

    if (directive.Timed != null)
    {
      XElement timed = new("timed");

      if (directive.Timed.Seconds != null)
      {
        timed.Add(new XAttribute(
    "minutes",
    (directive.Timed.Seconds.Value / 6).ToString(CultureInfo.InvariantCulture))); // Game uses "minutes" in the recipe xmls which are actually 6 seconds each
      }
      else
      {
        timed.Add(new XAttribute(
            "minutes",
            directive.Timed.Minutes.ToString(CultureInfo.InvariantCulture)));
      }

      timed.Add(new XAttribute(
          "cost",
          directive.Timed.Cost.ToString(CultureInfo.InvariantCulture)));

      root.Add(timed);
    }


    // Rows + Items
    (XElement element, Item item)?[,] cells = directive.Specifier.Station switch
    {
      StationType.CraftTable => new (XElement element, Item item)?[3, 3],// 3 x 3 (r x c)
      StationType.Furnace => new (XElement element, Item item)?[2, 1],// 2 x 1 (r x c)
      StationType.Cauldron => new (XElement element, Item item)?[3, 1],// 3 x 1 (r x c)
      _ => throw new InvalidOperationException("Somehow got an invalid crafting station in GetRecipeScaffold"),
    };

    for (int i = 0; i < cells.GetLength(0); i++) // rows
    {
      XElement row = new("row");

      for (int j = 0; j < cells.GetLength(1); j++) // items
      {
        Item? item =
            i >= 0 && i < directive.Rows.Count &&
            j >= 0 && j < directive.Rows[i].Count
                ? directive.Rows[i][j]
                : null;
        if (item == null)
        {
          cells[i, j] = null;
          continue;
        }

        XElement itemElement = new("item");
        row.Add(itemElement);

        cells[i, j] = (itemElement, item);
      } // end items

      if (row.Elements().Any())
      {
        root.Add(row);
      }


    } // end rows


    return (document, cells);
  }

}

internal partial class Item : IEquatable<Item>
{
  // === Equality implementation

  public bool Equals(Item? other)
  {
    if (other is null)
      return false;

    return Name == other.Name &&
           ItemGroupName == other.ItemGroupName &&
           Count == other.Count &&
           ExactCount == other.ExactCount &&
           Data == other.Data &&
           InheritData == other.InheritData &&
           CompareData == other.CompareData;
  }

  public override bool Equals(object? obj)
  {
    return obj is Item other && Equals(other);
  }

  public override int GetHashCode()
  {
    return HashCode.Combine(Name, ItemGroupName,
        Count, ExactCount,
        Data, InheritData, CompareData);
  }

  // ===

  private static Dictionary<string, List<string>> _cachedGroups = [];
  private static Dictionary<string, InvBaseItem> _cachedItems = [];

  private string? GetGroupItem(int groupIndex)
  {
    if (ItemGroupName == null)
    {
      return null;
    }

    if (_cachedGroups.TryGetValue(ItemGroupName, out List<string>? group) == false)
    {
      group = RLItemGroups.GetGroup(ItemGroupName);
      if (group != null && group.Count > 0)
      {
        _cachedGroups[ItemGroupName] = group;
      }
    }
    if (group != null && group.Count > 0)
    {
      if (groupIndex >= 0 && groupIndex < group.Count)
      {
        return group[groupIndex];
      }
    }

    return null;
  }

  private int GetItemCount(string internalName)
  {
    if (ExactCount == null)
    {
      return Count;
    }

    if (_cachedItems.TryGetValue(internalName.Trim(), out InvBaseItem item) == false)
    {
      item = InvDatabase.FindByName(internalName.Trim());
      if (item == null)
      {
        Warn($"Failed to get item for {internalName}");
        return Count;
      }
      _cachedItems[internalName] = item;
    }
    return ExactCount.Value * item.durability;
  }

  // WARNING:
  // Avoid calling ToGameXElement and GetAttributes in a hotpath unless you're caching the results!
  // I changed the equality semantics specifically so you can mindlessly throw Items as keys into a dictionary! <: 

  public XElement ToGameXElement(string elementName = "item", int groupIndex = 0)
  {
    XElement itemNode = new(elementName);

    AttributeData[] attributes = GetAttributes(groupIndex);
    if (attributes.Length == 0)
    {
      return itemNode;
    }

    foreach (AttributeData attribute in attributes)
    {
      itemNode.Add(new XAttribute(attribute.Name, attribute.Value));
    }

    return itemNode;
  }

  public AttributeData[] GetAttributes(int groupIndex)
  {
    string? name = GetGroupItem(groupIndex);
    name ??= Name;

    if (name == null)
    {
      return [];
    }

    int count = GetItemCount(name);

    int size = 3
        + (InheritData ? 1 : 0)
        + (CompareData ? 1 : 0);

    AttributeData[] attributes = new AttributeData[size];

    int index = 0;

    attributes[index++] = new AttributeData("name", name);
    attributes[index++] = new AttributeData("count", count.ToString());
    attributes[index++] = new AttributeData("data", Data.ToString());

    if (InheritData)
    {
      attributes[index++] = new AttributeData("inheritData", "true");
    }

    if (CompareData)
    {
      attributes[index++] = new AttributeData("compareData", "true");
    }

    return attributes;
  }
}




internal partial class GenericRecipeDirective
{

  // This is the color used to distinguish recipes that use groups vs those that dont
  private string ColorizeName(string name)
  {
    // Check if the recipe name maps to an in-game item, if it does, use the item name so we don't display 
    // the internal name otherwise

    InvGameItem? baseItem = InvDatabase.CreateItem(InvDatabase.FindByName(name), 0, 1);

    string actualName = (baseItem == null) ? name : baseItem.itemName;

    if (AllowMixed)
    {
      return $"[000000]{actualName}[-]";
    }
    else
    {
      return $"[505050]{actualName}[-]";
    }
  }

  private bool HasItemsUsingGroups()
  {
    foreach (List<Item> row in Rows)
    {
      foreach (Item item in row)
      {
        if (item == null)
        {
          continue;
        }

        if (item.ItemGroupName != null)
        {
          return true;
        }

      }
    }
    return false;
  }

  private (XDocument scaffold, (XElement element, Item item)?[,] itemCells)? _template; // Copy this before using
  // You can copy the template by using CopyTemplate(_template) defined at the bottom
  private static Dictionary<KeyValuePair<Item, int>, AttributeData[]> _seenItems = []; // Key-groupIndex mapping to computed attribute list
  private static long _totalRecipesAdded = 0;


  private static void CacheEnabledItemAttributeOverwrite(KeyValuePair<Item, int> itemID, XElement itemElement)
  {
    if (_seenItems.TryGetValue(itemID, out AttributeData[] attributes) == false)
    {
      attributes = itemID.Key.GetAttributes(itemID.Value);
      _seenItems[itemID] = attributes;
    }

    // Each "Item" is immutable and so its paired itemElement will share the exact same attributes
    foreach (AttributeData attribute in attributes)
    {
      itemElement.SetAttributeValue(attribute.Name, attribute.Value);
    }

  }

  public string GetCanonicalRecipeXmlString()
  {
    _template ??= RecipeHelper.GetRecipeScaffold(this);

    (XDocument workingCopy, (XElement element, Item item)?[,] itemCells) = CopyTemplate(_template.Value);
    for (int i = 0; i < itemCells.GetLength(0); i++) // Rows 
    {
      for (int j = 0; j < itemCells.GetLength(1); j++) // Columns
      {
        (XElement element, Item item)? cell = itemCells[i, j];
        if (cell == null)
        {
          continue;
        }

        KeyValuePair<Item, int> key = new(cell.Value.item, 0); // 0 because canonical assignment
        CacheEnabledItemAttributeOverwrite(key, cell.Value.element);

      }
    }

    if (HasItemsUsingGroups())
    {
      XAttribute atr = workingCopy.Root.Attribute("name");
      atr.Value = ColorizeName(atr.Value);
    }

    return workingCopy.ToString(SaveOptions.DisableFormatting);


  }

  public List<string>? GetNonCanonicalRecipeXmlStrings()
  {
    _template ??= RecipeHelper.GetRecipeScaffold(this);
    (XDocument workingCopy, (XElement element, Item item)?[,] itemCells) = CopyTemplate(_template.Value);
    // Check what groups we got

    Dictionary<string, List<(XElement element, Item item, List<string> group)>> groupsWithReferences = [];
    for (int i = 0; i < itemCells.GetLength(0); i++)
    {
      for (int j = 0; j < itemCells.GetLength(1); j++)
      {

        // IF it's a group item, we want to know that since we need to modify attributes during expansion
        (XElement element, Item item)? cell = itemCells[i, j];
        if (cell == null)
        {
          continue;
        }
        (XElement element, Item item) = cell.Value;

        if (item.ItemGroupName != null)
        {

          List<string> groupItemList = RLItemGroups.GetGroup(item.ItemGroupName)!; // Item groups must be valid per Validate logic
          if (groupsWithReferences.TryGetValue(item.ItemGroupName, out List<(XElement element, Item item, List<string> group)> groups)
              == false)
          {
            groups = [];
            groups.Add((element, item, groupItemList));
            groupsWithReferences[item.ItemGroupName] = groups;
          }
          else
          {
            groupsWithReferences[item.ItemGroupName].Add((element, item, groupItemList));
          }
        }
        // Also initialize everything
        CacheEnabledItemAttributeOverwrite(new KeyValuePair<Item, int>(item, 0), element);
      } //
    } // End cell iteration


    if (groupsWithReferences.Count <= 0)
    {
      return null; // No group items, so bail
    }

    return AllowMixed
      ? BuildFullExpansions(groupsWithReferences, workingCopy)
      : BuildStaticExpansions(groupsWithReferences, workingCopy);

  }

  private bool TryCumulateHiddens(long hiddens)
  {
    if (hiddens > RecipeLoader.MaxSaneHiddenRecipesPerRecipe)
    {
      Warn($"Recipe '{Specifier.TargetName}': expansion is exceeding {RecipeLoader.MaxSaneHiddenRecipesPerRecipe} hidden recipes. (Count is {hiddens})\n" +
          "Please review this recipe, it's likely abusing <allowmixed />.");
    }
    if (hiddens + _totalRecipesAdded > RecipeLoader.MaxHiddenRecipesTotal)
    {
      Warn($"Recipe '{Specifier.TargetName}': expansion would cause total to exceed " +
          $"{RecipeLoader.MaxHiddenRecipesTotal} hidden recipes. (Total count would have been {hiddens + _totalRecipesAdded})\n " +
          "Skipping expanions. Only the canonical recipe will work.");
      return false;

    }
    else
    {
      _totalRecipesAdded += hiddens;
      return true;
    }
  }

  // I'm not really sure if there's a better way of doing this, but that's the best I could come up with at 2:25 am
  // PERF: 
  // On my hardware, this can make ~20,000 expansions in ~350 ms. For more performance the best idea is probably 
  // to just go ahead and make the Recipe objects ourselves instead of making documents and then having the game 
  // serialize them. Issue with this is that we're betting on the recipe format not changing much in the future 
  // since we'll also have to manually register them and set up locks.ourselves, we are in trouble if that happens
  // The main performance bottleneck seems to be the game itself now though, from testing:
  private List<string> BuildFullExpansions(Dictionary<string, List<(XElement element, Item item, List<string> group)>> groupContainer, XDocument workingCopy)
  {
    // -- Setup 
    List<string> result = [];
    long hiddenCount = 1;
    List<(List<string> group, XElement element, Item item, int value)> slots = [];

    foreach ((string groupName, List<(XElement element, Item item, List<string> group)> members) in groupContainer)
    {
      foreach ((XElement element, Item item, List<string> group) in members)
      {
        slots.Add((group, element, item, 0));
      }
    }

    foreach (var (group, element, item, value) in slots)
    {
      try
      {
        checked
        {
          hiddenCount *= group.Count;
        }
      }
      catch (OverflowException)
      {
        Error("Overflow when calculating hidden count. What kind of recipes are you making? See above log" +
        " for likely offending recipe.");
        return [];
      }
    }
    hiddenCount--; // for canonical
    if (!TryCumulateHiddens(hiddenCount))
    {
      return [];
    }

    // --



    // Start cranking out recipes

    while (hiddenCount > 0)
    {

      // Start at one to avoid adding canonical (all values initialized to 0 before)
      bool overflowFlag = true;
      for (int i = 0; i < slots.Count; i++)
      {
        (List<string> group, XElement element, Item item, int value) slot = slots[i];

        if (overflowFlag)
        {
          if (slot.value + 1 >= slot.group.Count)
          {
            slot.value = 0;
            CacheEnabledItemAttributeOverwrite(new KeyValuePair<Item, int>(slot.item, slot.value), slot.element);
          }
          else
          {
            slot.value++;
            CacheEnabledItemAttributeOverwrite(new KeyValuePair<Item, int>(slot.item, slot.value), slot.element);
            overflowFlag = false;
          }

          slots[i] = slot;
        }
        else
        {
          break; // We just make the changes we need to make and bail for this iteration, no point checking the rest
        }
      }
      result.Add(workingCopy.ToString(SaveOptions.DisableFormatting));
      hiddenCount--;
    }

    return result;

  }

  private List<string> BuildStaticExpansions(Dictionary<string, List<(XElement element, Item item, List<string> group)>> groupContainer, XDocument workingCopy)
  {

    // -- setup
    List<string> result = [];

    List<(List<string> groupItems, List<(XElement, Item)> slots, int value)> groupSlots = [];

    foreach ((string groupName,
             List<(XElement element, Item item, List<string> group)> groupContent) in groupContainer)
    {
      List<(XElement, Item)> curGroupslots = [];
      List<string>? thisGroupElements = null;
      foreach ((XElement element, Item item, List<string> group) in groupContent)
      {
        thisGroupElements ??= group; // All share the same group
        curGroupslots.Add(
            (element, item)
            );
      }

      if (thisGroupElements == null)
      {
        throw new InvalidOperationException("Somehow got a null item group list in BuildStaticExpansions.");
      }


      groupSlots.Add(
          (thisGroupElements, curGroupslots, 0)
          );
    }

    long hiddenCount = 1;
    foreach ((List<string> groupItems, List<(XElement, Item)> slots, int value) in groupSlots)
    {
      try
      {
        checked
        {
          hiddenCount *= groupItems.Count;
        }
      }
      catch (OverflowException)
      {
        Error("Overflow when calculating hidden count. What kind of recipes are you making? See above log" +
        " for likely offending recipe.");
        return [];
      }
    }
    hiddenCount--; // for canonical

    if (!TryCumulateHiddens(hiddenCount))
    {
      return [];
    }

    // -- Recipe time

    while (hiddenCount > 0)
    {

      // Start at one to avoid adding canonical (all values initialized to 0 before)
      bool overflowFlag = true;
      for (int i = 0; i < groupSlots.Count; i++)
      {
        (List<string> groupItems, List<(XElement, Item)> slots, int value) targetGroupSlots = groupSlots[i];

        if (overflowFlag)
        {
          if (targetGroupSlots.value + 1 >= targetGroupSlots.groupItems.Count)
          {
            targetGroupSlots.value = 0;

            foreach ((XElement element, Item item) slot in targetGroupSlots.slots)
            {
              CacheEnabledItemAttributeOverwrite(
                  new KeyValuePair<Item, int>(slot.item, targetGroupSlots.value), slot.element
                  );
            }
          }
          else
          {
            targetGroupSlots.value++;
            foreach ((XElement element, Item item) slot in targetGroupSlots.slots)
            {
              CacheEnabledItemAttributeOverwrite(
                  new KeyValuePair<Item, int>(slot.item, targetGroupSlots.value), slot.element
                  );
            }
            overflowFlag = false;
          }

          groupSlots[i] = targetGroupSlots;
        }
        else
        {
          break; // We just make the changes we need to make and bail for this iteration, no point checking the rest
        }
      }
      result.Add(workingCopy.ToString(SaveOptions.DisableFormatting));
      hiddenCount--;
    }

    return result;


  }

  // Deep copy helper for the template
  private static (XDocument scaffold, (XElement element, Item item)?[,] itemCells) CopyTemplate(
      (XDocument scaffold, (XElement element, Item item)?[,] itemCells) template)
  {
    XDocument newScaffold = new(template.scaffold);

    List<XElement> originalElements = [.. template.scaffold.Descendants()];
    List<XElement> copiedElements = [.. newScaffold.Descendants()];

    Dictionary<XElement, XElement> elementMap = originalElements
        .Zip(copiedElements, (orig, copy) => (orig, copy))
        .ToDictionary(pair => pair.orig, pair => pair.copy);

    int rows = template.itemCells.GetLength(0);
    int cols = template.itemCells.GetLength(1);
    (XElement element, Item item)?[,] newCells = new (XElement element, Item item)?[rows, cols];

    for (int r = 0; r < rows; r++)
    {
      for (int c = 0; c < cols; c++)
      {
        var cell = template.itemCells[r, c];

        if (cell is null)
          continue;

        XElement newElement = elementMap[cell.Value.element];
        newCells[r, c] = (newElement, cell.Value.item);
      }
    }

    return (newScaffold, newCells);
  }

}



