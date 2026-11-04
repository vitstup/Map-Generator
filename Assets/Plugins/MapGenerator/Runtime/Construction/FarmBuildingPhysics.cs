using System;
using UnityEngine;

namespace TacticalMaps.Construction
{
    internal static class FarmBuildingPhysics
    {
        internal static int Level(Vector2 position, Vector2 main, Vector2 surroundings)
        {
            bool Inside(Vector2 size) => Mathf.Abs(position.x) <= size.x * .5f && Mathf.Abs(position.y) <= size.y * .5f;
            return Inside(main) ? 0 : Inside(surroundings) ? 1 : 2;
        }

        internal static void Configure(GameObject instance, GameObject prefab, int level, int layer, bool surroundings)
        {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer));
            bool enabled = level == 0 || (level == 1 && surroundings);
            // Remove the old single-box representation from previously saved previews.
            foreach (var collider in instance.GetComponents<Collider>())
            {
                collider.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
            }
            var root = instance.transform.Find("Colliders");
            if (!enabled)
            {
                if (root != null)
                {
                    root.gameObject.SetActive(false);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject); else UnityEngine.Object.DestroyImmediate(root.gameObject);
                }
                return;
            }
            if (root == null)
            {
                var source = prefab.transform.Find("Colliders");
                if (source == null) throw new InvalidOperationException("Bake compound colliders for " + prefab.name);
                root = UnityEngine.Object.Instantiate(source, instance.transform, false);
                root.name = "Colliders";
            }
            root.gameObject.SetActive(true);
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }
    }
}
