# Tactical map heightmap workbench

Open `Assets/Scenes/MainScene.unity`, select the Game tab and enter Play mode.
Click or drag on the large map. The selected source pixels and all three heightmaps update automatically.
`СГЕНЕРИРОВАТЬ` also regenerates the images after editing parameters.
`ПОСТРОИТЬ КАРТУ` passes the completed output to the separate `MapBuilder`. Once built during Play mode, terrains are rebuilt after subsequent successful generations. A pending generation finishes before construction starts. Inspect the geometry in the Scene tab; the existing full-screen Canvas remains the testing interface.

## Runtime groups

| Folder | Namespace | Responsibility |
| --- | --- | --- |
| `Runtime/Testing` | `TacticalMaps.Testing` | Temporary `MapWorkbench` controller and selection UI |
| `Runtime/Generation` | `TacticalMaps.Generation` | Source data, settings, height calculation and output textures |
| `Runtime/Construction` | `TacticalMaps.Construction` | Terrain and water spawning, appearance, overlap and resource lifetime |

`MapBuilder` exposes exactly one entry point: `public void Build(MapData map)`. All its other methods and fields are private. It has no references to the generator or controller; `MapWorkbench` supplies finished data. MapBuilder owns the common center and completed map lifetime. TerrainGenerator owns terrain layer, overlap, rendering error, terrain material and detail distance. WaterGenerator owns the water layer, material and mesh density. Both generators are on the same object and are called only by MapBuilder. Build is transactional: a failed stage keeps the previous map and cleans its partial replacement.

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

All outputs use linear R16 and one shared vertical range. `TerrainHeightMeters` is the physical range from base to heightmap value 1; `SeaLevelMeters` must be nonnegative and below that range. The global height is a source reference and no longer forces the Terrain range. Inland elevation is `sea + source * (terrainHeight - sea)`: source 0.7, Terrain 1000 and sea 50 give 715 metres before smoothing/compression. Terrain 1000 with global reference 4000 is valid. No per-level normalization is applied.

Water-mask samples ignore the global heightmap. The seabed starts near sea level at the shore and descends smoothly to zero with distance into water. `Seabed Pixels` controls its width in source pixels; a wider ramp is used when necessary to reserve the main terrain's slope budget. `Coast Pixels` controls the above-water coast transition. Gaussian smoothing and compression affect the main land relief; a transition restores background relief outside the main rectangle. Water depth remains a shared reference for all levels. Coincident world coordinates agree, with the construction overlap band handling resampling differences.

Preview brightness affects only rendering, not stored heights. The crop grid uses point filtering; green/orange/blue rectangles identify the three footprints. Dense grids are hidden above 151 samples per side to keep the preview readable.

Runtime regeneration runs on a background task. Changing the selection cancels the previous result, waits for a short drag debounce and generates the latest selection. Unity textures are created on the main thread. Play-mode parameter changes follow normal Unity behavior and reset on leaving Play mode.

## Files and checks

`Generated/Heightmap1.asset` through `Heightmap3.asset` contain the saved initial previews. The root object `MapGenerator` owns input maps and generation settings. `internal MapData Generate(Vector2Int point)` returns a completed map; `GenerateAsync(point, cancellation)` returns `Task<MapData>`. The point is an argument, never a generation setting. The generator stores no generated textures or current result. Its colocated `HeightmapGenerator` component implements the height stage and source cache; only MapGenerator calls that component. Further map-generation stages belong behind the same facade.

`MapData` is a public result class: properties use internal getters and private setters. It contains three read-only heightmap references, read-only world sizes, the common terrain height, sea level, global elevation reference, selected source point and compression metadata. Texture dimensions are read from the textures. Results remain valid across later generations and generator destruction. The caller owns transient textures and calls `map.Release()` when finished; the operation is idempotent and preserves saved assets.

`MapWorkbench` owns the current returned map and selection point. It calls only MapGenerator and MapBuilder, replaces and releases old results, and releases canceled/stale results. `MapSelectionGraphic` handles selection outlines and pointer input. `HeightField` contains the numerical algorithm, and `MapSettings` contains serialized configuration. The test controller and implementation helper types are internal; configurable fields are `[SerializeField] private`. Settings are copied when read or configured, so callers cannot silently mutate a running generation. Editor tools access internal methods through `InternalsVisibleTo("Assembly-CSharp-Editor")`; inspector references are assigned through `SerializedObject`.

