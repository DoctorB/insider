Insider Mod Loader - Windows x64 pre-alpha
==========================================

This build supports only Unity games that use the Mono scripting backend.

Install:
  dotnet insider.dll inspect "C:\Path\Game.exe"
  dotnet insider.dll install "C:\Path\Game.exe"

Verify or remove:
  dotnet insider.dll status "C:\Path\Game.exe"
  dotnet insider.dll uninstall "C:\Path\Game.exe"

Plugins belong in <game>\Insider\plugins.
Managed plugin dependencies belong in <game>\Insider\plugins\dependencies.
Each plugin receives its entry-assembly directory through context.PluginDirectory.
Its private persistent directories are exposed through context.ConfigDirectory
and context.DataDirectory. Plugins own the files below those two directories;
Insider does not define a configuration format or serializer.
Optional disabled plugin ids belong one per line in
<game>\Insider\config\disabled-plugins.txt.
Manage that list with:
  dotnet insider.dll plugins disable "C:\Path\Game.exe" com.example.plugin
  dotnet insider.dll plugins disabled "C:\Path\Game.exe"
  dotnet insider.dll plugins enable "C:\Path\Game.exe" com.example.plugin
Changes take effect on the next game start.
Logs are written to <game>\Insider\logs.
Managed detours and IL hooks use the loader-owned MonoMod.RuntimeDetour backend
and are removed automatically when their owning plugin unloads.
Plugins can schedule Unity-facing work through context.MainThread; pending work
is invalidated automatically when its owning plugin unloads.
Plugin configuration and data directories are preserved during uninstall.

License terms are included in LICENSE and THIRD_PARTY_NOTICES.md.

Keep a backup of the game installation. This software is pre-alpha.
The CLI requires the Microsoft .NET 10 runtime.
