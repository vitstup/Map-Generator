# Tactical map heightmap workbench

Open `Assets/Scenes/MainScene.unity`, select the Game tab and enter Play mode.
Click or drag on the large map. The selected source pixels and all three heightmaps update automatically.
`СГЕНЕРИРОВАТЬ` also regenerates the images after editing parameters.
`ПОСТРОИТЬ TERRAIN` passes the completed output to the separate `TerrainBuilder`. Once built during Play mode, terrains are rebuilt after subsequent successful generations. A pending generation finishes before construction starts. Inspect the geometry in the Scene tab; the existing full-screen Canvas remains the testing interface.

## Runtime groups

| Folder | Namespace | Responsibility |
| --- | --- | --- |
| `Runtime/Testing` | `TacticalMaps.Testing` | Temporary `MapWorkbench` controller and selection UI |
| `Runtime/Generation` | `TacticalMaps.Generation` | Source data, settings, height calculation and output textures |
| `Runtime/Construction` | `TacticalMaps.Construction` | Terrain spawning, height upload, overlap and resource lifetime |

`TerrainBuilder` exposes exactly one entry point: `internal void Build(MapData map)`. All its other methods and fields are private. It has no references to the generator or controller; `MapWorkbench` supplies finished data. Its serialized settings control the common center, overlap depth, seam blending, Terrain rendering error, material and surface layer.

The three centered terrains preserve their individual world dimensions and a shared vertical scale. Unity Terrain requires a square height grid of `2^n + 1` samples; a rectangular input is bilinearly resampled to the next supported square resolution, without changing the input texture or the rectangular world dimensions. Background terrain interiors are lowered and cut out beneath the previous level, retaining an overlap band. Only the main terrain collider is enabled. Rebuilding replaces only this builder's previous output and releases its transient TerrainData; saved project assets are preserved.

MainScene contains an initial built set with saved data in `Generated/Terrains`. `Tools > Tactical Maps > Set Up Terrain Construction` installs construction in a workbench scene and saves an initial preview. `Tools > Tactical Maps > Verify Terrain Construction` checks dimensions, height orientation, rectangular resampling, holes, collider selection, replacement, cleanup and the single-entry-point API in a preview scene.

## Coordinates and scale

- Source coordinates start at the bottom left, matching Unity texture coordinates.
- One radius controls the shared source square: radius 50 gives 101 × 101 samples.
- The entire square maps to level III. Levels I and II sample its center according to their world dimensions relative to level III.
- Each level has independent texture width/height and world width/height. Dimensions need not be square. Every next world rectangle must contain the previous one.
- Initial textures are 2049 × 2049, with world sizes 2048, 8096 and 20000 metres.
- A pixel outside the source image is water; the selection is not shifted at the image edges.

## Heights

The source heightmap is imported as readable, linear, uncompressed R16 at its full 4096 × 4096 resolution. The land mask uses R8; white is land. Source images themselves are unchanged.

All outputs use linear R16 with a common vertical reference. `GlobalMaxHeightMeters` is the elevation represented by source value 1 above sea level. `TerrainHeightMeters` is the physical range from terrain base to heightmap value 1. `SeaLevelMeters` is the water elevation above that base. On inland unsmoothed land, physical height is `SeaLevelMeters + source * GlobalMaxHeightMeters`, normalized by `TerrainHeightMeters`. These settings are common to all three levels. Terrain range must accommodate the sea level plus the full reference elevation. Defaults (4000, 4000, 0) preserve the previous reference. The sea value is metadata for water and other consumers; this stage does not create a water surface. No per-level normalization is applied.

Water-mask samples override source elevations to the minimum seabed height, zero. A coastal ramp fades from land to seabed. With a nonzero sea level the main terrain uses a widened sea baseline and reserves slope budget for it, then compresses land elevation above that baseline. The main rectangle uses Gaussian smoothing and height compression sufficient to bound its bilinear surface slope. Outside that rectangle, a smooth transition returns to the source relief before reaching the middle rectangle edge. All levels evaluate the same world-space height function, so coincident coordinates agree; coarser Terrain grids can introduce interpolation differences, handled with the construction overlap band.

Preview brightness affects only rendering, not stored heights. The crop grid uses point filtering; green/orange/blue rectangles identify the three footprints. Dense grids are hidden above 151 samples per side to keep the preview readable.

Runtime regeneration runs on a background task. Changing the selection cancels the previous result, waits for a short drag debounce and generates the latest selection. Unity textures are created on the main thread. Play-mode parameter changes follow normal Unity behavior and reset on leaving Play mode.

## Files and checks

`Generated/Heightmap1.asset` through `Heightmap3.asset` contain the saved initial previews. The root object `MapGenerator` owns input maps and generation settings. `internal MapData Generate(Vector2Int point)` returns a completed map; `GenerateAsync(point, cancellation)` returns `Task<MapData>`. The point is an argument, never a generation setting. The generator stores no generated textures or current result. Its colocated `HeightmapGenerator` component implements the height stage and source cache; only MapGenerator calls that component. Further map-generation stages belong behind the same facade.

`MapData` is an internal result class: properties use internal getters and private setters. It contains three read-only heightmap references, read-only world sizes, the common terrain height, sea level, global elevation reference, selected source point and compression metadata. Texture dimensions are read from the textures. Results remain valid across later generations and generator destruction. The caller owns transient textures and calls `map.Release()` when finished; the operation is idempotent and preserves saved assets.

`MapWorkbench` owns the current returned map and selection point. It calls only MapGenerator and TerrainBuilder, replaces and releases old results, and releases canceled/stale results. `MapSelectionGraphic` handles selection outlines and pointer input. `HeightField` contains the numerical algorithm, and `MapSettings` contains serialized configuration. All these types are internal; configurable fields are `[SerializeField] private`. Settings are copied when read or configured, so callers cannot silently mutate a running generation. Editor tools access internal methods through `InternalsVisibleTo("Assembly-CSharp-Editor")`; inspector references are assigned through `SerializedObject`.

`Tools > Tactical Maps > Verify Height Generation` checks water override, rectangular output, shared coordinates, slope, transitions, source borders and cancellation. It also checks standalone generation, private configuration, internal result getters/private setters, independent result lifetime, point arguments, defensive settings copies, nonzero-sea coast slopes and conversion between the global reference and different Terrain ranges.

`Tools > Tactical Maps > Create MainScene Workbench` builds the interface, generator and construction object in an open MainScene that has no workbench yet. It refuses to overwrite an existing workbench. Scene creation uses Unity Editor APIs.

Unity Pipeline is included to allow editor automation; TMP Essentials and a local Arial font asset provide the Russian interface.
