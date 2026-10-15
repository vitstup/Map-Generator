using System;
using System.IO;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;

namespace TacticalMaps.Editor
{
    internal static class MapWorkbenchBuilder
    {
        private const string Root = "Assets/MapGenerator/Generated";
        private static TMP_FontAsset font;
        private static readonly Color Ink = new Color(0.87f, 0.91f, 0.94f);
        private static readonly Color Muted = new Color(0.48f, 0.59f, 0.64f);

        [MenuItem("Tools/Tactical Maps/Create MainScene Workbench")]
        private static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/MainScene.unity")
                throw new InvalidOperationException("Open Assets/Scenes/MainScene.unity before building.");
            if (UnityEngine.Object.FindAnyObjectByType<MapWorkbench>() != null)
                throw new InvalidOperationException("Workbench already exists; it will not be overwritten.");
            Directory.CreateDirectory(Root);
            PrepareFont();
            var heights = ImportSource("Assets/Sprites/Maps/japan_heightmap_4096_16bit.png", TextureImporterFormat.R16);
            var land = ImportSource("Assets/Sprites/Maps/japan_square_land_water_4096.png", TextureImporterFormat.R8);
            var canvasGO = new GameObject("MapGeneratorCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create map workbench");
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var w = canvasGO.AddComponent<MapWorkbench>();
            var generator = new GameObject("MapGenerator").AddComponent<MapGenerator>();
            Undo.RegisterCreatedObjectUndo(generator.gameObject, "Create map generator");
            SetReference(generator, "heightSource", heights); SetReference(generator, "landSource", land);
            SetReference(w, "generator", generator);
            UnityEngine.UI.RawImage globalOverlay;
            UnityEngine.UI.RawImage cropImage;
            UnityEngine.UI.RawImage[] results;
            TMP_Text status;
            TMP_Text selectionLabel;
            TMP_Text[] resultLabels;
            TMP_InputField radiusInput;
            TMP_InputField xInput;
            TMP_InputField yInput;
            TMP_InputField heightInput;
            TMP_InputField coastInput;
            TMP_InputField smoothingInput;
            TMP_InputField slopeInput;
            TMP_InputField transitionInput;
            TMP_InputField[] levelInputs;
            UnityEngine.UI.Button generateButton;
            UnityEngine.UI.Slider overlaySlider;
            UnityEngine.UI.Slider exposureSlider;
            MapSelectionGraphic globalSelection;
            MapSelectionGraphic cropSelection;
            Material previewMaterial;
            var parent = canvasGO.transform;
            Panel(parent, "Background", 0, 0, 1920, 1080, new Color(0.035f, 0.055f, 0.075f));
            Label(parent, "ТАКТИЧЕСКАЯ КАРТА", 30, 25, 1000, 44, 32, Ink);
            Label(parent, "Выбор местности / три уровня высот", 32, 77, 1000, 28, 18, Muted);
            Label(parent, "01 / ИСХОДНАЯ КАРТА", 30, 118, 640, 24, 17, Ink);
            var global = Rect(parent, "GlobalMap", 30, 155, 620, 620);
            Raw(global, "Land", MakeOverview(land), null, Color.white);
            var globalMat = new Material(Shader.Find("TacticalMaps/HeightPreview")) { name = "GlobalHeightOverlay" };
            globalMat.SetFloat("_Exposure", 2.5f); AssetDatabase.CreateAsset(globalMat, Root + "/GlobalOverlay.mat");
            globalOverlay = Raw(global, "HeightOverlay", heights, globalMat, new Color(1, 1, 1, 0.6f));
            globalSelection = Full(global, "Selection").gameObject.AddComponent<MapSelectionGraphic>();
            SetReference(globalSelection, "workbench", w); globalSelection.raycastTarget = true;
            Label(parent, "Наложение высот", 30, 796, 250, 25, 18, Muted);
            overlaySlider = Slider(parent, "OverlayOpacity", 280, 798, 365, 24, 0, 1, 0.6f);
            xInput = Field(parent, "Центр X", 30, 850, 195);
            yInput = Field(parent, "Центр Y (снизу)", 245, 850, 195);
            radiusInput = Field(parent, "Радиус, px", 460, 850, 190);
            Label(parent, "Нажмите или перетащите точку на карте.\nРадиус 50 = квадрат 101 × 101 исходных пикселей.\nЗа границами исходной карты — вода.", 30, 940, 620, 90, 18, Muted);

