using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>Placeholder monster: a dark wobbling block with glowing eyes and an HP bar.</summary>
    public sealed class MonsterView : MonoBehaviour
    {
        Monster _m;
        GameController _game;
        Transform _body;
        Transform _hpFill;
        Vector3 _from, _to;
        Quaternion _facing = Quaternion.identity;
        float _dieAt = -1f;

        public void Init(Monster m, GameController game)
        {
            _m = m;
            _game = game;
            bool brute = m.Type == Monsters.Brute;
            float size = brute ? 0.6f : 0.38f;
            var color = Palette.For(m.Type);

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            Shapes.Box("Mass", _body, new Vector3(0f, size * 0.5f, 0f), new Vector3(size, size, size), color);
            Shapes.Box("Eyes", _body, new Vector3(0f, size * 0.7f, size * 0.5f), new Vector3(size * 0.6f, size * 0.12f, 0.03f), Palette.DoomGlow, false);

            var bar = new GameObject("HP").transform;
            bar.SetParent(transform, false);
            bar.localPosition = new Vector3(-0.3f, size + 0.2f, 0f);
            Shapes.Box("Back", bar, new Vector3(0.3f, 0f, 0f), new Vector3(0.6f, 0.06f, 0.06f), Palette.Ink, false);
            _hpFill = Shapes.Box("Fill", bar, new Vector3(0.3f, 0.01f, -0.01f), new Vector3(0.6f, 0.07f, 0.07f), Palette.Error, false).transform;

            _from = _to = WorldView.CellToWorld(m.Cell);
            transform.localPosition = _to;
        }

        public void OnTick()
        {
            _from = WorldView.CellToWorld(_m.PreviousCell);
            _to = WorldView.CellToWorld(_m.Cell);
            if (_to != _from) _facing = Quaternion.LookRotation(_to - _from); // body only; the HP bar stays level
            float k = Mathf.Clamp01(_m.Hp / (float)_m.Type.Hp);
            _hpFill.localScale = new Vector3(Mathf.Max(0.001f, 0.6f * k), 0.07f, 0.07f);
            _hpFill.localPosition = new Vector3(0.3f * k, 0.01f, -0.01f);
        }

        /// <summary>Shrink away (killed) or vanish into the tower (arrived), then clean up.</summary>
        public void Die(bool killed)
        {
            if (_dieAt >= 0f) return;
            _dieAt = Time.time;
            if (!killed) transform.localPosition = _to;
        }

        void Update()
        {
            if (_dieAt >= 0f)
            {
                float t = (Time.time - _dieAt) / 0.25f;
                transform.localScale = Vector3.one * Mathf.Max(0f, 1f - t);
                if (t >= 1f) Destroy(gameObject);
                return;
            }

            float s = Mathf.SmoothStep(0f, 1f, _game.TickProgress);
            transform.localPosition = Vector3.Lerp(_from, _to, s);
            _body.localRotation = _facing * Quaternion.Euler(0f, 0f, 6f * Mathf.Sin(Time.time * 9f + _m.Id));
        }
    }
}
