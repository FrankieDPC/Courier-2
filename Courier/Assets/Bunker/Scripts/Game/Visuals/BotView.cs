using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// Placeholder robot: you have an antenna with a lamp; farmhands wear straw hats; water bots wear a water tank. Each has a stack of crates on its back
    /// showing how full its load is (coloured by what it's mostly carrying). Bobs while it works and
    /// shows "!" when its last command had a problem. Interpolates between ticks.
    /// </summary>
    public sealed class BotView : MonoBehaviour
    {
        const int LoadBlocks = 5;

        Bot _bot;
        GameController _game;
        Transform _body;
        GameObject[] _load;
        GameObject _selection;
        TextMesh _alert;

        Vector3 _from, _to;
        Quaternion _fromRot, _toRot;
        bool _bump;

        public Vector3 Offset { get; set; }

        public void Init(Bot b, GameController game)
        {
            _bot = b;
            _game = game;
            var color = Palette.Bot(b);

            _selection = Shapes.Box("Selection", transform, new Vector3(0f, 0.055f, 0f), new Vector3(0.78f, 0.02f, 0.78f), Palette.Selection, false); // above tilled soil
            _body = BuildModel(transform, color, b.Kind);

            _load = new GameObject[LoadBlocks];
            for (int i = 0; i < LoadBlocks; i++)
                _load[i] = Shapes.Box("Load", _body, new Vector3(0f, 0.2f + i * 0.1f, -0.2f), new Vector3(0.24f, 0.09f, 0.14f), Color.white);

            _alert = Shapes.Label("Alert", transform, new Vector3(0f, 1.05f, 0f), "!", Palette.Error, 0.07f);
            _alert.gameObject.AddComponent<Billboard>();
            Snap();
        }

        /// <summary>The bot model, shared with the preview ghost. <paramref name="accent"/> overrides the hat/tank colour.</summary>
        public static Transform BuildModel(Transform parent, Color color, BotKind kind, Color? accent = null, bool shadows = true)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(parent, false);
            Shapes.Box("Wheels", body, new Vector3(0f, 0.06f, 0f), new Vector3(0.36f, 0.12f, 0.3f), Palette.Shade(color, 0.45f), shadows);
            Shapes.Box("Torso", body, new Vector3(0f, 0.28f, 0f), new Vector3(0.4f, 0.32f, 0.32f), color, shadows);
            Shapes.Box("Head", body, new Vector3(0f, 0.55f, 0.02f), new Vector3(0.3f, 0.22f, 0.26f), Palette.Shade(color, 1.15f), shadows);
            Shapes.Box("Eye", body, new Vector3(0f, 0.57f, 0.16f), new Vector3(0.18f, 0.06f, 0.03f), Palette.Hex("#BFF7FF"), false);
            if (kind == BotKind.Player)
            {
                // You: a taller antenna with a lamp, and a little scarf.
                var lamp = accent ?? Palette.Screen;
                Shapes.Box("Antenna", body, new Vector3(0.08f, 0.82f, 0.02f), new Vector3(0.03f, 0.3f, 0.03f), Palette.MetalDark, shadows);
                Shapes.Box("Lamp", body, new Vector3(0.08f, 0.99f, 0.02f), new Vector3(0.08f, 0.08f, 0.08f), lamp, false);
                Shapes.Box("Scarf", body, new Vector3(0f, 0.44f, 0.02f), new Vector3(0.42f, 0.06f, 0.34f), Palette.Hex("#C8553D"), shadows);
            }
            else if (kind == BotKind.WaterBot)
            {
                // A water tank where the hat would be, with a spout over the eye.
                var tank = accent ?? Palette.For(Item.Water);
                var trim = Palette.Shade(tank, 0.7f);
                Shapes.Box("Tank", body, new Vector3(0f, 0.75f, -0.01f), new Vector3(0.3f, 0.18f, 0.26f), tank, shadows);
                Shapes.Box("Lid", body, new Vector3(0f, 0.855f, -0.01f), new Vector3(0.14f, 0.03f, 0.12f), trim, shadows);
                Shapes.Box("Spout", body, new Vector3(0f, 0.79f, 0.17f), new Vector3(0.06f, 0.05f, 0.12f), trim, shadows);
            }
            else
            {
                var hat = accent ?? Palette.Hex("#E9C46A");
                Shapes.Box("Brim", body, new Vector3(0f, 0.68f, 0.02f), new Vector3(0.46f, 0.03f, 0.42f), hat, shadows);
                Shapes.Box("Crown", body, new Vector3(0f, 0.75f, 0.02f), new Vector3(0.24f, 0.12f, 0.22f), hat, shadows);
            }
            return body;
        }

        Vector3 WorldPos(Cell c) => SurfaceView.CellToWorld(c) + Offset;
        static Quaternion Rot(Facing f) => Quaternion.Euler(0f, f.ToYawDegrees(), 0f);

        public void Snap()
        {
            _from = _to = WorldPos(_bot.Position);
            _fromRot = _toRot = Rot(_bot.Facing);
            _bump = false;
            RefreshDecor();
            Apply(1f);
        }

        public void OnTick()
        {
            // Glide on from where the bot is drawn now (not its last tile + today's fan-out offset), so
            // leaving a shared tile or several ticks in one frame never makes it jump.
            _from = transform.localPosition;
            _to = WorldPos(_bot.Position);
            _fromRot = transform.localRotation;
            _toRot = Rot(_bot.Facing);
            _bump = _bot.LastResult == CommandResult.Blocked;
            RefreshDecor();
        }

        void RefreshDecor()
        {
            ShowLoad(_load, _bot.Load.Weight, _bot.Capacity, _bot.Load.Main);
            bool problem = CommandInfo.IsProblem(_bot.LastResult);
            _alert.gameObject.SetActive(problem);
            if (problem) _alert.color = CommandInfo.IsHardProblem(_bot.LastResult) ? Palette.Error : Palette.Warning;
        }

        /// <summary>Show the load as a stack of crates: how full, coloured by the main item.</summary>
        public static void ShowLoad(GameObject[] blocks, int weight, int capacity, Item main)
        {
            int shown = weight <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(blocks.Length * weight / (float)Mathf.Max(1, capacity)), 1, blocks.Length);
            if (weight == 0 && main != Item.None) shown = 1; // weightless seeds still show one crate
            var color = Palette.For(main);
            for (int i = 0; i < blocks.Length; i++)
            {
                bool on = i < shown;
                blocks[i].SetActive(on);
                if (on) Shapes.Recolor(blocks[i], color);
            }
        }

        void Update()
        {
            _selection.SetActive(_game.SelectedBot == _bot);
            Apply(_game.TickProgress);
            bool working = _bot.LastResult == CommandResult.Working && !_game.Paused;
            float bob = working ? 0.05f * Mathf.Abs(Mathf.Sin(Time.time * 14f)) : 0f;
            _body.localPosition = new Vector3(0f, bob, 0f);
        }

        void Apply(float t)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            var pos = Vector3.Lerp(_from, _to, s);
            var rot = Quaternion.Slerp(_fromRot, _toRot, s);
            if (_bump) pos += rot * Vector3.forward * (0.25f * Mathf.Sin(t * Mathf.PI));
            transform.localPosition = pos;
            transform.localRotation = rot;
        }
    }
}
