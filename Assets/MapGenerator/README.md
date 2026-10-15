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

`TerrainBuilder` exposes exactly one entry point: `internal void Build(Texture2D[] heightmaps, Vector2[] sizes, float heightMeters)`. All its other methods and fields are private. It has no references to the generator or controller; `MapWorkbench` supplies finished data. Its serialized settings control the common center, overlap depth, seam blending, Terrain rendering error, material and surface layer.

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

All output textures use linear R16 and the same normalized height range. `Шкала высот` converts that range to metres for slope limiting; it is configurable because the source elevation encoding is not documented. No per-map normalization is applied.

Water overrides source elevations to zero. A distance-based coastal ramp fades land toward water. The main rectangle uses Gaussian smoothing and height compression sufficient to bound its bilinear surface slope. Outside that rectangle, a smooth transition returns to the source relief before reaching the middle rectangle edge. All levels evaluate the same world-space height function, so coincident coordinates agree; coarser Terrain grids can introduce interpolation differences, handled with the construction overlap band.

Preview brightness affects only rendering, not stored heights. The crop grid uses point filtering; green/orange/blue rectangles identify the three footprints. Dense grids are hidden above 151 samples per side to keep the preview readable.

Runtime regeneration runs on a background task. Changing the selection cancels the previous result, waits for a short drag debounce and generates the latest selection. Unity textures are created on the main thread. Play-mode parameter changes follow normal Unity behavior and reset on leaving Play mode.

## Files and checks

`Generated/Heightmap1.asset` through `Heightmap3.asset` contain the saved initial previews. The separate root object `MapGenerator` owns input maps, settings and output textures. Its internal `Generate` / `GenerateAsync` methods work without a Canvas. Other project systems read outputs through internal `GetHeightmap(level)`.

`MapWorkbench` on the Canvas handles input and display only. `MapSelectionGraphic` handles selection outlines and pointer input. `HeightField` contains the numerical algorithm, and `MapSettings` contains serialized configuration. All these types are internal; configurable fields are `[SerializeField] private`. Settings are copied when read or configured, so callers cannot silently mutate a running generation. Editor tools access internal methods through `InternalsVisibleTo("Assembly-CSharp-Editor")`; inspector references are assigned through `SerializedObject`.

`Tools > Tactical Maps > Verify Height Generation` checks water override, rectangular output, shared coordinates, slope, transitions, source borders and cancellation. It also creates a standalone generator without UI, verifies private serialized fields and the internal API, and checks that settings are defensively copied.

`Tools > Tactical Maps > Create MainScene Workbench` builds the interface, generator and construction object in an open MainScene that has no workbench yet. It refuses to overwrite an existing workbench. Scene creation uses Unity Editor APIs.

Unity Pipeline is included to allow editor automation; TMP Essentials and a local Arial font asset provide the Russian interface.
