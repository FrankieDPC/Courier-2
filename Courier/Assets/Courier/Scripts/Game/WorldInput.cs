using Courier.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Courier.Game
{
    /// <summary>
    /// Mouse and keyboard on the map. Reads devices directly (Input System) to stay asset-free.
    ///   Left click   select bot / building, or place in build mode
    ///   Right click  cancel build mode / deselect   (right-drag pans, see CameraRig)
    ///   Space        start day / pause
    ///   Tab          next courier
    ///   F            cycle speed
    ///   Esc          cancel
    /// </summary>
    public sealed class WorldInput : MonoBehaviour
    {
        const float ClickSlopPixels = 6f;

        GameController _game;
        Camera _cam;
        Vector2 _rightDownAt;
        bool _rightDownOverUI;

        public void Init(GameController game, Camera cam)
        {
            _game = game;
            _cam = cam;
        }

        void Update()
        {
            if (_game == null) return;
            HandleKeyboard();
            HandleMouse();
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) _game.PrimaryAction();
            if (kb.tabKey.wasPressedThisFrame) _game.SelectNextBot();
            if (kb.fKey.wasPressedThisFrame) _game.CycleSpeed();
            if (kb.escapeKey.wasPressedThisFrame) _game.Cancel();
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var screen = mouse.position.ReadValue();
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            var cell = default(Cell);
            bool hasCell = !overUI && TownView.ScreenToCell(_cam, screen, _game.Town.Grid, out cell);
            _game.SetHover(hasCell, cell);

            if (hasCell && mouse.leftButton.wasPressedThisFrame) _game.ClickCell(cell);

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _rightDownAt = screen;
                _rightDownOverUI = overUI;
            }
            if (mouse.rightButton.wasReleasedThisFrame && !_rightDownOverUI &&
                (screen - _rightDownAt).sqrMagnitude < ClickSlopPixels * ClickSlopPixels)
                _game.Cancel();
        }
    }
}
