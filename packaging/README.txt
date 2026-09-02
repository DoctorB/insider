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
Logs are written to <game>\Insider\logs.
Managed detours and IL hooks use the loader-owned MonoMod.RuntimeDetour backend
and are removed automatically when their owning plugin unloads.
Plugins can schedule Unity-facing work through context.MainThread; pending work
is invalidated automatically when its owning plugin unloads.

License terms are included in LICENSE and THIRD_PARTY_NOTICES.md.

Keep a backup of the game installation. This software is pre-alpha.
The CLI requires the Microsoft .NET 10 runtime.
