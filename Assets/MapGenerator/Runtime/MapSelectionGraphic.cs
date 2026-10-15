using UnityEngine;
using UnityEngine.EventSystems;

namespace TacticalMaps
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MapSelectionGraphic : UnityEngine.UI.MaskableGraphic, IPointerDownHandler, IDragHandler
    {
        public MapWorkbench workbench;
        public bool crop;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            if (workbench == null || workbench.heightSource == null) return;
            var s = workbench.settings;
            Rect r = rectTransform.rect;
            if (crop)
            {
                int side = 2 * s.sourceRadius + 1;
                // Avoid a solid grid when the selected pixels are too small to distinguish.
                if (side <= 151)
                    for (int i = 0; i <= side; i++)
                    {
                        float t = (float)i / side;
                        Line(vh, new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMin), new Vector2(Mathf.Lerp(r.xMin, r.xMax, t), r.yMax), 0.45f, new Color(1, 1, 1, 0.14f));
                        Line(vh, new Vector2(r.xMin, Mathf.Lerp(r.yMin, r.yMax, t)), new Vector2(r.xMax, Mathf.Lerp(r.yMin, r.yMax, t)), 0.45f, new Color(1, 1, 1, 0.14f));
                    }
                for (int i = 0; i < 3; i++)
                {
                    Vector2 ratio = s.levels[i].size / s.levels[2].size;
                    Box(vh, new Rect(-r.width * ratio.x / 2, -r.height * ratio.y / 2, r.width * ratio.x, r.height * ratio.y), i == 0 ? new Color(0.3f, 1, 0.65f) : i == 1 ? new Color(1, 0.74f, 0.3f) : new Color(0.3f, 0.7f, 1), 1.5f);
                }
            }
            else
            {
                float w = workbench.heightSource.width, h = workbench.heightSource.height;
                var center = new Vector2(r.xMin + (s.center.x + 0.5f) / w * r.width, r.yMin + (s.center.y + 0.5f) / h * r.height);
                float side = s.sourceRadius * 2 + 1;
                Box(vh, new Rect(center.x - side / w * r.width / 2, center.y - side / h * r.height / 2, side / w * r.width, side / h * r.height), new Color(0.3f, 1, 0.65f), 1.8f);
                Line(vh, center - Vector2.right * 7, center + Vector2.right * 7, 1, Color.white);
                Line(vh, center - Vector2.up * 7, center + Vector2.up * 7, 1, Color.white);
            }
        }
        static void Box(UnityEngine.UI.VertexHelper vh, Rect r, Color c, float thickness)
        {
            Vector2 a = new Vector2(r.xMin, r.yMin), b = new Vector2(r.xMax, r.yMin), d = new Vector2(r.xMin, r.yMax), e = new Vector2(r.xMax, r.yMax);
            Line(vh, a, b, thickness, c); Line(vh, b, e, thickness, c); Line(vh, e, d, thickness, c); Line(vh, d, a, thickness, c);
        }
        static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width / 2;
            int index = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero); vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero); vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
        public void OnPointerDown(PointerEventData e) => Select(e);
        public void OnDrag(PointerEventData e) => Select(e);
        void Select(PointerEventData e)
        {
            if (crop || workbench == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out var p)) return;
            Rect r = rectTransform.rect;
            workbench.SelectPixel(Mathf.FloorToInt((p.x - r.xMin) / r.width * workbench.heightSource.width), Mathf.FloorToInt((p.y - r.yMin) / r.height * workbench.heightSource.height));
        }
    }
}
