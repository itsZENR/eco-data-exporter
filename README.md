# Eco Market Dumper

An experimental client-side JSON exporter for Eco, preserved as an inactive source snapshot. Active development has stopped, and compatibility with current Eco releases has not been verified.

Plugin version: **0.2.5**. Original source package: **v4.5**.

## Features

- **F8:** export the currently loaded Economy Viewer market, including prices, stock, currencies and store metadata.
- **F9:** export recipes, skills, referenced items, talents and diagnostic modifier information.
- Write timestamped JSON files and a latest copy to `MarketDumps/` and `EcoDataDumps/` under the game directory.

This is a data exporter. It does not include a pricing calculator or trading bot. Exports can contain player names, skill state and store information; no real player or server exports are included here.

## Original target environment

- Eco **0.14.1.1**
- **.NET 6**
- **BepInEx 6 IL2CPP** and its generated interop assemblies

The original implementation and instructions are preserved in `Plugin.cs`, `EcoMarketDumper.csproj` and `README.txt`. `SOURCE-MANIFEST.json` records their SHA-256 hashes.

## Building

Use an existing licensed game installation containing the required Eco, Unity, BepInEx and Il2CppInterop assemblies. These dependencies are not redistributed in this repository.

Set the MSBuild `GameDir` property to that installation. For example, in PowerShell:

```powershell
dotnet build -c Release '-p:GameDir=D:\SteamLibrary\steamapps\common\Eco'
```

The original project automatically copies the compiled plugin into `BepInEx/plugins/EcoMarketDumper` under `GameDir` after a successful build. Restart Eco after replacing the plugin; BepInEx does not hot-reload it.

No build or in-game compatibility test was performed while preparing this source archive. See `README.txt` for the original v4.5 changes and known research context.

## Archive scope

The three original source/documentation files are preserved without changes. Local build outputs in `bin/` and `obj/` are excluded from Git and retained in a separate complete local backup. No earlier Git history was present in the source directory.

This is an unofficial project and is not an endorsement or permission from Strange Loop Games. Publication of the source does not establish that this integration is permitted on a particular server. No license for third-party game content is granted by this repository.
