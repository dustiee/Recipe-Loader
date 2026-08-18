using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;

using static RecipeLoader.LogTools;

namespace RecipeLoader;

internal static class RLItemGroups
{
  private static bool _ranTests = false;

  // IMPORTANT!
  // Update GROUP-LIST.md when changing groups or adding new ones 

  // Add more groups here as needed.
  // Convention: first element is the CANONICAL item. Basically should be the first thing that comes to mind when 
  // first seeing the group name.

  // WARNING:
  // If a group ever exceeds 138 items, our expansion limit checks in the expansion methods for GenericRecipeDirective 
  // can overflow because we're using a long 
  //
  // NOTE:
  // Groups MUST use internal item names, which might not always be the displayed ones!
  private static readonly Dictionary<string, List<string>> _groups = new(StringComparer.OrdinalIgnoreCase)
    {

      // === Blocks 

      {
        "Plank",
        new List<string> { "Plank", "Birch Plank", "Clear Plank", "Palm Plank", "Swamp Plank" }
      },

      {
        "Bark", // "Wood" in-game. NOT PLANKS. This is from trees.
        new List<string> { "Bark", "Birch Bark", "Palm Bark", "Space Bark", "Dead Bark", "Lost Bark" }
      },

      {
        "Leaves",
        new List<string> {
            "Leaves", "Birch Leaves", "Oak Leaves", "Pine Leaves", "Jungle Leaves", "Palm Leaves", "Poplar Leaves",
            "Baobab Leaves", "Space Leafs", "Lost Leaves", "Autumn Leaves", "Sakura Leaves", "Red Leaves",
          }
      },

      {
        "Stone", // I don't know whether "Sandstone" should go here. Probably not?
        new List<string> { "Stone", "Clear Stone", "Forgotten Stone", "Forgotten Stone Old", "Swamp Stone",
            "Asteroid Stone",
          }
      },

      {
        "Sand",
        new List<string> { "Sand", "Sand Stone" }
      },

      {
        "Flower",
        new List<string> {
            "Yellow Flower", "Blue Flower", "Red Flower", "Rose Flower", "Tulip Flower", "Peony Flower", "Primula Flower",
            "Lilia Flower", "Mythic Flower", "Space Flower", "Jungle Flowers" }
      },

      {
        "Ladder",
        new List<string> { "Ladder", "Metal Ladder" }
      },

      {
        "Window",
        new List<string> { "Window", "Wood Window", "Antique Window" }
      },

      {
        "Music Box",
        new List<string> { "Accordion Music Box", "Drum Music Box", "Guitar Music Box", "Piano Music Box" }
      },

      {
        "Coral",
        new List<string> { "Green Coral", "Yellow Coral", "Frozen Coral" }
      },

      {
        "Seaweed",
        new List<string> { "Kelp", "Kombu", "Enhalus", "Sea Grass" }
      },

      {
        "Mushroom",
        new List<string> { "Mushroom", "Toadstool", "Morel", "Ink Cap" }
      },


      // Double Blocks

      {
        "Door",
        new List<string> { "Wood Door", "Simple Wood Door", "Metal Door", "Metal Lattice Door"  }
      },

      {
        "Wood Door",
        new List<string> { "Wood Door", "Simple Wood Door" }
      },

      {
        "Metal Door",
        new List<string> { "Metal Door", "Metal Lattice Door" }
      },
      // === end Blocks
      
      // === Tools 

      {
        "Pickaxe",
        new List<string> { "Dig", "Iron Dig", "Goblin PickAxe", "Multitool" }
      },

      {
        "Axe",
        new List<string> { "Axe", "Iron Axe", "Multitool" }
      },

      {
        "Shovel",
        new List<string> { "Shovel", "Iron Shovel", "Multitool" }
      },

      {
        "Hammer",
        new List<string> { "Hammer", "Iron Hammer", "Multitool" }
      },
      // End Tools

      // === Items 

      // -- ores and metals
      {
        "Metal Bar",
        new List<string> { "Iron Bar", "Gold Bar", "Unobtainium Bar" }
      },

      {
        "Metal Block",
        new List<string> { "Iron Block", "Gold Block", "Cobalt Block", "Unobtainium Block" }
      },

      {
        "Ore",
        new List<string> {
          "Coal Ore", "Iron Ore", "Gold Ore", "Cobalt Ore", "Meteorite Ore", "Devil Coal Ore" }
      },

      {
        "Gem", // Ruby, Emerald, Sapphire, Cryptonite
        new List<string> { "Red Crystals", "Green Fluorite", "Blue Crystals",  "Cryptonite" }
      },

      // end ores and metals

      // --- Bottles and notes 
      {
        "Bottle With Note",
        new List<string> { "Bottle Empty Note", "Bottle Poi Note", "Bottle son Note" }
      },

      {
        "Note With Symbols",
        new List<string> { "Note son", "Note Poi", "Note With Symbols" }
      },
      // -- End bottles and notes
      // -- Records 
{
  "Record",
  new List<string> {
    "Record Abyss", "Record Aragog", "Record Arpeggio", "Record Ashkore", "Record Atmosphere", "Record Atmosphere Piano",
    "Record Begin", "Record Boss Fight", "Record Dark Boss", "Record Desrtino", "Record Dragon Lord",
    "Record Guitar Accords", "Record Haven", "Record Mati", "Record Menu Theme", "Record Miracles", "Record Mistery",
    "Record New Forest", "Record Ocean Ripples", "Record Phoenix", "Record Pip Orquesta", "Record Pirates - Ending",
    "Record Pirates - Silent Sea", "Record Siren", "Record Solar", "Record Space Mushrooms", "Record Swamp Golem",
    "Record Tradiore - 8bit", "Record Tradiore - Indie", "Record Tradiore - Ragga", "Record Tradiore - Ray of sunset",
    "Record Tradiore - Serenity", "Record Tradiore - Space 80s", "Record Tradiore - Thoughts",
    "Record Tradiore - Walpurgis Night", "Record Vanity", "Record Wonderland" }
},
      // -- End records

      // --- Fish
      {
        "Raw Fish",
        new List<string> {
          "ClownFish", "GruntFish", "LionFish", "Purplefish", "TriggerFish", "Stingray", "Shark", "Squid", "StarFish",
          "Urchin", "Fangtooth", "Gulper", "DumboOctopus", "Dunkleosteus" }
      },

      {
        "Cooked Fish",
        new List<string> { "Fried ClownFish", "Fried GruntFish", "Fried LionFish", "Fried Purplefish", "Fried TriggerFish",
        "Fried Stingray", "Fried Shark", "Fried Squid", "Fried StarFish", "Fried Urchin", "Fried Fangtooth", "Fried Gulper",
        "Fried DumboOctopus", "Fried Dunkleosteus" }
      },

      {
        "Clownfish",
        new List<string> { "ClownFish", "Fried ClownFish" }
      },
      {
        "Gruntfish",
        new List<string> { "GruntFish", "Fried GruntFish" }
      },
      {
        "Lionfish",
        new List<string> { "LionFish", "Fried LionFish" }
      },
      {
        "Purplefish",
        new List<string> { "Purplefish", "Fried Purplefish" }
      },
      {
        "Triggerfish",
        new List<string> { "TriggerFish", "Fried TriggerFish" }
      },
      {
        "Stingray",
        new List<string> { "Stingray", "Fried Stingray" }
      },
      {
        "Shark",
        new List<string> { "Shark", "Fried Shark" }
      },
      {
        "Squid",
        new List<string> { "Squid", "Fried Squid" }
      },
      {
        "Starfish",
        new List<string> { "StarFish", "Fried StarFish" }
      },
      {
        "Sea Urchin",
        new List<string> { "Urchin", "Fried Urchin" }
      },
      {
        "Fangtooth",
        new List<string> { "Fangtooth", "Fried Fangtooth" }
      },
      {
        "Gulper",
        new List<string> { "Gulper", "Fried Gulper" }
      },
      {
        "Dumbo Octopus",
        new List<string> { "DumboOctopus", "Fried DumboOctopus" }
      },
      {
        "Dunkleosteus",
        new List<string> { "Dunkleosteus", "Fried Dunkleosteus" }
      },

      // --- end fish
      
      // -- animal drops 
      {
        "Animal Skin",
        new List<string> { "BoarSkin", "Deerskin", "HorseSkin", "SheepSkin" }
      },

      // -- end animal drops 
      // === end Items 

      // == Animals 
      {
          "Soul",
          new List<string> {
            "Baby Ashkore Soul", "Cat Soul", "Daithir Soul", "Dog Soul", "Dragon Soul", "Eldriar Soul", "Alien Dog Soul",
            "Mati Soul", "Onyx Soul", "Rainbow Basilisk Soul", "Spider Soul", "Vairmut Soul", "Xordaraxus Soul" }
      },

    // == End Animals
    };

