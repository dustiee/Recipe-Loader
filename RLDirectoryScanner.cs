using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static RecipeLoader.LogTools;
using static RecipeLoader.DirectoryScannerTools; // Defined here below

namespace RecipeLoader;


internal static class DirectoryScannerData
{
  // NOTE: discovered content is ordered Lowest --> Highest priority. When parsing content that may have replacement/deletion directives,
  // this fact can be used to iterate over processed root directories and apply those directives to already iterated roots
  public static IReadOnlyList<ProcessedRootDirectory> DiscoveredContent => RootDirectoryProcessor.ProcessedRootDirectories;
  public static IReadOnlyList<string> RootDirectoryPaths => RootDirectoryCollector.FoundRootDirectories;

  public static void ClearData()
  {
    RootDirectoryProcessor.ClearData();
    RootDirectoryCollector.ClearData();
  }
}


internal static class DirectoryScannerConfig
{
  // the unique name associated with this mod the scanner should look for that contains content
  internal const string RootDirectoryName = "RecipeLoaderContent";

  // the unique names of subdirectories the scanner should look for. They **must** be directly under the root!
  // the name of the valid subdirectory is stored in each SubdirectoryContent object. The author suggests using this 
  // as a way to enforce somewhat sensible structure in root directories by routing files based off the valid subdirectory name,
  // such that users can't throw everything into a single folder.
  // If you don't care and want everything under the root directories to be a valid subdirectory, set this to null.
  internal static readonly HashSet<string>? ValidSubdirectories = ["Delete", "Replace", "Insert"]; // case sensitive

  // the file extension(s) the scanner looks for in subdirectory files
  internal static readonly HashSet<string> Extensions = [".xml"]; // case sensitive

  internal static int DefaultPriority = 10;

  internal static int MaxDepthForRootScan = 5; // inclusive
  internal static int MaxDepthForContentScan = 3; // inclusive
  internal static int MaxDirectoriesIterationLimit = 1000; // most amount of directories per iteration before stopping prematurely

  // implement this if you want to do something to each file string during processing
  internal static string FilePreprocessor(string fileString, string fileExtension, string subdirectoryName)
  {
    return fileString;
  }
}

internal class ProcessedRootDirectory(List<SubdirectoryContent> subdirectories, int? priority)
{
  internal int Priority = priority ?? DirectoryScannerConfig.DefaultPriority;
  internal List<SubdirectoryContent> SubdirectoryContents = subdirectories;
}

// NOTE: Files store the path as a key so you can reference them later during parsing.
// I.e, logging files that have problems or failed to parse, so you can point the user directly to 
// said problematic file
internal class SubdirectoryContent(string subdirectoryName, Dictionary<string, string> files)
{
  internal string SubdirectoryName = subdirectoryName;
  internal Dictionary<string, string> Files = files; // <path, stringContent>
}




internal static class RootDirectoryProcessor
{
  private static IReadOnlyList<ProcessedRootDirectory>? _processedRootDirectories;
  internal static IReadOnlyList<ProcessedRootDirectory> ProcessedRootDirectories => _processedRootDirectories ??= ProcessRootDirectories();
  internal static void ClearData() { _processedRootDirectories = null; }

  internal static IReadOnlyList<ProcessedRootDirectory> ProcessRootDirectories()
  {

    List<ProcessedRootDirectory> processedRootDirectories = [];

    foreach (string rootDirectory in DirectoryScannerData.RootDirectoryPaths)
    {


      // priority file(s)

      int priority = DirectoryScannerConfig.DefaultPriority;
      bool foundPriority = false;

      var priorityFiles = Directory
          .GetFiles(rootDirectory)
          .Where(f => Path.GetFileName(f).StartsWith("PRIORITY", StringComparison.Ordinal));

      foreach (string priorityFile in priorityFiles)
      {
        string suffix = Path.GetFileName(priorityFile)["PRIORITY".Length..];

        if (int.TryParse(suffix, out int parsed))
        {
          if (!foundPriority || parsed > priority)
          {
            priority = parsed;
          }

          foundPriority = true;

          Debug($"{priorityFile} : Got priority file with value {parsed}");
        }
        else
        {
          Warn($"{priorityFile} : Failed parsing priority file suffix");
        }
      }


      // direct subdirectories

      List<SubdirectoryContent> subdirectoryContents = [];
      List<string> subdirectories;

      try
      {
        subdirectories = [.. Directory.GetDirectories(rootDirectory)];
      }
      catch (Exception ex)
      {
        Warn($"{rootDirectory} : Failed getting subdirs. : {ex.Message}");
        continue;
      }

      foreach (string subdirectory in subdirectories)
      {
        string subdirectoryName = Path.GetFileName(subdirectory);

        if (DirectoryScannerConfig.ValidSubdirectories != null)
        {
          if (!DirectoryScannerConfig.ValidSubdirectories.Contains(subdirectoryName))
            continue;
        }


        try
        {
          SubdirectoryContent content = ProcessSubdirectory(subdirectory);
          subdirectoryContents.Add(content);
        }
        catch (Exception ex)
        {
          Error($"{subdirectory} : Failed processing subdirectory. : {ex.Message}");
        }
      }

      ProcessedRootDirectory thisRoot = new(subdirectoryContents, priority);
      processedRootDirectories.Add(thisRoot);
    }

    processedRootDirectories = [.. processedRootDirectories.OrderBy(root => root.Priority)];

    return processedRootDirectories.AsReadOnly();
  }


