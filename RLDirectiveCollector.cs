using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Xml.Linq;
using static RecipeLoader.LogTools;
using static RecipeLoader.FileParser;
using static RecipeLoader.DirectiveListHelper; // Defined here at the bottom
using System;

// PERF:
// If performance becomes an issue with many recipes, the logic here may be a suspect

// Confict resolution needs to happen here as each directive can explode into a considerable amount of recipes if
// they use groups 
namespace RecipeLoader;

internal static class DirectiveCollector
{

  internal static List<DeleteRecipeDirective> DeleteRecipeDirectives = [];
  internal static List<ReplacementRecipeDirective> ReplacementRecipeDirectives = [];
  internal static List<InsertRecipeDirective> InsertRecipeDirectives = [];

  internal static void PopulateRecipeDirectiveLists()
  {

    Stopwatch stopwatch = new();
    stopwatch.Start();

    List<DeleteRecipeDirective> deleteRecipeDirectivesCollection = [];
    List<ReplacementRecipeDirective> replacementRecipeDirectivesCollection = [];
    List<InsertRecipeDirective> insertRecipeDirectivesCollection = [];

    // Ordered lowest --> highest priority (Sorted by DirectoryScanner)
    foreach (ProcessedRootDirectory rootDirectory in DirectoryScannerData.DiscoveredContent)
    {
      (IReadOnlyList<DeleteRecipeDirective> newDeleteDirectives,
       IReadOnlyList<ReplacementRecipeDirective> newReplaceDirectives,
       IReadOnlyList<InsertRecipeDirective> newInsertDirectives
      ) = CollectDirectives(rootDirectory.SubdirectoryContents);


      // See GetResolvedCombinedCollections for resolving logic

      (deleteRecipeDirectivesCollection, replacementRecipeDirectivesCollection, insertRecipeDirectivesCollection)
        = GetResolvedCombinedCollections(
          deleteRecipeDirectivesCollection, replacementRecipeDirectivesCollection, insertRecipeDirectivesCollection,
          newDeleteDirectives, newReplaceDirectives, newInsertDirectives);

    }

    DeleteRecipeDirectives = deleteRecipeDirectivesCollection;
    ReplacementRecipeDirectives = replacementRecipeDirectivesCollection;
    InsertRecipeDirectives = insertRecipeDirectivesCollection;

    DirectoryScannerData.ClearData();
    EndStopwatchAndDebugPrint(stopwatch, "Populated Delete/Replacement/Insert Recipes in");
    Print(
        $"""

        Got: {DeleteRecipeDirectives.Count} delete directives
        Got: {ReplacementRecipeDirectives.Count} replacement directives
        Got: {InsertRecipeDirectives.Count} insert directives
        """);

    return;
  }

  private static
    (IReadOnlyList<DeleteRecipeDirective>,
     IReadOnlyList<ReplacementRecipeDirective>,
     IReadOnlyList<InsertRecipeDirective>)
    CollectDirectives(IEnumerable<SubdirectoryContent> subdirectoryList)
  {

    List<DeleteRecipeDirective> deleteRecipeDirectivesCollection = [];
    List<ReplacementRecipeDirective> replacementRecipeDirectivesCollection = [];
    List<InsertRecipeDirective> insertRecipeDirectivesCollection = [];



    foreach (SubdirectoryContent subdirectory in subdirectoryList)
    {
      if (subdirectory.Files.Count <= 0)
        continue;

      bool isDeleteDirective = subdirectory.SubdirectoryName == "Delete";
      var genericValidatedFiles =
        new Dictionary<string, string>(
          subdirectory.Files
              .Where(file => FileValidator.ValidateRecipeDirectiveFile(file.Key, file.Value, isDeleteDirective) == true)
      );

      switch (subdirectory.SubdirectoryName)
      {
        case "Delete":
          List<DeleteRecipeDirective> newDeleteRecipeDirectives
            = ParseDeleteFiles(genericValidatedFiles.Values);
          deleteRecipeDirectivesCollection.AddRange(newDeleteRecipeDirectives);
          deleteRecipeDirectivesCollection = RemoveDuplicates(deleteRecipeDirectivesCollection, delete => delete.Specifier);
          break;

        case "Replace":
          List<ReplacementRecipeDirective> newReplacementRecipeDirectives
            = ParseReplacementFiles(genericValidatedFiles);
          replacementRecipeDirectivesCollection.AddRange(newReplacementRecipeDirectives);
          replacementRecipeDirectivesCollection = RemoveDuplicates(replacementRecipeDirectivesCollection, replace => replace.Specifier);
          break;

        case "Insert":
          List<InsertRecipeDirective> newInsertRecipeDirectives
            = ParseInsertFiles(genericValidatedFiles);
          insertRecipeDirectivesCollection.AddRange(newInsertRecipeDirectives);
          // No duplicate removal here because recipes with the same title names (TargetName) are permitted (though not recommended!)
          break;

        default:
          Warn($"Unknown subdirectory name: {subdirectory.SubdirectoryName}");
          break;
      }

    }
    return (deleteRecipeDirectivesCollection,
        replacementRecipeDirectivesCollection,
        insertRecipeDirectivesCollection);

  }

  private static List<InsertRecipeDirective>
    ParseInsertFiles(IEnumerable<KeyValuePair<string, string>> filePairs)
  {
    List<InsertRecipeDirective> insertDirectiveList = [];

    foreach (var (filePath, fileString) in filePairs)
    {
      XDocument insertDocument = XDocument.Parse(fileString);
      InsertRecipeDirective insertDirective =
        CreateNewInsertRecipeDirectiveFrom(filePath, insertDocument);
      insertDirectiveList.Add(insertDirective);
    }
    return insertDirectiveList;
  }


