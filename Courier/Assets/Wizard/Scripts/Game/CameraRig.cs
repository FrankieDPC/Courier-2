using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wizard.Game
{
    /// <summary>
    /// Orthographic, north-up camera (so "turn left" on screen matches the bot's left).
    /// WASD/arrows or right/middle-drag to pan, scroll to zoom. Re-frames when the town expands.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        const float Pitch = 60f;
        const float Distance = 30f; // keep inside URP's default 50m shadow distance
        const float MinSize = 3f;
        const float MaxSize = 24f;
        const float KeyPanSpeed = 1.2f; // screen-heights per second

        GameController _game;
        Camera _cam;
        Vector3 _focus;
        float _size = 8f;
        bool _panning;

        public void Init(GameController game, Camera cam)
        {
            _game = game;
            _cam = cam;
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Palette.Background;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;

            _game.World.Grid.Expanded += FrameVillage;
            FrameVillage();
        }

        void OnDestroy()
        {
            if (_game != null) _game.World.Grid.Expanded -= FrameVillage;
        }

        public void FrameVillage()
        {
            var grid = _game.World.Grid;
            Vector3 min = WorldView.CellToWorld(grid.UnlockedMin);
            Vector3 max = WorldView.CellToWorld(grid.UnlockedMax);
            _focus = (min + max) * 0.5f;

            float dx = max.x - min.x + 2f;
            float dz = max.z - min.z + 2f;
            // Leave room for the side panels (~35% of the screen width).
            float usableAspect = Mathf.Max(0.5f, _cam.aspect * 0.62f);
            _size = Mathf.Clamp(Mathf.Max(dz * Mathf.Sin(Pitch * Mathf.Deg2Rad) * 0.5f, dx * 0.5f / usableAspect) + 0.5f, MinSize, MaxSize);
            Apply();
        }

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float worldPerPixel = 2f * _size / Mathf.Max(1, Screen.height);
            float zStretch = 1f / Mathf.Sin(Pitch * Mathf.Deg2Rad);

            if (kb != null && !UIFactory.IsTyping())
            {
                var dir = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1f;
                float speed = KeyPanSpeed * _size * Time.unscaledDeltaTime;
                _focus += new Vector3(dir.x, 0f, dir.y * zStretch) * speed;
            }

            if (mouse != null)
            {
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

                // Only pan when the drag started over the map, not over a panel.
                if ((mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame) && !overUI)
                    _panning = true;
                if (!mouse.rightButton.isPressed && !mouse.middleButton.isPressed)
                    _panning = false;
                if (_panning)
                {
                    var d = mouse.delta.ReadValue();
                    _focus -= new Vector3(d.x, 0f, d.y * zStretch) * worldPerPixel;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(scroll) > 0.01f)
                    _size = Mathf.Clamp(_size * (scroll > 0f ? 0.9f : 1.1f), MinSize, MaxSize);
            }

            var grid = _game.World.Grid;
            _focus.x = Mathf.Clamp(_focus.x, 0f, grid.Width - 1);
            _focus.z = Mathf.Clamp(_focus.z, 0f, grid.Height - 1);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            transform.SetPositionAndRotation(_focus - rot * Vector3.forward * Distance, rot);
            _cam.orthographicSize = _size;
        }
    }
}
