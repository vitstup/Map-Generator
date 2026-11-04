using UnityEngine;

namespace TacticalMaps.Testing
{
    internal sealed class AgricultureOverlay : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.RawImage overlay;
        [SerializeField] private UnityEngine.UI.Toggle visibility;
        private void OnEnable()
        {
            if (visibility != null) { visibility.onValueChanged.AddListener(Show); Show(visibility.isOn); }
        }
        private void OnDisable() { if (visibility != null) visibility.onValueChanged.RemoveListener(Show); }
        private void Show(bool value) { if (overlay != null) overlay.enabled = value; }
    }
}
