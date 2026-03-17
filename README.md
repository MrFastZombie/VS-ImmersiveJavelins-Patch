# Immersive Javelins Unofficial Patch

Vintage Story mod that patches the Immersive Javelins mod to make it work on servers and newer versions of Vintage Story.

Moddb link: <https://mods.vintagestory.at/vsimmersivejavelinspatch>

Immersive Javelins link: <https://mods.vintagestory.at/immersivejavelins>

## Building

 You need two environment variables set before compiling this mod:

* **VINTAGE_STORY_DEV_JAVELINS**: Pointing to a Vintage Story installation folder (where the .exe is).
* **VINTAGE_STORY_DEV_JAVELINS_DATA**: Pointing to that installation's data folder (the folder containing your mods folder).

This project contains a task file and launch file that should then allow compilation and debug with VS Code. If you different dev software then you will need to figure out how to run it yourself.

You can alternatively replace the file paths in launch.json, CakeBuild.csproj, & VSImmersiveJavelinsPatch.csproj if needed, just remember to not include these changes in a pull request to this project.

Use the "package" task to build and package the mod into a zip file.