`Tools > Tactical Maps > Verify Height Generation` checks water override, rectangular output, shared coordinates, slope, transitions, source borders and cancellation. It also checks standalone generation, private configuration, internal result getters/private setters, independent result lifetime, point arguments, defensive settings copies, nonzero-sea coast slopes and proportional scaling into different Terrain ranges and a seabed that deepens away from shore.

`Tools > Tactical Maps > Create MainScene Workbench` builds the interface, generator and construction object in an open MainScene that has no workbench yet. It refuses to overwrite an existing workbench. Scene creation uses Unity Editor APIs.

Unity Pipeline is included to allow editor automation; TMP Essentials and a local Arial font asset provide the Russian interface.

## Water

`Runtime/Construction` contains MapBuilder (public facade with one public `Build(MapData)` method), TerrainGenerator and WaterGenerator (internal stages). Inspector layer fields are actual layer dropdowns, not LayerMasks. Existing project layers Terrain and Water are used; no camera or input code is required by the package.

`Materials/LowPolyWater.mat` uses `Shaders/LowPolyWater.shader` for URP. The water is a single continuous planar mesh covering the outer map. The axis spacing is denser in the main region and coarser in the middle/outer bands, without separate LOD surfaces or seams. Default settings give 31,329 vertices and 61,952 triangles in one transparent forward pass. Water has no collider, reflection camera, shadow-casting pass, screen-depth requirement, refraction or CPU Update animation. These are geometry/draw budgets, not a measured frame-rate guarantee.

Two waves displace vertices on the GPU. Fragment derivatives supply flat triangle normals for the low-poly style. The material controls amplitude, wavelength, speed, shallow/deep colors, approximate sky tint, sun glints and animated shoreline foam. The mesh bounds allow displacement. Wave amplitude fades near shore; water fragments with effectively zero depth are clipped. A small surface offset avoids coincident surfaces. Sea level zero has no physical water depth; use a positive level for visible water and its depth-based transparency, color and foam.

Water snapshots the three heightmaps at up to 1025 / 513 / 257 pixels per axis (about 2.8 MB of R16 texels for square defaults). They provide world-space seabed depth and land clipping without changing renderer or camera settings. The snapshots and a per-built-map material belong to MapBuilder; later MapData.Release calls cannot invalidate water. Rebuilds dispose the previous transient meshes, terrain data, material and snapshots, while preserving saved assets.

MainScene has a saved preview in `Generated/MapPreview`. Change the source water material, then rebuild to apply it to a new surface. Test controls continue to call only MapGenerator and MapBuilder. All implementation and generated assets remain under Assets/MapGenerator.

## Terrain surfaces and biomes

`BiomeGenerator` is an internal component on the MapGenerator object. Only the facade invokes it. Its private serialized layer references and settings control six replaceable surfaces: meadow, dry grass, earth, rock, sand and silt. Settings cover moisture, coast influence, altitude dryness, valley radius/moisture, regional climate, erosion slopes, rock slopes, beach height, silt depth, seed and splat resolutions. Regional randomness is only a subtle climate bias; height, gradient, aspect and topographic position select coverage. There is no Perlin color pattern.

MapData.Surface contains the shared layer library, three normalized splat arrays and common texture origin. Async generation snapshots height pixels on the main thread and computes weights on a worker with cancellation. TerrainGenerator uploads these finished weights and clones TerrainLayers with aligned texture phase; it performs no biome calculation. MapBuilder owns the layer clones, terrain data and water resources; albedo/normal textures belong to the shared asset library and survive rebuilds.

`Textures/Surfaces` holds six generated albedos plus normal-map import sources. `Materials/TerrainLayers` holds their editable TerrainLayer assets. Change a layer's albedo, normal map, normal strength, tint/remap, smoothness and Tile Size directly; tile dimensions are metres (defaults about 6–13 m), independent of terrain size. Mipmaps, anisotropic filtering and normals keep close views detailed and reduce shimmer. Slightly different tile scales and blending of related surfaces reduce visible repetition. No geometry grass is spawned.

