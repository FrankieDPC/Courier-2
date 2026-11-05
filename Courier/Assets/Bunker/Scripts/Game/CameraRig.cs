using Bunker.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Bunker.Game
{
    /// <summary>
    /// Orthographic, north-up camera that follows your robot. Right/middle-drag to look around (it
    /// drifts back to you when you move), scroll to zoom, C to recentre.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        const float Pitch = 60f;
        const float Distance = 30f; // keep inside URP's default 50m shadow distance
        const float MinSize = 3f;
        const float MaxSize = 14f;
        const float Follow = 8f;    // how quickly the camera catches up (per second)
        const float ReturnRate = 1.5f;

        GameController _game;
        Camera _cam;
        Vector3 _focus;
        Vector3 _lookOffset; // from dragging; fades out while you walk
        float _size = 7f;
        bool _panning;
        Cell _lastPlayerCell;

        public void Init(GameController game, Camera cam)
        {
            _game = game;
            _cam = cam;
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Palette.Background;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 200f;

            _focus = PlayerWorld();
            if (_game.World.Player != null) _lastPlayerCell = _game.World.Player.Position;
            _game.World.DayStarted += OnMorning;
            Apply();
        }

        void OnDestroy()
        {
            if (_game != null && _game.World != null) _game.World.DayStarted -= OnMorning;
        }

        /// <summary>Everyone was reset at the hatch overnight: jump there (it happens under the night fade).</summary>
        void OnMorning()
        {
            _lookOffset = Vector3.zero;
            _panning = false;
            _focus = PlayerWorld();
            if (_game.World.Player != null) _lastPlayerCell = _game.World.Player.Position;
        }

        /// <summary>Your robot's interpolated position (the hatch if there's no robot).</summary>
        Vector3 PlayerWorld()
        {
            var w = _game.World;
            var p = w.Player;
            if (p == null) return SurfaceView.CellToWorld(w.Map.Home);
            if (_game.Player != null) return _game.Player.WorldPosition; // you move freely, between tiles
            return SurfaceView.CellToWorld(p.Position);
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float worldPerPixel = 2f * _size / Mathf.Max(1, Screen.height);
            float zStretch = 1f / Mathf.Sin(Pitch * Mathf.Deg2Rad);

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
                    _lookOffset -= new Vector3(d.x, 0f, d.y * zStretch) * worldPerPixel;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(scroll) > 0.01f)
                    _size = Mathf.Clamp(_size * (scroll > 0f ? 0.9f : 1.1f), MinSize, MaxSize);
            }
            if (kb != null && !UIFactory.IsTyping() && kb.cKey.wasPressedThisFrame) _lookOffset = Vector3.zero;

            // Walking brings the view back to you (not while paused, when you're looking around to plan).
            var p = _game.World.Player;
            bool walking = p != null && !_game.Paused && !_game.InNight;
            if (p != null && p.Position != _lastPlayerCell)
            {
                _lastPlayerCell = p.Position;
                if (!_panning && walking) _lookOffset = Vector3.Lerp(_lookOffset, Vector3.zero, 0.35f);
            }
            if (!_panning && walking && _game.Player != null && _game.Player.IsWalking)
                _lookOffset = Vector3.MoveTowards(_lookOffset, Vector3.zero, _lookOffset.magnitude * ReturnRate * Time.unscaledDeltaTime);

            // Keep you in the middle of the open map, not under the side panels or the top bar.
            float uiScale = Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));
            float right = _game.IsEditorOpen ? GameUI.RightWidth : 0f; // the programming panel only shows while open
            float shiftX = (right - GameUI.LeftWidth) * 0.5f * uiScale * worldPerPixel;
            float shiftZ = GameUI.SidePanelTop * 0.5f * uiScale * worldPerPixel * zStretch;

            var map = _game.World.Map;
            var target = PlayerWorld() + _lookOffset + new Vector3(shiftX, 0f, shiftZ);
            var clamped = new Vector3(Mathf.Clamp(target.x, 0f, map.Width - 1), target.y, Mathf.Clamp(target.z, 0f, map.Height - 1));
            _lookOffset += clamped - target; // dragging past the edge doesn't build up a dead zone
            target = clamped;
            _focus = Vector3.Lerp(_focus, target, 1f - Mathf.Exp(-Follow * Time.unscaledDeltaTime));
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
