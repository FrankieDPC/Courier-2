using Wizard.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wizard.Game
{
    /// <summary>
    /// Mouse and keyboard on the map. Reads devices directly (Input System) to stay asset-free.
    ///   Left click   select construct / building, or place in build mode
    ///   Right click  cancel build mode / deselect   (right-drag pans, see CameraRig)
    ///   Space        start day / pause
    ///   Tab          next construct
    ///   F            cycle speed
    ///   P            preview the selected construct's day
    ///   Esc          cancel
    /// </summary>
    [DefaultExecutionOrder(100)] // after the EventSystem, so UI hover/focus is up to date
    public sealed class WorldInput : MonoBehaviour
    {
        const float ClickSlopPixels = 6f;

        GameController _game;
        Camera _cam;
        Vector2 _rightDownAt;
        bool _wasTyping;
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
            bool typing = UIFactory.IsTyping();
            bool suppress = typing || _wasTyping; // Esc/Enter that closed a text field shouldn't also hit the map
            _wasTyping = typing;
            if (kb == null || suppress) return;

            if (kb.spaceKey.wasPressedThisFrame) _game.PrimaryAction();
            if (kb.tabKey.wasPressedThisFrame) _game.SelectNextConstruct();
            if (kb.fKey.wasPressedThisFrame) _game.CycleSpeed();
            if (kb.pKey.wasPressedThisFrame && _game.Preview != null) _game.Preview.Toggle();
            if (kb.escapeKey.wasPressedThisFrame) _game.Cancel();
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var screen = mouse.position.ReadValue();
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            var cell = default(Cell);
            bool hasCell = !overUI && WorldView.ScreenToCell(_cam, screen, _game.World.Grid, out cell);
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
