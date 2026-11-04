using UnityEngine;

namespace Bunker.Game
{
    /// <summary>Keeps a world-space label facing the camera.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
