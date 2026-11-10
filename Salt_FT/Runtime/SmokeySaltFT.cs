using UnityEngine;
using VRC.SDKBase;

namespace Smokey.SaltFT
{
    /// <summary>
    /// Marker for "Smokey's Salt Face Tracking". Add the prefab to your avatar. When you upload or enter
    /// Play mode, the face tracking shapes are added to a temporary copy of the head mesh.
    /// Nothing in your project (model, mesh, avatar) is changed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Smokey/Salt Face Tracking")]
    public class SmokeySaltFT : MonoBehaviour, IEditorOnly
    {
        [Tooltip("Data file with the face tracking shapes. Already set on the prefab.")]
        public TextAsset patch;
    }
}
