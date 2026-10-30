using UnityEngine;

namespace TacticalMaps.Generation
{
    internal static class TextureContentRevision
    {
        internal static Hash128 Get(Texture2D texture)
        {
#if UNITY_EDITOR
            return texture.imageContentsHash;
#else
            // imageContentsHash is editor-only. Runtime CPU uploads increment updateCount.
            // Callers also compare the source object; persistent road caches include their pixel fingerprint.
            return new Hash128(texture.updateCount, (uint)texture.width, (uint)texture.height, 0);
#endif
        }
    }
}
