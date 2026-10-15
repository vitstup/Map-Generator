using TacticalMaps.Generation;
using TacticalMaps.Construction;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace TacticalMaps.Testing
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom("TacticalMaps")]
    internal sealed class MapWorkbench : MonoBehaviour
    {
        [SerializeField] private MapGenerator generator;
        [SerializeField] private TerrainBuilder terrainBuilder;
        [SerializeField] private UnityEngine.UI.Button buildTerrainButton;
        [SerializeField] private UnityEngine.UI.RawImage globalOverlay, cropImage;
        [SerializeField] private UnityEngine.UI.RawImage[] results;
        [SerializeField] private TMP_Text status, selectionLabel;
        [SerializeField] private TMP_Text[] resultLabels;
        [SerializeField] private TMP_InputField radiusInput, xInput, yInput, heightInput, globalHeightInput, seaLevelInput, coastInput, smoothingInput, slopeInput, transitionInput;
        [SerializeField] private TMP_InputField[] levelInputs;
        [SerializeField] private UnityEngine.UI.Button generateButton;
        [SerializeField] private UnityEngine.UI.Slider overlaySlider, exposureSlider;
        [SerializeField] private MapSelectionGraphic globalSelection, cropSelection;
        [SerializeField] private Material previewMaterial;
        private Texture2D cropTexture;
        private Material runtimePreviewMaterial;
        private CancellationTokenSource cancellation;
        private Task<MapData> job;
        private MapData currentMap;
        [SerializeField] private Vector2Int selectedPoint = new Vector2Int(2550, 2250);
        private float queuedAt = -1;
        private bool buildPending;
        private bool hasBuiltTerrain;

        internal MapGenerator Generator => generator;
        internal Vector2Int SelectedPoint => selectedPoint;

        private void Awake()
        {
            if (!Application.isPlaying) return;
            runtimePreviewMaterial = new Material(previewMaterial);
            foreach (var image in results) image.material = runtimePreviewMaterial;
            SyncControls();
            generateButton.onClick.AddListener(Generate);
            buildTerrainButton.onClick.AddListener(BuildTerrains);
            overlaySlider.onValueChanged.AddListener(v => globalOverlay.color = new Color(1, 1, 1, v));
            exposureSlider.onValueChanged.AddListener(v => runtimePreviewMaterial.SetFloat("_Exposure", v));
            foreach (var input in new[] { radiusInput, xInput, yInput, heightInput, globalHeightInput, seaLevelInput, coastInput, smoothingInput, slopeInput, transitionInput })
                input.onEndEdit.AddListener(_ => QueueGeneration());
            foreach (var input in levelInputs) input.onEndEdit.AddListener(_ => QueueGeneration());
            RefreshSelection();
            queuedAt = Time.unscaledTime;
        }

        internal void SyncControls()
        {
            var settings = generator.Settings;
            radiusInput.SetTextWithoutNotify(settings.SourceRadius.ToString());
            xInput.SetTextWithoutNotify(selectedPoint.x.ToString()); yInput.SetTextWithoutNotify(selectedPoint.y.ToString());
            globalHeightInput.SetTextWithoutNotify(F(settings.GlobalMaxHeightMeters)); seaLevelInput.SetTextWithoutNotify(F(settings.SeaLevelMeters));
            heightInput.SetTextWithoutNotify(F(settings.TerrainHeightMeters)); coastInput.SetTextWithoutNotify(F(settings.CoastPixels));
            smoothingInput.SetTextWithoutNotify(F(settings.SmoothingPixels)); slopeInput.SetTextWithoutNotify(F(settings.MaxSlope));
            transitionInput.SetTextWithoutNotify(F(settings.TransitionMeters));
            for (int i = 0; i < 3; i++)
            {
                levelInputs[i * 4].SetTextWithoutNotify(settings.Levels[i].Resolution.x.ToString());
                levelInputs[i * 4 + 1].SetTextWithoutNotify(settings.Levels[i].Resolution.y.ToString());
                levelInputs[i * 4 + 2].SetTextWithoutNotify(F(settings.Levels[i].Size.x));
                levelInputs[i * 4 + 3].SetTextWithoutNotify(F(settings.Levels[i].Size.y));
            }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static float Parse(TMP_InputField input) => float.Parse(input.text.Replace(',', '.'), CultureInfo.InvariantCulture);
        private static int Integer(TMP_InputField input) => int.Parse(input.text, CultureInfo.InvariantCulture);
        private MapSettings ReadControls()
        {
            var s = generator.Settings;
            s.SourceRadius = Integer(radiusInput);
            s.TerrainHeightMeters = Parse(heightInput); s.GlobalMaxHeightMeters = Parse(globalHeightInput); s.SeaLevelMeters = Parse(seaLevelInput); s.CoastPixels = Parse(coastInput); s.SmoothingPixels = Parse(smoothingInput);
            s.MaxSlope = Parse(slopeInput); s.TransitionMeters = Parse(transitionInput);
            for (int i = 0; i < 3; i++)
            {
                s.Levels[i].Resolution = new Vector2Int(Integer(levelInputs[i * 4]), Integer(levelInputs[i * 4 + 1]));
                s.Levels[i].Size = new Vector2(Parse(levelInputs[i * 4 + 2]), Parse(levelInputs[i * 4 + 3]));
            }
            return s;
        }

        internal void SelectPixel(int x, int y)
        {
            xInput.SetTextWithoutNotify(Mathf.Clamp(x, 0, generator.HeightSource.width - 1).ToString());
            yInput.SetTextWithoutNotify(Mathf.Clamp(y, 0, generator.HeightSource.height - 1).ToString());
            QueueGeneration();
        }
        private void QueueGeneration()
        {
            try
            {
                generator.Configure(ReadControls());
                selectedPoint = ReadPoint();
                cancellation?.Cancel();
                RefreshSelection(); queuedAt = Time.unscaledTime + 0.3f;
                status.text = "Параметры изменены. Ожидание генерации…";
            }
            catch (Exception e) { queuedAt = -1; status.text = "Проверьте параметры: " + e.Message; }
        }
        private void Generate()
        {
            try
            {
                generator.Configure(ReadControls());
                selectedPoint = ReadPoint();
                if (job != null && !job.IsCompleted) { cancellation.Cancel(); queuedAt = Time.unscaledTime; return; }
                RefreshSelection();
                cancellation?.Dispose(); cancellation = new CancellationTokenSource();
                job = generator.GenerateAsync(selectedPoint, cancellation.Token);
                status.text = "Генерация трёх карт…";
                queuedAt = -1;
            }
            catch (Exception e) { status.text = "Ошибка: " + e.Message; }
        }

        private void Update()
        {
            if (job != null && job.IsCompleted)
            {
                if (job.IsFaulted) { status.text = "Ошибка: " + job.Exception.GetBaseException().Message; Debug.LogException(job.Exception); }
                else if (!job.IsCanceled && queuedAt < 0)
                {
                    DisplayResults(job.Result);
                    if (buildPending || hasBuiltTerrain) BuildTerrains();
                }
                else if (!job.IsCanceled && !job.IsFaulted) job.Result.Release();
                job = null;
            }
            if (queuedAt >= 0 && Time.unscaledTime >= queuedAt && job == null) Generate();
        }

        private void BuildTerrains()
        {
            if ((job != null && !job.IsCompleted) || queuedAt >= 0)
            {
                buildPending = true;
                status.text = "Terrain будут построены после завершения генерации…";
                return;
            }
            try
            {
                if (currentMap == null) { buildPending = true; Generate(); return; }
                terrainBuilder.Build(currentMap);
                buildPending = false; hasBuiltTerrain = true;
                status.text = "Построены 3 Terrain · осмотр в Scene · коллайдер у основного уровня";
            }
            catch (Exception e) { buildPending = false; status.text = "Ошибка построения: " + e.Message; Debug.LogException(e); }
        }

        internal void DisplayResults(MapData map)
        {
            var previous = currentMap;
            currentMap = map;
            for (int i = 0; i < 3; i++)
            {
                var texture = map.Heightmaps[i]; var size = map.Sizes[i];
                results[i].texture = texture;
                results[i].GetComponent<UnityEngine.UI.AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
                resultLabels[i].text = $"{texture.width} × {texture.height} px   /   {F(size.x)} × {F(size.y)} м";
            }
            status.text = $"Готово · Terrain {F(map.TerrainHeightMeters)} м · море {F(map.SeaLevelMeters)} м · высоты ×{F(map.HeightCompression)}";
            if (previous != map) previous?.Release();
        }

        private Vector2Int ReadPoint()
        {
            var point = new Vector2Int(Integer(xInput), Integer(yInput));
            if (point.x < 0 || point.y < 0 || point.x >= generator.HeightSource.width || point.y >= generator.HeightSource.height)
                throw new ArgumentException("Точка должна находиться внутри исходной карты.");
            return point;
        }

        internal void RefreshSelection()
        {
            var settings = generator.Settings;
            int n = settings.SourceRadius * 2 + 1;
            ReleaseCrop();
            cropTexture = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "SelectedSourcePixels", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int sx = selectedPoint.x + x - settings.SourceRadius, sy = selectedPoint.y + y - settings.SourceRadius;
                Color color = new Color(0.035f, 0.11f, 0.17f);
                if (generator.TryReadSource(sx, sy, out float height, out bool land) && land)
                    color = Color.Lerp(new Color(0.22f, 0.36f, 0.26f), new Color(0.92f, 0.93f, 0.82f), Mathf.Sqrt(height));
                pixels[y * n + x] = color;
            }
            cropTexture.SetPixels32(pixels); cropTexture.Apply(); cropImage.texture = cropTexture;
            selectionLabel.text = $"Пиксель {selectedPoint.x}, {selectedPoint.y}  ·  выборка {n} × {n}\nЗелёный — основной · оранжевый — средний · голубой — дальний";
            globalSelection.SetVerticesDirty(); cropSelection.SetVerticesDirty();
        }

        private void ReleaseCrop()
        {
            if (cropTexture == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(cropTexture)) return;
#endif
            if (Application.isPlaying) Destroy(cropTexture); else DestroyImmediate(cropTexture);
        }
        private void OnDestroy()
        {
            cancellation?.Cancel(); cancellation?.Dispose();
            ReleaseCrop();
            if (job != null && job.Status == TaskStatus.RanToCompletion && job.Result != currentMap) job.Result.Release();
            currentMap?.Release();
            if (runtimePreviewMaterial != null) Destroy(runtimePreviewMaterial);
        }
    }
}
