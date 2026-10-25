using UnityEngine;

namespace TacticalMaps.Grass
{
    // Built-map-owned resources. Serialized references survive saved previews and domain reloads.
    [ExecuteAlways, DisallowMultipleComponent]
    internal sealed class GrassMap : MonoBehaviour
    {
        [SerializeField, HideInInspector] private GrassSystem owner;
        [SerializeField, HideInInspector] private Terrain[] terrains;
        [SerializeField, HideInInspector] private Texture2D[] coverage;
        [SerializeField, HideInInspector] private float seaLevel;
        private readonly bool[] privateMasks = new bool[3];
        internal Terrain[] Terrains => terrains;
        internal Texture2D[] Coverage => coverage;
        internal float SeaLevel => seaLevel;
        internal void Configure(GrassSystem system, Terrain[] ground, Texture2D[] masks, float sea)
        {
            var previous = coverage;
            if (owner != null) owner.Detach(this);
            owner = system; terrains = ground; coverage = masks; seaLevel = sea;
            System.Array.Clear(privateMasks, 0, privateMasks.Length);
            if (isActiveAndEnabled) owner.Attach(this);
            if (previous != null) foreach (var texture in previous) Dispose(texture);
        }
        internal void Exclude(Bounds area)
        {
            for (int level = 0; level < terrains.Length; level++)
            {
                var p = terrains[level].transform.position; var size = terrains[level].terrainData.size;
                if (area.max.x < p.x || area.min.x > p.x + size.x || area.max.z < p.z || area.min.z > p.z + size.z) continue;
                var texture = coverage[level];
                // Saved preview assets are shared; interactive exclusions use a private snapshot.
                if (!privateMasks[level])
                {
                    var original=texture; texture=Instantiate(original); coverage[level]=texture;
                    privateMasks[level]=true; Dispose(original);
                }
                int x0 = Mathf.Clamp(Mathf.FloorToInt((area.min.x-p.x)/size.x*texture.width),0,texture.width-1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((area.min.z-p.z)/size.z*texture.height),0,texture.height-1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((area.max.x-p.x)/size.x*texture.width),0,texture.width-1);
                int z1 = Mathf.Clamp(Mathf.CeilToInt((area.max.z-p.z)/size.z*texture.height),0,texture.height-1);
                var pixels = texture.GetPixels32();
                for (int z=z0;z<=z1;z++) for (int x=x0;x<=x1;x++) pixels[z*texture.width+x].a=0;
                texture.SetPixels32(pixels); texture.Apply(false,false);
            }
        }
        private void OnEnable() { if (owner != null) owner.Attach(this); }
        private void OnDisable() { if (owner != null) owner.Detach(this); }
        private void OnDestroy() { if (coverage != null) foreach (var texture in coverage) Dispose(texture); }
        internal static void Dispose(Object resource)
        {
            if (resource == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(resource)) return;
#endif
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
