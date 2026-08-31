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
Logs are written to <game>\Insider\logs.

Keep a backup of the game installation. This software is pre-alpha.
The CLI requires the Microsoft .NET 10 runtime.
