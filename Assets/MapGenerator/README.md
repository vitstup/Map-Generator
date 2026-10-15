# Tactical map heightmap workbench

Open `Assets/Scenes/MainScene.unity`, select the Game tab and enter Play mode.
Click or drag on the large map. The selected source pixels and all three heightmaps update automatically.
`СГЕНЕРИРОВАТЬ` also regenerates the images after editing parameters.

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

Water overrides source elevations to zero. A distance-based coastal ramp fades land toward water. The main rectangle uses Gaussian smoothing and height compression sufficient to bound its bilinear surface slope. Outside that rectangle, a smooth transition returns to the source relief before reaching the middle rectangle edge. All levels evaluate the same world-space height function, so coincident coordinates agree; coarser textures can still introduce interpolation differences when actual Terrain objects are added later.

Preview brightness affects only rendering, not stored heights. The crop grid uses point filtering; green/orange/blue rectangles identify the three footprints. Dense grids are hidden above 151 samples per side to keep the preview readable.

Runtime regeneration runs on a background task. Changing the selection cancels the previous result, waits for a short drag debounce and generates the latest selection. Unity textures are created on the main thread. Play-mode parameter changes follow normal Unity behavior and reset on leaving Play mode.

## Files and checks

`Generated/Heightmap1.asset` through `Heightmap3.asset` contain the saved initial previews. Runtime results are available through `MapWorkbench.generated` and the Canvas RawImages.

`Tools > Tactical Maps > Verify Height Generation` runs synthetic checks for water override, rectangular output, shared coordinates, main-zone slope, transition continuity, source borders and cancellation. `MapGeneratorChecks.cs` contains these checks.

`Tools > Tactical Maps > Create MainScene Workbench` builds the interface in an open MainScene that has no workbench yet. It refuses to overwrite an existing workbench. Scene creation uses Unity Editor APIs. There are no Terrain objects in this stage.

Unity Pipeline is included to allow editor automation; TMP Essentials and a local Arial font asset provide the Russian interface.
