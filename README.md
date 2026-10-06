# Moon1265: Dethmun

A new moon for Kerbin in **Kerbal Space Program 1** (1.8 to 1.12): a perfectly flat, grey moon
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

1. Install [Kopernicus](https://github.com/Kopernicus/Kopernicus) for your KSP version
   (CKAN works). It includes ModuleManager.
2. Copy `GameData/Moon1265` into your KSP `GameData` folder.
3. Start the game. Dethmun shows up in the Tracking Station orbiting Kerbin.

It works with existing saves too, as long as nothing else uses
`flightGlobalsIndex = 1265`.

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
  PluginData/height.png       heightmap (white = surface, black = trench)
  PluginData/surface_color.png  ground colour close up
  PluginData/biomes.png       biome map
  PluginData/scaled_color.png   map-view / distant texture
  PluginData/scaled_normal.png  flat normal map
tools/generate_textures.py    regenerates all the textures
```
