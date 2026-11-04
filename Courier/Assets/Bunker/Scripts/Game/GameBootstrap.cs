using System.Collections;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace Bunker.Game
{
    /// <summary>
    /// Entry point. Put this on one GameObject in an otherwise empty scene (menu: Bunker > Create Bunker
    /// Scene does it for you). It builds the surface, camera, views and UI from code so there are no
    /// prefabs to wire up while the design is still moving. Also handles "Play again".
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Real seconds per simulation tick at 1x speed.")]
        [SerializeField] float secondsPerTick = 0.25f;

        GameController _game;
        GameObject _view;
        GameObject _ui;
        CameraRig _rig;
        WorldInput _input;
        PlayerController _player;
        DayLight _dayLight;
        PreviewController _preview;

        void Awake()
        {
            EnsureEventSystem();
            SetupLighting();
            Build();
        }

        void Build()
        {
            var world = StarterSurface.Create();

            _game = gameObject.AddComponent<GameController>();
            _game.Init(world, secondsPerTick);
            _game.RestartRequested += OnRestartRequested;

            _player = gameObject.AddComponent<PlayerController>();
            _player.Init(_game);
            _game.Player = _player;

            _preview = gameObject.AddComponent<PreviewController>();
            _preview.Init(_game);

            var cam = EnsureCamera();

            var view = new GameObject("Surface View").AddComponent<SurfaceView>();
            view.Init(_game);
            _view = view.gameObject;

            _rig = cam.gameObject.AddComponent<CameraRig>();
            _rig.Init(_game, cam);
            _input = gameObject.AddComponent<WorldInput>();
            _input.Init(_game, cam);

            var ui = new GameObject("UI").AddComponent<GameUI>();
            ui.Init(_game);
            _ui = ui.gameObject;

            _dayLight = gameObject.AddComponent<DayLight>();
            _dayLight.Init(_game, SetupLighting(), cam);

            _game.ShowToast("Morning, Pip. WASD to move, E to work the tile in front of you. The day runs 6 am to 2 am.");
        }

        void OnRestartRequested() => StartCoroutine(Rebuild());

        IEnumerator Rebuild()
        {
            _game.RestartRequested -= OnRestartRequested;
            Destroy(_ui);
            Destroy(_view);
            Destroy(_rig);
            Destroy(_input);
            Destroy(_player);
            Destroy(_dayLight);
            Destroy(_preview);
            Destroy(_game);
            yield return null; // let Unity finish destroying before rebuilding
            SetupLighting();
            Build();
        }

        static Camera EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            return cam;
        }

        static void EnsureEventSystem()
        {
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (es == null) es = new GameObject("EventSystem").AddComponent<EventSystem>();

            // This project uses the Input System package only, so the legacy module would throw.
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Object.DestroyImmediate(legacy);
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = es.gameObject.AddComponent<InputSystemUIInputModule>();

            // WASD/arrows steer your robot, so they mustn't also move the UI selection (or nudge a
            // slider that happens to be selected after a click).
            module.move = null;
        }

        public static Light SetupLighting()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // Soft, slightly hazy daylight over an overgrown world.
            sun.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.64f, 0.58f);

            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = Palette.Background;
            return sun;
        }
    }
}
