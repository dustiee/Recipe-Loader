using System;
using System.Collections.Generic;
using static RecipeLoader.API;


namespace RecipeLoader;

internal readonly struct AttributeData(string name, string value)
{
  internal readonly string Name = name;
  internal readonly string Value = value;
}

internal partial class Item(
  string? name = null,
  int count = 1,
  int? exactCount = null,
  string? itemGroupName = null,
  int data = 0,
  bool inheritData = false,
  bool compareData = false) : IEquatable<Item>
{
  public readonly string? Name = name;
  public readonly int Count = count;

  public readonly int? ExactCount = exactCount;
  public readonly string? ItemGroupName = itemGroupName; // Validated in FileValidator if not null
  public readonly int Data = data;
  public readonly bool InheritData = inheritData;
  public readonly bool CompareData = compareData;

  // Methods in RLDataClassMethods.cs
}

internal partial class GenericRecipeDirective(GenericRecipeDirectiveData data)
{
  public GenericRecipeDirectiveData Data = data;

  public RecipeSpecifier Specifier => Data.Specifier;

  public bool Hidden => Data.Hidden;
  public bool HiddenUntilUnlocked => Data.HiddenUntilUnlocked;
  public bool LockedInCreative => Data.LockedInCreative;
  public bool CompareIngredientData => Data.CompareIngredientData;
  public RecipeXml.Locked? Locked => Data.Locked;

  public bool AllowMixed => Data.AllowMixed;

  public List<List<Item>> Rows => Data.Rows; // outer = rows, inner = items per row. Guaranteed to contain all (rows, columns) of the station.
  public Item Result => Data.Result;
  public Timed? Timed => Data.Timed;

  // Methods in RLDataClassMethods.cs
}

internal class InsertRecipeDirective(
    GenericRecipeDirectiveData data,
    bool? overwrite = false
) : GenericRecipeDirective(data)
{
  public bool Overwrite = overwrite ?? false;
}

internal class ReplacementRecipeDirective(
    GenericRecipeDirectiveData data,
    bool? doneReplacement = false
) : GenericRecipeDirective(data)
{
  public bool DoneReplacement = doneReplacement ?? false;
}

internal class DeleteRecipeDirective(RecipeSpecifier specifier)
{
  public readonly RecipeSpecifier Specifier = specifier;
}






// Boring section

internal class GenericRecipeDirectiveData(
RecipeSpecifier specifier,
    bool hidden,
    bool hiddenUntilUnlocked,
    bool lockedInCreative,
    bool compareIngredientData,
    RecipeXml.Locked? locked,
    bool allowMixed,
    List<List<Item>> rows,
    Item result,
    Timed? timed
)
{
  public readonly RecipeSpecifier Specifier = specifier;

  public bool Hidden = hidden;
  public bool HiddenUntilUnlocked = hiddenUntilUnlocked;
  public bool LockedInCreative = lockedInCreative;
  public bool CompareIngredientData = compareIngredientData;
  public RecipeXml.Locked? Locked = locked;

  public bool AllowMixed = allowMixed;

  public List<List<Item>> Rows = rows; // outer = rows, inner = items per row
  public Item Result = result;
  public Timed? Timed = timed;
}

internal class StationCategoryPair(
    StationType station,
    InvBaseItem.CreativeCategory category)
{
  internal readonly StationType Station = station;
  internal readonly InvBaseItem.CreativeCategory Category = category;

  public bool Equals(StationCategoryPair? other)
  {
    if (other is null)
      return false;

    return Station == other.Station &&
           Category == other.Category;
  }

  public override bool Equals(object? obj)
  {
    return Equals(obj as StationCategoryPair);
  }

  public override int GetHashCode()
  {
    return HashCode.Combine(Station, Category);
  }

  public static bool operator ==(
      StationCategoryPair? left,
      StationCategoryPair? right)
  {
    if (left is null)
      return right is null;

    return left.Equals(right);
  }

  public static bool operator !=(
      StationCategoryPair? left,
      StationCategoryPair? right)
  {
    return !(left == right);
  }
}

internal class RecipeSpecifier(
    string name,
    StationCategoryPair stationCategory)
    : IEquatable<RecipeSpecifier>
{
  internal readonly string TargetName = name;
  internal readonly StationCategoryPair StationCategory = stationCategory;
  internal StationType Station => StationCategory.Station;
  internal InvBaseItem.CreativeCategory Category => StationCategory.Category;

  public bool Equals(RecipeSpecifier? other)
  {
    if (other is null)
      return false;
    return TargetName == other.TargetName &&
           Station == other.Station &&
           Category == other.Category;
  }
  public override bool Equals(object? obj)
  {
    return Equals(obj as RecipeSpecifier);
  }
  public override int GetHashCode()
  {
    return HashCode.Combine(TargetName, Station, Category);
  }
  public static bool operator ==(RecipeSpecifier? left, RecipeSpecifier? right)
  {
    if (left is null)
      return right is null;
    return left.Equals(right);
  }
  public static bool operator !=(RecipeSpecifier? left, RecipeSpecifier? right)
  {
    return !(left == right);
  }
}

internal class Timed
{
  public double Minutes; // using double here because that's the in-game type
  public float Cost;

  public double? Seconds; // Overwrites minutes when set
}