            Label(parent, "02 / ВЫБРАННЫЕ ПИКСЕЛИ", 700, 118, 370, 24, 17, Ink);
            var crop = Rect(parent, "SourceCrop", 700, 155, 330, 330);
            cropImage = Raw(crop, "Pixels", null, null, Color.white);
            cropSelection = Full(crop, "PixelGridAndFootprints").gameObject.AddComponent<MapSelectionGraphic>();
            SetReference(cropSelection, "workbench", w);
            var cropSerialized = new SerializedObject(cropSelection); cropSerialized.FindProperty("crop").boolValue = true; cropSerialized.ApplyModifiedPropertiesWithoutUndo();
            cropSelection.raycastTarget = false;
            selectionLabel = Label(parent, "", 700, 497, 365, 68, 16, Muted);

            Label(parent, "ПАРАМЕТРЫ ГЕНЕРАЦИИ", 1090, 118, 790, 24, 17, Ink);
            heightInput = Field(parent, "Шкала высот, м", 1090, 155, 180);
            coastInput = Field(parent, "Берег, исх. px", 1290, 155, 180);
            smoothingInput = Field(parent, "Сглаживание, px", 1490, 155, 180);
            slopeInput = Field(parent, "Макс. склон, °", 1690, 155, 180);
            transitionInput = Field(parent, "Переход I → II, м", 1090, 237, 180);
            Label(parent, "Общий квадрат задаёт дальнюю область.\nРазмеры в метрах определяют вложенные участки.\nВсе три карты используют единую шкалу высот.", 1290, 241, 580, 70, 17, Muted);
            Label(parent, "УРОВЕНЬ", 1090, 330, 150, 22, 15, Muted);
            Label(parent, "ТЕКСТУРА W × H, px", 1270, 330, 270, 22, 15, Muted);
            Label(parent, "TERRAIN W × H, м", 1580, 330, 300, 22, 15, Muted);
            levelInputs = new TMP_InputField[12];
            string[] names = { "I   Основной", "II  Окружение", "III Горизонт" };
            for (int i = 0; i < 3; i++)
            {
                float y = 365 + i * 52;
                Label(parent, names[i], 1090, y, 175, 36, 18, i == 0 ? new Color(0.3f, 1, 0.65f) : i == 1 ? new Color(1, 0.74f, 0.3f) : new Color(0.3f, 0.7f, 1));
                levelInputs[i * 4] = Input(parent, "Level" + i + "TextureWidth", 1270, y, 118, 36);
                levelInputs[i * 4 + 1] = Input(parent, "Level" + i + "TextureHeight", 1400, y, 118, 36);
                levelInputs[i * 4 + 2] = Input(parent, "Level" + i + "WorldWidth", 1580, y, 137, 36);
                levelInputs[i * 4 + 3] = Input(parent, "Level" + i + "WorldHeight", 1730, y, 137, 36);
            }
            generateButton = Button(parent, "СГЕНЕРИРОВАТЬ", 1090, 530, 290, 42);
            Label(parent, "Яркость превью", 1400, 535, 170, 26, 17, Muted);
            exposureSlider = Slider(parent, "PreviewExposure", 1590, 538, 277, 24, 1, 32, 2);
            previewMaterial = new Material(Shader.Find("TacticalMaps/HeightPreview")) { name = "HeightmapPreview" };
            previewMaterial.SetFloat("_Exposure", 2); AssetDatabase.CreateAsset(previewMaterial, Root + "/HeightmapPreview.mat");
            results = new UnityEngine.UI.RawImage[3]; resultLabels = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float x = 700 + i * 395;
                Label(parent, "0" + (i + 3) + " / " + names[i], x, 605, 375, 26, 18, Ink);
                var holder = Rect(parent, "Heightmap" + (i + 1), x, 645, 375, 350);
                Panel(holder, "Frame", 0, 0, 375, 350, new Color(0.07f, 0.1f, 0.12f));
                var result = Raw(holder, "Preview", null, previewMaterial, Color.white);
                result.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>().aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
                results[i] = result;
                resultLabels[i] = Label(parent, "", x, 1005, 380, 26, 15, Muted);
            }
            status = Label(parent, "", 700, 1043, 1180, 24, 16, Ink);
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                Undo.RegisterCreatedObjectUndo(new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)), "Create event system");
            SetReference(w, "globalOverlay", globalOverlay);
            SetReference(w, "cropImage", cropImage);
            SetReferences(w, "results", results);
            SetReference(w, "status", status);
            SetReference(w, "selectionLabel", selectionLabel);
            SetReferences(w, "resultLabels", resultLabels);
            SetReference(w, "radiusInput", radiusInput);
            SetReference(w, "xInput", xInput);
            SetReference(w, "yInput", yInput);
            SetReference(w, "heightInput", heightInput);
            SetReference(w, "coastInput", coastInput);
            SetReference(w, "smoothingInput", smoothingInput);
            SetReference(w, "slopeInput", slopeInput);
            SetReference(w, "transitionInput", transitionInput);
            SetReferences(w, "levelInputs", levelInputs);
            SetReference(w, "generateButton", generateButton);
            SetReference(w, "overlaySlider", overlaySlider);
            SetReference(w, "exposureSlider", exposureSlider);
            SetReference(w, "globalSelection", globalSelection);
            SetReference(w, "cropSelection", cropSelection);
            SetReference(w, "previewMaterial", previewMaterial);
            w.SyncControls(); w.RefreshSelection();
            generator.Generate(); w.DisplayResults();
            for (int i = 0; i < 3; i++) AssetDatabase.CreateAsset(generator.GetHeightmap(i), Root + "/Heightmap" + (i + 1) + ".asset");
            AssetDatabase.CreateAsset((Texture2D)cropImage.texture, Root + "/SelectedPixels.asset");
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = canvasGO;
            File.WriteAllText("Temp/map-workbench-built.txt", "MainScene built successfully: Canvas, selection, 3 R16 textures; no Terrain objects.");
            Debug.Log("Tactical map workbench created in MainScene.");
        }

        private static Texture2D ImportSource(string path, TextureImporterFormat format)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.SingleChannel;
            var textureSettings = new TextureImporterSettings(); importer.ReadTextureSettings(textureSettings);
            textureSettings.singleChannelComponent = TextureImporterSingleChannelComponent.Red; importer.SetTextureSettings(textureSettings);
            importer.sRGBTexture = false; importer.isReadable = true; importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096; importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.filterMode = FilterMode.Bilinear;
            var platform = importer.GetDefaultPlatformTextureSettings(); platform.maxTextureSize = 4096;
            platform.format = format; platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform); importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void SetReferences(UnityEngine.Object target, string name, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target); var property = serialized.FindProperty(name);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static Texture2D MakeOverview(Texture2D land)
        {
            int n = 1024; var texture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "LandOverview", wrapMode = TextureWrapMode.Clamp };
            var raw = land.GetPixelData<byte>(0); var colors = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                colors[y * n + x] = raw[(y * land.height / n) * land.width + x * land.width / n] >= 128 ? new Color(0.29f, 0.42f, 0.31f) : new Color(0.025f, 0.1f, 0.16f);
            texture.SetPixels32(colors); texture.Apply(); AssetDatabase.CreateAsset(texture, Root + "/LandOverview.asset"); return texture;
        }
        private static void PrepareFont()
        {
            if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) TMP_PackageResourceImporter.ImportResources(true, false, false);
            const string fontPath = Root + "/Arial.ttf";
            if (!File.Exists(fontPath)) File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"), fontPath);
            AssetDatabase.ImportAsset(fontPath);
            font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(fontPath), 72, 8, GlyphRenderMode.SDFAA, 1024, 1024);
            font.name = "Workbench Cyrillic";
            string chars = new string(Enumerable.Range(32, 95).Concat(Enumerable.Range(0x400, 112)).Select(c => (char)c).ToArray()) + "×→°–…·";
            font.TryAddCharacters(chars, out string missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, Root + "/WorkbenchFont.asset");
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(font);
        }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(width, height); return r;
        }
        private static RectTransform Full(Transform parent, string name)
        {
            var r = Rect(parent, name, 0, 0, 0, 0); r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f); r.offsetMin = r.offsetMax = Vector2.zero; return r;
        }
        private static UnityEngine.UI.Image Panel(Transform p, string name, float x, float y, float width, float height, Color color)
        {
            var image = Rect(p, name, x, y, width, height).gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static TMP_Text Label(Transform p, string text, float x, float y, float width, float height, int size, Color color)
        {
            var label = Rect(p, "Label", x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size; label.color = color; label.raycastTarget = false;
            label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal; return label;
        }
        private static UnityEngine.UI.RawImage Raw(Transform p, string name, Texture texture, Material material, Color color)
        {
            var image = Full(p, name).gameObject.AddComponent<UnityEngine.UI.RawImage>(); image.texture = texture;
            image.material = material; image.color = color; image.raycastTarget = false; return image;
        }
        private static TMP_InputField Field(Transform p, string label, float x, float y, float width)
        {
            Label(p, label, x, y, width, 25, 16, Muted); return Input(p, label, x, y + 29, width, 36);
        }
        private static TMP_InputField Input(Transform p, string name, float x, float y, float width, float height)
        {
            var bg = Panel(p, name, x, y, width, height, new Color(0.09f, 0.135f, 0.17f)); bg.raycastTarget = true;
            var input = bg.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = bg;
            var viewport = Full(bg.transform, "TextViewport"); viewport.offsetMin = new Vector2(9, 2); viewport.offsetMax = new Vector2(-9, -2);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var text = Label(viewport, "", 0, 0, width - 18, height - 4, 19, Ink); text.alignment = TextAlignmentOptions.MidlineLeft;
            var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            input.textViewport = viewport; input.textComponent = text; input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine; input.characterLimit = 10; return input;
        }
        private static UnityEngine.UI.Button Button(Transform p, string text, float x, float y, float width, float height)
        {
            var bg = Panel(p, "Generate", x, y, width, height, new Color(0.14f, 0.42f, 0.33f)); bg.raycastTarget = true;
            var button = bg.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = bg;
            var label = Label(bg.transform, text, 0, 0, width, height, 19, Ink); label.alignment = TextAlignmentOptions.Center; return button;
        }
        private static UnityEngine.UI.Slider Slider(Transform p, string name, float x, float y, float width, float height, float min, float max, float value)
        {
            var r = Rect(p, name, x, y, width, height);
            Panel(r, "Track", 0, height / 2 - 2, width, 4, new Color(0.18f, 0.26f, 0.3f));
            var handleArea = Full(r, "HandleArea"); handleArea.offsetMin = new Vector2(7, 0); handleArea.offsetMax = new Vector2(-7, 0);
            var handle = Panel(handleArea, "Handle", 0, 0, 14, height, new Color(0.38f, 0.85f, 0.66f)); handle.raycastTarget = true;
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var slider = r.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.minValue = min; slider.maxValue = max; slider.value = value; return slider;
        }
    }
}
