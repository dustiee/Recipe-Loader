using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.IO;
namespace RecipeLoader;


internal static class LogTools
{

  // Timing


  internal static void EndStopwatchAndDebugPrint(
      Stopwatch stopwatch,
      string timePrefixMessage)
  {
    stopwatch.Stop();

    double milliseconds = stopwatch.ElapsedTicks * (1_000.0 / Stopwatch.Frequency);

    if (milliseconds < 10.0)
    {
      double microseconds = milliseconds * 1_000.0;
      Debug($"{timePrefixMessage} : {microseconds:F0} µs");
    }
    else if (milliseconds < 500.0)
    {
      Debug($"{timePrefixMessage} : {milliseconds:F3} ms");
    }
    else
    {
      double seconds = milliseconds / 1_000.0;
      Debug($"{timePrefixMessage} : {seconds:F3} s");
    }
  }

  // Logging 

  private static string FormatMessage(
      object? obj,
      string caller,
      string callerPath)
      => $"""
            {obj} [ FROM: {caller} @ {Path.GetFileName(callerPath)}]
            """;

  internal static void Print(
      object? obj,
      [CallerMemberName] string caller = "",
      [CallerFilePath] string callerPath = "")
  {
    RecipeLoader.Log!.LogInfo(FormatMessage(obj, caller, callerPath));
  }

  internal static void Debug(
      object? obj,
      [CallerMemberName] string caller = "",
      [CallerFilePath] string callerPath = "")
  {
    if (!RecipeLoader.MuteDebug)
    {
      RecipeLoader.Log!.LogDebug(FormatMessage(obj, caller, callerPath));
    }
  }

  internal static void Warn(
      object? obj,
      [CallerMemberName] string caller = "",
      [CallerFilePath] string callerPath = "")
  {
    RecipeLoader.Log!.LogWarning(FormatMessage(obj, caller, callerPath));
  }

  internal static void Error(
      object? obj,
      [CallerMemberName] string caller = "",
      [CallerFilePath] string callerPath = "")
  {
    RecipeLoader.Log!.LogError(FormatMessage(obj, caller, callerPath));
  }

  internal static void Fatal(
      object? obj,
      [CallerMemberName] string caller = "",
      [CallerFilePath] string callerPath = "")
  {
    RecipeLoader.Log!.LogFatal(FormatMessage(obj, caller, callerPath));
  }

}
