using Courier.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace Courier.Game
{
    /// <summary>
    /// Entry point. Put this on one GameObject in an otherwise empty scene (menu: Courier > Create
    /// Town Scene does it for you). It builds the town, camera, views and UI from code so there are
    /// no prefabs to wire up while the design is still moving.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Real seconds per simulation tick at 1x speed.")]
        [SerializeField] float secondsPerTick = 0.25f;

        void Awake()
        {
            var town = StarterTown.Create();

            var game = gameObject.AddComponent<GameController>();
            game.Init(town, secondsPerTick);

            EnsureEventSystem();
            SetupLighting();
            var cam = EnsureCamera();

            var view = new GameObject("Town View").AddComponent<TownView>();
            view.Init(game);

            cam.gameObject.AddComponent<CameraRig>().Init(game, cam);
            gameObject.AddComponent<WorldInput>().Init(game, cam);

            var ui = new GameObject("UI").AddComponent<GameUI>();
            ui.Init(game);

            if (town.Bots.Count > 0) game.SelectBot(town.Bots[0]);
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
            if (es == null)
            {
                es = new GameObject("EventSystem").AddComponent<EventSystem>();
            }

            // This project uses the Input System package only, so the legacy module would throw.
            var legacy = es.GetComponent<StandaloneInputModule>();
            if (legacy != null) Object.DestroyImmediate(legacy);
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        static void SetupLighting()
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
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.66f);
        }
    }
}
