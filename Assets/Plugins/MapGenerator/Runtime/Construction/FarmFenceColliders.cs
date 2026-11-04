using System;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalMaps.Construction
{
    // A run spans contiguous mesh sections, not material batches or individual fence posts.
    internal sealed class FarmFenceColliders
    {
        private readonly Transform root;
        private readonly Vector2 mainSize, surroundingsSize;
        private readonly int layer;
        private readonly bool surroundings;
        private readonly Dictionary<GameObject, Bounds> profiles = new Dictionary<GameObject, Bounds>();
        private readonly List<Vector3> points = new List<Vector3>();
        private GameObject currentPrefab;
        private Vector3 origin, lastFar;
        private Quaternion rotation;
        private int count;

        internal FarmFenceColliders(Transform agriculture, Vector2 main, Vector2 around, int physicsLayer, bool surroundingPhysics)
        {
            if (physicsLayer < 0 || physicsLayer > 31) throw new ArgumentOutOfRangeException(nameof(physicsLayer));
            var previous = agriculture.Find("FenceColliders");
            if (previous != null)
            {
                previous.name = "FenceColliders_Removing";
                previous.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(previous.gameObject); else UnityEngine.Object.DestroyImmediate(previous.gameObject);
            }
            root = new GameObject("FenceColliders").transform; root.SetParent(agriculture, false);
            root.gameObject.layer = physicsLayer;
            mainSize = main; surroundingsSize = around; layer = physicsLayer; surroundings = surroundingPhysics;
        }

        internal void Add(FarmFenceBuilder.Placement placement)
        {
            // Cut physical lines at zone boundaries, even when a rendered section crosses them.
            var cuts = new List<float> { 0, 1 };
            Cuts(mainSize); Cuts(surroundingsSize); cuts.Sort();
            for (int i = 1; i < cuts.Count; i++)
            {
                float start = cuts[i - 1], end = cuts[i];
                if (end - start < .00001f) continue;
                int level = FarmBuildingPhysics.Level(Vector2.Lerp(placement.LocalNear, placement.LocalFar, (start + end) * .5f), mainSize, surroundingsSize);
                if (level == 2 || (level == 1 && !surroundings)) { Flush(); continue; }
                var near = Vector3.Lerp(placement.Near, placement.Far, start);
                var far = Vector3.Lerp(placement.Near, placement.Far, end);
                var inverse = Quaternion.Inverse(rotation);
                var a = inverse * (near - origin); var b = inverse * (far - origin);
                // Allow small terrain noise, but split genuine bends instead of growing a tall box over empty air.
                if (points.Count > 0 && (currentPrefab != placement.Prefab || (near - lastFar).sqrMagnitude > .001f ||
                    Mathf.Abs(a.y) > .12f || Mathf.Abs(b.y) > .12f || Mathf.Abs(a.z) > .02f || Mathf.Abs(b.z) > .02f)) Flush();
                if (points.Count == 0) { currentPrefab = placement.Prefab; origin = near; rotation = placement.Matrix.rotation; }
                if (!profiles.TryGetValue(placement.Prefab, out var bounds)) profiles.Add(placement.Prefab, bounds = Profile(placement.Prefab));
                float section = placement.Prefab.GetComponent<FenceSection>().length;
                float length = Vector2.Distance(placement.LocalNear, placement.LocalFar) * (end - start);
                var matrix = Matrix4x4.TRS((near + far) * .5f, placement.Matrix.rotation, new Vector3(length / section, 1, 1));
                for (int corner = 0; corner < 8; corner++) points.Add(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
                lastFar = far;
            }

            void Cuts(Vector2 size)
            {
                var d = placement.LocalFar - placement.LocalNear;
                for (int axis = 0; axis < 2; axis++)
                {
                    if (Mathf.Abs(d[axis]) < .000001f) continue;
                    foreach (int sign in new[] { -1, 1 })
                    {
                        float t = (size[axis] * .5f * sign - placement.LocalNear[axis]) / d[axis];
                        if (t > 0 && t < 1) cuts.Add(t);
                    }
                }
            }
        }

        internal void Flush()
        {
            if (points.Count == 0) return;
            var inverse = Quaternion.Inverse(rotation);
            var bounds = new Bounds(inverse * (points[0] - origin), Vector3.zero);
            foreach (var point in points) bounds.Encapsulate(inverse * (point - origin));
            var go = new GameObject("FenceLine_" + count++, typeof(BoxCollider)) { layer = layer };
            go.transform.SetParent(root, false);
            var relative = root.worldToLocalMatrix * Matrix4x4.TRS(origin + rotation * bounds.center, rotation, Vector3.one);
            go.transform.localPosition = relative.GetColumn(3); go.transform.localRotation = relative.rotation; go.transform.localScale = relative.lossyScale;
            go.GetComponent<BoxCollider>().size = Vector3.Max(bounds.size, Vector3.one * .02f);
            points.Clear(); currentPrefab = null;
        }

        internal static Bounds Profile(GameObject prefab)
        {
            Bounds? result = null;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (result.HasValue) { var b = result.Value; b.Encapsulate(point); result = b; } else result = new Bounds(point, Vector3.zero);
                }
            }
            return result ?? throw new InvalidOperationException("Fence prefab has no mesh: " + prefab.name);
        }
    }
}
