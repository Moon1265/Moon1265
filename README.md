# Moon1265: Dethmun

> **Work in progress:** the first-person EVA and rifle code in `Source/` is a first draft
> that has not been compiled or tested yet. The moon itself works on its own.

A new moon for Kerbin for **Kerbal Space Program 1.12.5**: a perfectly flat, grey moon
with a trench running all the way around its equator, Death Star style.

The challenge it's built for: EVA a Kerbal from the surface, use the jetpack to
reach an orbit just above the ground, then lower that orbit into the trench and
fly laps between the walls.

## What you get

| | Dethmun | (Minmus, for comparison) |
|---|---|---|
| Parent | Kerbin | Kerbin |
| Orbit | 30,000 km, circular, equatorial | 47,000 km, 6° inclined |
| Radius | 60 km | 60 km |
| Surface gravity | 0.05 g (0.49 m/s²) | 0.05 g |
| Atmosphere | none | none |
| Terrain | perfectly smooth sphere | hills and flats |
| Trench | ~200 m wide, 100 m deep, on the equator | – |

Handy numbers for the trench run:

- **Orbital speed at the surface:** about 171.5 m/s, with one lap every ~37 minutes.
  The moon spins eastward at about 9 m/s at the equator, so launch east.
- **How flat your orbit has to be:** to stay inside a 200 m trench, your inclination
  has to stay below about **0.09°** (the trench's half-width divided by the radius).
  Take off from inside the trench or right beside it. Launching from anywhere else
  gives you at least that latitude as inclination.
- **Biomes:** `Plains` and `Trench`, so you can grab EVA reports from both.

## Installing

1. Install the KSP 1.12.x release of [Kopernicus](https://github.com/Kopernicus/Kopernicus/releases)
   together with its dependencies, ModuleManager and ModularFlightIntegrator.
   CKAN (with your game set to 1.12.5) installs all three for you.
2. Copy `GameData/Moon1265` into your KSP `GameData` folder.
3. Start the game. Dethmun shows up in the Tracking Station orbiting Kerbin.

It works with existing saves too, as long as nothing else uses
`flightGlobalsIndex = 1265`.

## First-person EVA and rifle (draft)

On EVA, press **Y** to look through your Kerbal's eyes. Then:

| Control | Action |
|---|---|
| Mouse | look |
| WASD / jetpack keys | move, same as normal EVA |
| Left mouse | fire (automatic) |
| Right mouse | aim down sights |
| T | reload |
| Y | back to the normal camera |

Bullets hit Kerbals (4 hits) and ship parts (more hits for heavier parts), push them
around, and blow them up when their health runs out. Firing pushes you backwards, which
matters in space. Keys, damage, recoil and so on are in `GameData/Moon1265/Settings.cfg`.

### Building the plugin

The C# code has to be compiled against your own copy of KSP 1.12.5. Install the
[.NET SDK](https://dotnet.microsoft.com/download), then from the repo folder:

```
dotnet build Source\Moon1265\Moon1265.csproj -c Release -p:KSPDIR="C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
```

This puts `Moon1265.dll` in `GameData/Moon1265/Plugins` and copies the whole mod into your game.

## Tuning the trench

- **Width:** run `python3 tools/generate_textures.py --trench-width 300` (any number of
  metres; standard-library Python only, no installs). This rewrites the textures in
  `GameData/Moon1265/PluginData`.
- **Depth:** in `GameData/Moon1265/Dethmun.cfg`, under `VertexHeightMap`, set `deformity`
  to the depth and `offset` to minus that same number (for example `offset = -150`,
  `deformity = 150`).
- **Wall sharpness:** `PQS { maxLevel = 10 }` sets how finely the ground is meshed near
  you. Raise it to 11 for crisper walls, or lower it if your PC struggles.

After changing anything, delete `GameData/Kopernicus/Cache/Dethmun.bin` if it exists, so
the map-view mesh gets rebuilt.

## Files

```
GameData/Moon1265/
  Dethmun.cfg                 Kopernicus body definition
  Moon1265.version            KSP-AVC version file (KSP 1.12.5)
  Settings.cfg                first-person and rifle settings
  Plugins/                    compiled Moon1265.dll goes here
  PluginData/height.png       heightmap (white = surface, black = trench)
  PluginData/surface_color.png  ground colour close up
  PluginData/biomes.png       biome map
  PluginData/scaled_color.png   map-view / distant texture
  PluginData/scaled_normal.png  flat normal map
tools/generate_textures.py    regenerates all the textures
Source/Moon1265/              C# plugin: first-person EVA camera and rifle
```