  public static List<string>? GetGroup(string groupName)
  {
    return _groups.TryGetValue(groupName, out var list) ? list : null;
  }


  public static string? GetCanonical(string groupName)
  {
    var list = GetGroup(groupName);
    return list?[0];
  }



  // Test to see if all the items defined in our groups are valid.
  public static void CheckGroupValidity()
  {
    if (_ranTests)
    {
      return;
    }
    _ranTests = true;

    Print("Testing built-in groups...");
    Queue<KeyValuePair<string, string>> badEntries = []; // group, itemName
    int allCount = 0;
    int okCount = 0;


    // Test
    foreach (KeyValuePair<string, List<string>> groupEntry in _groups)
    {

      string groupName = groupEntry.Key;
      List<string> group = groupEntry.Value;

      if (group.Count > 138)
      {
        Warn($"Group '{group}' has more than 138 items (has {group.Count}! Validation checks in GenericRecipeDirective" +
            " may overflow with particularly degenerate recipes. Please fix this.");
      }

      int itemsPassed = 0;

      // test group
      foreach (string item in group)
      {
        allCount++;
        if (InvDatabase.FindByName(item) == null)
        {
          Warn($"'{item}' @ '{groupName}' does not exist in the InvDatabase");
          badEntries.Enqueue(new KeyValuePair<string, string>(groupName, item));
        }
        else
        {
          if (RecipeLoader.QuieterTests == false)
          {
            Debug($"'{item}' OK!");
          }
          itemsPassed++;
        }

      }
      if (RecipeLoader.QuieterTests == false)
      {
        Print($"{groupName} : {itemsPassed} / {group.Count} entries valid");
      }
      okCount += itemsPassed;

    } // End tests

    Print($"Group tests done! ({okCount} / {allCount} total entries valid)");
    if (badEntries.Count > 0)
    {
      Print("Bad entries: (Please notify the mod author about this)");
      while (badEntries.Count > 0)
      {
        KeyValuePair<string, string> badentry = badEntries.Dequeue();
        Print($"{badentry.Key} : {badentry.Value}");
      }
    }

  }
}

// NOTE: We're using Priority.Last here in case we end up supporting user-made groups later 
// AND the user is using a plugin that adds new items to the database. We want to run the tests 
// after in case their groups use custom items
// I.e, we'd add the custom groups in the prefix
[HarmonyPatch(typeof(InvDatabase), "Awake")]
[HarmonyPriority(Priority.Last)]
internal static class GroypTests
{
  private static int _databases = 0;

  [HarmonyPostfix]
  internal static void Patch()
  {
    int count = Interlocked.Increment(ref _databases);
    if (count == InvDatabase.list.Length) // All databases initialized
    {
      RLItemGroups.CheckGroupValidity();
    }
  }
}