  internal static SubdirectoryContent ProcessSubdirectory(string subdirectoryPath)
  {
    string subdirectoryName = Path.GetFileName(subdirectoryPath);
    Dictionary<string, string> files = [];

    IReadOnlyList<string> frontier = [subdirectoryPath]; // starter

    int depth = 0;
    int dirFailures = 0;
    int fileFailures = 0;

    do
    {
      depth++;

      (IReadOnlyList<string> dirScanResults, int dirFailureInstance) = GetNextDepthDirectories(frontier);
      FilteredResults fileScanResults = GetFilteredNextDepthFiles(frontier, DirectoryScannerConfig.Extensions);

      foreach (string filePath in fileScanResults.FilteredObjects)
      {
        try
        {

          string fileContent = File.ReadAllText(filePath);
          string fileExtension = Path.GetExtension(filePath);
          fileContent = DirectoryScannerConfig.FilePreprocessor(fileContent, fileExtension, subdirectoryName);

          files[filePath] = fileContent;
        }
        catch (IOException ex)
        {
          Warn($"{filePath} : Failure reading contents : {ex}");
          fileFailures++;
        }
      }


      frontier = dirScanResults; // next iteration
      dirFailures += dirFailureInstance;
      fileFailures += fileScanResults.Failures;
      Debug($"""
          
          Directory Scan:
          Depth: {depth}
          Directories found: {frontier.Count}
          Files found: {fileScanResults.FilteredObjects.Count}
          Failures so far: Directories : {dirFailures} | Files : {fileFailures}
          """);

      if (frontier.Count > DirectoryScannerConfig.MaxDirectoriesIterationLimit)
      {
        Error($"Frontier count exceeded MaxDirectoriesIterationLimit : {frontier.Count} > {DirectoryScannerConfig.MaxDirectoriesIterationLimit} ! Bailing out.");
        break;
      }

    } while (depth < DirectoryScannerConfig.MaxDepthForContentScan
             && frontier.Count > 0);


    return new SubdirectoryContent(subdirectoryName, files);
  }

}

internal static class RootDirectoryCollector
{
  private static IReadOnlyList<string>? _foundRootDirectories;
  internal static IReadOnlyList<string> FoundRootDirectories => _foundRootDirectories ??= CollectRootDirectories();
  internal static void ClearData() { _foundRootDirectories = null; }

  internal static IReadOnlyList<string> CollectRootDirectories()
  {

    // setup
    List<string> foundRootPaths = [];
    string dllLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    IReadOnlyList<string> frontier = [dllLocation]; // starter

    // get root directories

    int depth = 0;
    int failures = 0;

    do
    {
      depth++;

      FilteredResults scanResults = GetFilteredNextDepthDirectories(frontier, DirectoryScannerConfig.RootDirectoryName);

      frontier = scanResults.UnfilteredObjects; // next iteration
      foundRootPaths.AddRange(scanResults.FilteredObjects);
      failures += scanResults.Failures;
      Debug($"""
          
          Depth: {depth}
          FilteredPaths: {scanResults.FilteredObjects.Count}
          UnfilteredPaths: {scanResults.UnfilteredObjects.Count}
          Failures so far: {failures}
          """);

      if (frontier.Count > DirectoryScannerConfig.MaxDirectoriesIterationLimit)
      {
        Error($"Frontier count exceeded MaxDirectoriesIterationLimit : {frontier.Count} > {DirectoryScannerConfig.MaxDirectoriesIterationLimit} ! Bailing out.");
        break;
      }

    } while (depth < DirectoryScannerConfig.MaxDepthForRootScan
             && frontier.Count > 0);

    Debug($"Found {foundRootPaths.Count} roots named {DirectoryScannerConfig.RootDirectoryName}. Failures: {failures} ");

    return foundRootPaths.AsReadOnly();

  }

}

