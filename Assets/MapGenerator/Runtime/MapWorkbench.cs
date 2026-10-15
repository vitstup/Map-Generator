using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace TacticalMaps
{
    public sealed class MapWorkbench : MonoBehaviour
    {
        public Texture2D heightSource, landSource;
        public MapSettings settings = new MapSettings();
        public UnityEngine.UI.RawImage globalOverlay, cropImage;
        public UnityEngine.UI.RawImage[] results;
        public TMP_Text status, selectionLabel;
        public TMP_Text[] resultLabels;
        public TMP_InputField radiusInput, xInput, yInput, heightInput, coastInput, smoothingInput, slopeInput, transitionInput;
        // Per-level fields: texture width, texture height, terrain width, terrain length.
        public TMP_InputField[] levelInputs;
        public UnityEngine.UI.Button generateButton;
        public UnityEngine.UI.Slider overlaySlider, exposureSlider;
        public MapSelectionGraphic globalSelection, cropSelection;
        public Material previewMaterial;
        public Texture2D[] generated = new Texture2D[3];
        float[] sourceHeights;
        byte[] sourceLand;
        Texture2D cropTexture;
        CancellationTokenSource cancellation;
        Task<ushort[][]> job;
        MapSettings runningSettings;
        float compression;
        float queuedAt = -1;
        bool ownsResults;

        void Awake()
        {
            if (!Application.isPlaying) return;
            // A private material keeps display exposure separate from the source overlay.
            previewMaterial = new Material(previewMaterial);
            foreach (var image in results) image.material = previewMaterial;
            SyncControls();
            generateButton.onClick.AddListener(Generate);
            overlaySlider.onValueChanged.AddListener(v => globalOverlay.color = new Color(1, 1, 1, v));
            exposureSlider.onValueChanged.AddListener(v => previewMaterial.SetFloat("_Exposure", v));
            foreach (var input in new[] { radiusInput, xInput, yInput, heightInput, coastInput, smoothingInput, slopeInput, transitionInput })
                input.onEndEdit.AddListener(_ => QueueGeneration());
            foreach (var input in levelInputs) input.onEndEdit.AddListener(_ => QueueGeneration());
            LoadSources();
            RefreshSelection();
        }

        public void LoadSources()
        {
            if (sourceHeights != null) return;
            if (heightSource.format != TextureFormat.R16 || landSource.format != TextureFormat.R8)
                throw new InvalidOperationException("Source formats must be uncompressed linear R16 height and R8 land mask.");
            if (heightSource.width != landSource.width || heightSource.height != landSource.height)
                throw new InvalidOperationException("Height and land textures must have matching dimensions.");
            var raw = heightSource.GetPixelData<ushort>(0);
            sourceHeights = new float[raw.Length];
            for (int i = 0; i < raw.Length; i++) sourceHeights[i] = raw[i] / 65535f;
            sourceLand = landSource.GetPixelData<byte>(0).ToArray();
        }

        public void SyncControls()
        {
            radiusInput.SetTextWithoutNotify(settings.sourceRadius.ToString());
            xInput.SetTextWithoutNotify(settings.center.x.ToString()); yInput.SetTextWithoutNotify(settings.center.y.ToString());
            heightInput.SetTextWithoutNotify(F(settings.heightMeters)); coastInput.SetTextWithoutNotify(F(settings.coastPixels));
            smoothingInput.SetTextWithoutNotify(F(settings.smoothingPixels)); slopeInput.SetTextWithoutNotify(F(settings.maxSlope));
            transitionInput.SetTextWithoutNotify(F(settings.transitionMeters));
            for (int i = 0; i < 3; i++)
            {
                levelInputs[i * 4].SetTextWithoutNotify(settings.levels[i].resolution.x.ToString());
                levelInputs[i * 4 + 1].SetTextWithoutNotify(settings.levels[i].resolution.y.ToString());
                levelInputs[i * 4 + 2].SetTextWithoutNotify(F(settings.levels[i].size.x));
                levelInputs[i * 4 + 3].SetTextWithoutNotify(F(settings.levels[i].size.y));
            }
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static float Parse(TMP_InputField input) => float.Parse(input.text.Replace(',', '.'), CultureInfo.InvariantCulture);
        static int Integer(TMP_InputField input) => int.Parse(input.text, CultureInfo.InvariantCulture);
        MapSettings ReadControls()
        {
            var s = JsonUtility.FromJson<MapSettings>(JsonUtility.ToJson(settings));
            s.sourceRadius = Integer(radiusInput); s.center = new Vector2Int(Integer(xInput), Integer(yInput));
            s.heightMeters = Parse(heightInput); s.coastPixels = Parse(coastInput); s.smoothingPixels = Parse(smoothingInput);
            s.maxSlope = Parse(slopeInput); s.transitionMeters = Parse(transitionInput);
            for (int i = 0; i < 3; i++)
            {
                s.levels[i].resolution = new Vector2Int(Integer(levelInputs[i * 4]), Integer(levelInputs[i * 4 + 1]));
                s.levels[i].size = new Vector2(Parse(levelInputs[i * 4 + 2]), Parse(levelInputs[i * 4 + 3]));
            }
            s.Validate(heightSource.width, heightSource.height);
            if (s.center.x < 0 || s.center.y < 0 || s.center.x >= heightSource.width || s.center.y >= heightSource.height)
                throw new ArgumentException("Центр должен находиться внутри исходной карты.");
            return s;
        }

        public void SelectPixel(int x, int y)
        {
            xInput.SetTextWithoutNotify(Mathf.Clamp(x, 0, heightSource.width - 1).ToString());
            yInput.SetTextWithoutNotify(Mathf.Clamp(y, 0, heightSource.height - 1).ToString());
            QueueGeneration();
        }
        void QueueGeneration()
        {
            try
            {
                settings = ReadControls();
                cancellation?.Cancel();
                RefreshSelection(); queuedAt = Time.unscaledTime + 0.3f;
                status.text = "Параметры изменены. Ожидание генерации…";
            }
            catch (Exception e) { queuedAt = -1; status.text = "Проверьте параметры: " + e.Message; }
        }
        public void Generate()
        {
            try
            {
                var next = ReadControls();
                if (job != null && !job.IsCompleted) { cancellation.Cancel(); queuedAt = Time.unscaledTime; return; }
                settings = next; LoadSources(); RefreshSelection();
                runningSettings = JsonUtility.FromJson<MapSettings>(JsonUtility.ToJson(settings));
                cancellation?.Dispose(); cancellation = new CancellationTokenSource();
                var token = cancellation.Token;
                var snapshot = runningSettings;
                int sourceWidth = heightSource.width, sourceHeight = heightSource.height;
                job = Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var field = new HeightField(sourceHeights, sourceLand, sourceWidth, sourceHeight, snapshot);
                    compression = field.compression;
                    var output = new ushort[3][];
                    for (int i = 0; i < 3; i++) output[i] = field.Render(i, token);
                    return output;
                }, token);
                status.text = "Генерация трёх карт…";
                queuedAt = -1;
            }
            catch (Exception e) { status.text = "Ошибка: " + e.Message; }
        }

        void Update()
        {
            if (job != null && job.IsCompleted)
            {
                if (job.IsFaulted) { status.text = "Ошибка: " + job.Exception.GetBaseException().Message; Debug.LogException(job.Exception); }
                else if (!job.IsCanceled && queuedAt < 0) Apply(job.Result, runningSettings, compression);
                job = null;
            }
            if (queuedAt >= 0 && Time.unscaledTime >= queuedAt && job == null) Generate();
        }

        public void Apply(ushort[][] pixels, MapSettings s, float scale)
        {
            for (int i = 0; i < 3; i++)
            {
                if (ownsResults && generated[i] != null) Destroy(generated[i]);
                var l = s.levels[i];
                var texture = new Texture2D(l.resolution.x, l.resolution.y, TextureFormat.R16, false, true)
                { name = "Heightmap_" + (i + 1), wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                texture.SetPixelData(pixels[i], 0); texture.Apply(false, false);
                generated[i] = texture; results[i].texture = texture;
                results[i].GetComponent<UnityEngine.UI.AspectRatioFitter>().aspectRatio = (float)l.resolution.x / l.resolution.y;
                resultLabels[i].text = $"{l.resolution.x} × {l.resolution.y} px   /   {F(l.size.x)} × {F(l.size.y)} м";
            }
            ownsResults = true;
            status.text = $"Готово · R16 · общая шкала 0–{F(s.heightMeters)} м · масштаб высот основной зоны {F(scale)}";
        }

        public void RefreshSelection()
        {
            LoadSources();
            int n = settings.sourceRadius * 2 + 1;
            if (cropTexture != null) { if (Application.isPlaying) Destroy(cropTexture); else DestroyImmediate(cropTexture); }
            cropTexture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "SelectedSourcePixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int sx = settings.center.x + x - settings.sourceRadius, sy = settings.center.y + y - settings.sourceRadius;
                Color c = new Color(0.035f, 0.11f, 0.17f);
                if (sx >= 0 && sy >= 0 && sx < heightSource.width && sy < heightSource.height)
                {
                    int p = sy * heightSource.width + sx;
                    if (sourceLand[p] >= 128) c = Color.Lerp(new Color(0.22f, 0.36f, 0.26f), new Color(0.92f, 0.93f, 0.82f), Mathf.Sqrt(sourceHeights[p]));
                }
                pixels[y * n + x] = c;
            }
            cropTexture.SetPixels32(pixels); cropTexture.Apply(); cropImage.texture = cropTexture;
            selectionLabel.text = $"Пиксель {settings.center.x}, {settings.center.y}  ·  выборка {n} × {n}\nЗелёный — основной · оранжевый — средний · голубой — дальний";
            globalSelection.SetVerticesDirty(); cropSelection.SetVerticesDirty();
        }

        void OnDestroy()
        {
            cancellation?.Cancel(); cancellation?.Dispose();
            if (Application.isPlaying)
            {
                if (ownsResults) foreach (var t in generated) if (t != null) Destroy(t);
                if (cropTexture != null) Destroy(cropTexture);
                if (previewMaterial != null) Destroy(previewMaterial);
            }
        }
    }
}
