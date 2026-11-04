using System.Collections;
using Farm.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace Farm.Game
{
    /// <summary>
    /// Entry point. Put this on one GameObject in an otherwise empty scene (menu: Farm > Create Farm
    /// Scene does it for you). It builds the farm, camera, views and UI from code so there are no
    /// prefabs to wire up while the design is still moving. Also handles "Play again".
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Real seconds per simulation tick at 1x speed.")]
        [SerializeField] float secondsPerTick = 0.2f;

        GameController _game;
        GameObject _view;
        GameObject _ui;
        CameraRig _rig;
        WorldInput _input;
        PreviewController _preview;

        void Awake()
        {
            EnsureEventSystem();
            SetupLighting();
            Build();
        }

        void Build()
        {
            var world = StarterFarm.Create();

            _game = gameObject.AddComponent<GameController>();
            _game.Init(world, secondsPerTick);
            _game.RestartRequested += OnRestartRequested;

            _preview = gameObject.AddComponent<PreviewController>();
            _preview.Init(_game);

            var cam = EnsureCamera();

            var view = new GameObject("Farm View").AddComponent<FarmView>();
            view.Init(_game);
            _view = view.gameObject;

            _rig = cam.gameObject.AddComponent<CameraRig>();
            _rig.Init(_game, cam);
            _input = gameObject.AddComponent<WorldInput>();
            _input.Init(_game, cam);

            var ui = new GameObject("UI").AddComponent<GameUI>();
            ui.Init(_game);
            _ui = ui.gameObject;

            if (world.Bots.Count > 0) _game.SelectBot(world.Bots[0]);
        }

        void OnRestartRequested() => StartCoroutine(Rebuild());

        IEnumerator Rebuild()
        {
            _game.RestartRequested -= OnRestartRequested;
            Destroy(_ui);
            Destroy(_view);
            Destroy(_rig);
            Destroy(_input);
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
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        public static void SetupLighting()
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
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.5f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.66f, 0.64f, 0.6f);

            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = Palette.Background;
        }
    }
}