  private static List<ReplacementRecipeDirective>
    ParseReplacementFiles(IEnumerable<KeyValuePair<string, string>> filePairs)
  {
    List<ReplacementRecipeDirective> replacementDirectiveList = [];

    foreach (var (filePath, fileString) in filePairs)
    {
      XDocument replaceDocument = XDocument.Parse(fileString);
      ReplacementRecipeDirective replacementDirective =
        CreateNewReplacementRecipeDirectiveFrom(filePath, replaceDocument);
      replacementDirectiveList.Add(replacementDirective);
    }
    return replacementDirectiveList;
  }

  private static List<DeleteRecipeDirective> ParseDeleteFiles(IEnumerable<string> fileStrings)
  {
    List<DeleteRecipeDirective> deleteDirectiveList = [];

    foreach (string fileString in fileStrings)
    {
      XDocument deleteDocument = XDocument.Parse(fileString);
      RecipeSpecifier specifier = GetRecipeSpecifierFromRecipeXDocument(deleteDocument);
      DeleteRecipeDirective deleteDirective
        = new(specifier);

      deleteDirectiveList.Add(deleteDirective);
    }
    return deleteDirectiveList;
  }

}

internal static class DirectiveListHelper
{

  internal static (
      List<DeleteRecipeDirective> Deletes,
      List<ReplacementRecipeDirective> Replacements,
      List<InsertRecipeDirective> Inserts)
  GetResolvedCombinedCollections(
      List<DeleteRecipeDirective> curD,
      List<ReplacementRecipeDirective> curR,
      List<InsertRecipeDirective> curI,
      IEnumerable<DeleteRecipeDirective> newD,
      IEnumerable<ReplacementRecipeDirective> newR,
      IEnumerable<InsertRecipeDirective> newI)
  {
    List<InsertRecipeDirective> newOverwriteI =
        [.. newI.Where(i => i.Overwrite == true)];

    // Directive usage order is DELETE -> REPLACE -> INSERT in RLPatcher

    // DELETES:
    // - Old overwritten by new replacements (so that they can actually replace the recipe)
    // - New added to Old
    // - Duplicates removed 
    curD = RemoveFilteredElements(curD, newR,
        d => d.Specifier,
        r => r.Specifier);
    curD.AddRange(newD);
    curD = RemoveDuplicates(curD, d => d.Specifier);

    // REPLACEMENTS 
    // - Old removed by new deletes 
    // - Old overwritten by new replacements (Old recipes with DoneReplacement == true also set it for new ones)
    // - Old overwritten by new overwriting insertions 
    // - New added to Old
    // - Duplicates removed 


    curR = RemoveFilteredElements(curR, newD,
        r => r.Specifier,
        d => d.Specifier);

    List<ReplacementRecipeDirective> rDoneReplacing = FilterMatchingElements(newR, curR.Where(r => r.DoneReplacement == true),
         r => r.Specifier,
         r => r.Specifier);

    foreach (ReplacementRecipeDirective r in rDoneReplacing)
    {
      r.DoneReplacement = true;
    }


    curR = RemoveFilteredElements(curR, newR,
        r => r.Specifier,
        r => r.Specifier);

    curR = RemoveFilteredElements(curR, newOverwriteI,
        r => r.Specifier,
        i => i.Specifier);

    // <curR added and deduplicated further below>

    // INSERTS 
    // Old removed by new deletes 
    // Old removed by new overwriting insertions 
    // Old removed by new replacements (set DoneReplacement -> true for those)
    // New added to Old 
    // Duplicates *NOT* removed, they're OK for this.

    curI = RemoveFilteredElements(curI, newD,
        i => i.Specifier,
        d => d.Specifier);

    List<ReplacementRecipeDirective> rReplacing = FilterMatchingElements(newR, curI,
        r => r.Specifier,
        i => i.Specifier);

    // Modifying references so these are reflected in curR
    foreach (ReplacementRecipeDirective r in rReplacing)
    {
      r.DoneReplacement = true;
    }

    curI = RemoveFilteredElements(curI, newR,
        i => i.Specifier,
        r => r.Specifier);


    curI = RemoveFilteredElements(curI, newOverwriteI,
        i => i.Specifier,
        i => i.Specifier);

    curI.AddRange(newI);

    curR.AddRange(newR);
    curR = RemoveDuplicates(curR, r => r.Specifier);


    return (curD, curR, curI);
  }



  internal static List<T> RemoveDuplicates<T, TKey>(
      IEnumerable<T> list,
      Func<T, TKey> keySelector)
  {
    return [.. list
        .GroupBy(keySelector)
        .Select(group => group.First())];
  }

  public static List<TSource> RemoveFilteredElements<TSource, TDelete, TKey>(
      IEnumerable<TSource> source,
      IEnumerable<TDelete> elementsToDelete,
      Func<TSource, TKey> sourceKeySelector,
      Func<TDelete, TKey> deleteKeySelector)
  {
    var deleteKeys = elementsToDelete
        .Select(deleteKeySelector)
        .ToHashSet();

    return [.. source.Where(x => !deleteKeys.Contains(sourceKeySelector(x)))];
  }


  public static List<TSource> FilterMatchingElements<TSource, TFilter, TKey>(
      IEnumerable<TSource> source,
      IEnumerable<TFilter> filterElements,
      Func<TSource, TKey> sourceKeySelector,
      Func<TFilter, TKey> filterKeySelector)
  {
    var filterKeys = filterElements
        .Select(filterKeySelector)
        .ToHashSet();

    return [.. source.Where(x => filterKeys.Contains(sourceKeySelector(x)))];
  }

}