The main splat map is 1024 square, backgrounds 512. These maps control material coverage, not fine surface detail. The matte terrain shader reuses URP terrain instancing, holes, lighting and shadows. Six layers can require two terrain layer passes; these are conventional Terrain materials, not a one-pass or measured FPS guarantee. `Detailed Surface Distance` on TerrainGenerator controls the distance before Unity's cheaper far basemap.

`Tools > Tactical Maps > Set Up Terrain Surface Library` configures/imports the six albedo/normal pairs and assigns the generation component. It also enables animated material updates in the active Scene View. `Rebuild Saved Map Preview` regenerates the current selection and saves MainScene. Existing generator height settings and camera setup are preserved.

Water now combines GPU vertex waves, drifting fine ripple normals, moving crest highlights and irregular shore foam. The material exposes Ripple Size/Strength, Flow Speed and Ripple Highlights as well as the original wave settings. Transparent blending and depth-dependent opacity remain. In Scene View, animated materials and Always Refresh must be enabled to see continuous motion outside Play Mode; the setup tool enables them. Runtime animation uses shader time with no per-frame CPU mesh updates.

`Verify Biome Generation` checks deterministic normalized weights, coast/bed/slope rules, rectangular dimensions and cancellation. `Verify Water Motion` renders two fixed-camera frames and checks their pixel difference in active Edit Mode or unpaused Play Mode. Keep Unity foreground while running the check so the Editor updates its rendering loop. `Capture Surface Preview` writes a close view to Temp for visual inspection. All three testing tools are editor-only and create temporary verification cameras; the user's camera controller is unchanged.

Generated albedos use the built-in image_gen tool. The prompt set is saved in `Textures/Surfaces/GenerationPrompts.md`.

## Matte terrain and stochastic texturing

`Materials/MatteTerrain.mat` is the source material used by TerrainGenerator. It uses `TacticalMaps/Terrain/MatteStochastic`, with custom additive and distant matte shaders. Lighting is diffuse-only: no specular, Fresnel or environment-reflection term, regardless of TerrainLayer smoothness or texture alpha. Detail normals, directional/additional lights, shadows, fog and ambient lighting remain.

Three hashed samples on a triangular world-space lattice randomize texture translation and quarter-turn orientation. Albedo and normal samples share transforms and weights; normal XY is rotated back into the original tangent frame. Explicit rotated texture gradients retain anisotropic mip filtering at grazing angles. Variance correction preserves fine contrast across blend regions. The lattice scale is independent of terrain dimensions, and each terrain material stores its physical size so nested levels retain the same lattice after saving/reopening.

The source material exposes Anti-tiling Strength, Patch Width (in texture tiles), Blend Sharpness, Pattern Seed, Anti-tiling Fade Start/End (metres), and Detail Normal Strength. Defaults fade stochastic sampling from 1200 to 3000 metres; beyond that a single ordinary sample per map is used. During the fade an additional regular sample allows a continuous transition. Unused layers are skipped where their splat weights are zero. These are sampling costs, not a guaranteed frame-rate result.

TerrainGenerator keeps geometric LOD (`Main Pixel Error = 1`). `Main Surface Distance` defaults to 12000 m, with a separate 6000 m background distance. This controls material basemaps, not mesh simplification. Surface imports use anisotropy 16 and ordinary mip bias 0. The distant basemap has its own diffuse-only shader so gloss cannot reappear there.

Each built terrain owns a material clone; source materials/textures remain shared assets. Rebuild/rollback releases transient clones. `Tools > Tactical Maps > Set Up Matte Terrain` updates the saved scene materials without regenerating heightmaps or water. Rebuild after editing source-material settings to update all three clones.

`Verify Matte Terrain` renders a solid glossy-input regression under high/low viewing angles, checks additive layers 5/6 and basemap visibility, and compares the tile-period difference with anti-tiling off/on. It also reports synchronized offscreen render/readback timings, which include CPU work and are not a pure GPU measurement. `Capture Matte Terrain Angles` writes top/grazing/distant images to Temp. Current Editor GPU frame timing was unavailable; profile target gameplay hardware before deciding the final quality distances.