internal static class DirectoryScannerTools
{

  internal readonly struct FilteredResults(IReadOnlyList<string> filtered, IReadOnlyList<string> unfiltered, int failures)
  {
    internal readonly IReadOnlyList<string> FilteredObjects = filtered;
    internal readonly IReadOnlyList<string> UnfilteredObjects = unfiltered;
    internal readonly int Failures = failures;
  }

  internal static FilteredResults GetFilteredNextDepthFiles(IEnumerable<string> directoryPathEnumerable, string extension)
  {
    HashSet<string> input = [extension];
    return GetFilteredNextDepthFiles(directoryPathEnumerable, input);
  }


  internal static FilteredResults GetFilteredNextDepthFiles(IEnumerable<string> directoryPathEnumerable, IEnumerable<string> extensionFilters)
  {
    (IReadOnlyList<string> nextDepthFiles, int failures) =
      GetNextDepthFiles(directoryPathEnumerable);

    List<string> filteredFiles = [];
    List<string> unfilteredFiles = [];

    foreach (string filePath in nextDepthFiles)
    {
      if (extensionFilters.Contains(Path.GetExtension(filePath)))
        filteredFiles.Add(filePath);
      else
        unfilteredFiles.Add(filePath);
    }

    return new FilteredResults(
      filteredFiles.AsReadOnly(),
      unfilteredFiles.AsReadOnly(),
      failures
      );

  }

  internal static FilteredResults GetFilteredNextDepthDirectories(IEnumerable<string> directoryPathEnumerable, string filter)
  {
    HashSet<string> input = [filter];
    return GetFilteredNextDepthDirectories(directoryPathEnumerable, input);
  }

  internal static FilteredResults GetFilteredNextDepthDirectories(IEnumerable<string> directoryPathEnumerable, IEnumerable<string> filters)
  {
    (IReadOnlyList<string> nextDepthDirectories, int failures) =
      GetNextDepthDirectories(directoryPathEnumerable);

    List<string> filteredDirectories = [];
    List<string> unfilteredDirectories = [];
    foreach (string directoryPath in nextDepthDirectories)
    {

      if (filters.Contains(Path.GetFileName(directoryPath)))
      {
        filteredDirectories.Add(directoryPath);
      }
      else
      {
        unfilteredDirectories.Add(directoryPath);
        IEnumerable<string> lenientFilter = filters.Select(s => s.Trim().ToLower());
        if (lenientFilter.Contains(Path.GetFileName(directoryPath).ToLower().Trim()))
        {
          Warn($"{directoryPath} is named suspiciously close to a target filter. Is it named correctly?");
        }
      }



    }

    return new FilteredResults(
      filteredDirectories.AsReadOnly(),
      unfilteredDirectories.AsReadOnly(),
      failures
      );

  }

  internal static (IReadOnlyList<string> filePaths, int failures) GetNextDepthFiles(IEnumerable<string> directoryPathList)
  {
    List<string> filesFound = [];
    int failures = 0;

    foreach (string directoryPath in directoryPathList)
    {
      try
      {
        List<string> innerFiles = [.. Directory.GetFiles(directoryPath)];
        filesFound.AddRange(innerFiles);
      }
      catch (Exception ex)
      {
        Warn($" {directoryPath} : Failed getting subfiles. : {ex.Message}");
        failures++;
        continue;
      }

    }

    return (filesFound.AsReadOnly(), failures);

  }


  internal static (IReadOnlyList<string>, int) GetNextDepthDirectories(IEnumerable<string> directoryPathList)
  {
    List<string> directoriesFound = [];
    int failures = 0;

    foreach (string directoryPath in directoryPathList)
    {
      try
      {
        List<string> innerDirectories = [.. Directory.GetDirectories(directoryPath)];
        directoriesFound.AddRange(innerDirectories);
      }
      catch (Exception ex)
      {
        Warn($" {directoryPath} : Failed getting subdirs. : {ex.Message}");
        failures++;
        continue;
      }

    }

    return (directoriesFound.AsReadOnly(), failures);
  }
}

