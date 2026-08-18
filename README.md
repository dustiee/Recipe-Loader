# Recipe Loader 
## Description
A BepInEx plugin for Block Story that allows inserting custom recipes, and deleting/replacing existing ones via .xml files. <br>

Documentation for making recipes and available elements/attributes is in this <u>[guide](GUIDE-TO-RECIPES.md)</u>.

Recipes using groups have recipes for its variants constructed in the background as recipes in the Hide category.

You get about a 40,000 hidden recipe budget if you're willing to wait up to 3 seconds on game launch, which is probably more than enough. <br>
That "40,000" number comes from testing on my own computer, which is quite old, but your mileage may vary.

Some reasons as to why you might want to use this:

0. Handles conflicts automatically
0. Has convenience features such as exactCount and item groups
0. Logs issues with xml recipe files
0. No c# experience required to get started with making recipes
0. Includes a small transpiler patch that makes the game parse recipes ~20% faster.

Some reasons as to why you might **not** want to use this:

0. You need recipes added during gameplay and not during initialization
0. Implementation is too inefficient for your use case, or you have a better way of doing what this mod does

Initially made for personal use, but maybe it'll be useful for others.

### Requirements

0. BepInEx properly installed in the BlockStory directory. Installation guide for BepInEx is available <u>[here](https://docs.bepinex.dev/articles/user_guide/installation/index.html).</u>

## Installation 

Download the latest release and move ```RecipeLoader.dll``` into ```/path/to/BlockStory/BepInEx/plugins/```

## Extras

You can use [General Exporter](https://github.com/dustiee/General-Exporter) with this mod to get the game's recipes prepared in a format for Recipe Loader. You may 
want to use that when replacing or deleting recipes, since not all in-game recipe display names map cleanly to their 
actual recipe name.

## Building prerequisites

You'll need the game's assemblies, so you'll need to paste Assembly-CSharp.dll from the game's ```Managed``` folder into ./lib 

## Future possible improvements:

- Optimize directive collection and their usage in the patch if they start causing performance problems
- Make Recipe objects directly to increase hidden recipe budget. <br>
  The game's RecipeManager parsing seems to be the bottleneck currently. <br>
  It's probably fine to leave it as is for now, though. I think the recipe budget is okay at the moment.
- Replace the recipe system altogether with a better one
- Add a better indicator to recipes using groups (probably only possible by replacing the entire system)

### Comments

The best idea in the future probably would be to entirely replace the way recipes are parsed and matched. This would 
allow item groups to be supported more naturally since we could avoid having to generate hidden recipes, and that
would also allow us to remove any unreasonable quirks the current recipe system has.

I guess that would make this a bit more than just a recipe loader, and it wouldn't be as true to the actual game 
anymore, but that's probably okay.
I don't know enough about how the recipe system works yet to try something like this currently, and it's likely 
not worth doing now anyway given that the game seems fairly unstable given the recent development updates.

## Disclaimer

This software is publicly available in the hope that it will be useful. I do not take responsibility for maintaining or 
improving it in the future.
