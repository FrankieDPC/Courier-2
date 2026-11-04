using Wizard.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wizard.Game
{
    /// <summary>
    /// One draggable command block: a palette/spellbook source that never moves, or a row in the
    /// routine being edited. All the drag logic lives in <see cref="RoutineEditor"/>.
    /// </summary>
    public sealed class CommandBlockView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        RoutineEditor _editor;
        Image _bg;
        Text _number;
        Text _label;
        CanvasGroup _group;

        public Command Command { get; private set; }
        public bool FromPalette { get; private set; }
        public int Index { get; private set; }
        public Color BaseColor { get; private set; }

        public static CommandBlockView Create(RoutineEditor editor, Transform parent, Command cmd, bool fromPalette, int index, string label)
        {
            var color = Palette.For(cmd);
            var bg = UIFactory.Panel(fromPalette ? "Palette " + cmd.Type : "Row " + index, parent, color, true, 0.7f);
            var view = bg.gameObject.AddComponent<CommandBlockView>();
            view._editor = editor;
            view._bg = bg;
            view._group = bg.gameObject.AddComponent<CanvasGroup>();
            view.Command = cmd;
            view.FromPalette = fromPalette;
            view.Index = index;
            view.BaseColor = color;

            float left = 10f;
            float right = 8f;
            if (!fromPalette)
            {
                view._number = UIFactory.Label("No", bg.transform, (index + 1).ToString("00"), 13, new Color(1f, 1f, 1f, 0.75f));
                UIFactory.Stretch(view._number.rectTransform, 8, 0, 0, 0);
                left = 36f;
            }
            else if (cmd.IsCast)
            {
                // Spellbook entries get their own "edit" button; drags still go to the block behind it.
                var edit = UIFactory.Button("Edit", bg.transform, "edit", new Color(1f, 1f, 1f, 0.25f), Color.white, 12,
                    () => editor.OpenSpell(cmd.SpellId), out _);
                UIFactory.Place((RectTransform)edit.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(40, 26));
                right = 48f;
            }

            // The label sits in its own clipped strip so long spell names never cover the edit button.
            var clip = UIFactory.Rect("Label Clip", bg.transform);
            UIFactory.Stretch(clip, left, right, 0, 0);
            clip.gameObject.AddComponent<RectMask2D>();
            view._label = UIFactory.Label("Label", clip, label, 15, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            view._label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(view._label.rectTransform);
            return view;
        }

        /// <summary>Update the text in place (e.g. after a spell rename) without rebuilding the block.</summary>
        public void SetLabel(string label) => _label.text = label;

        public static string Describe(Command c, Spellbook book)
        {
            if (c.IsCast)
            {
                var spell = book.Get(c.SpellId);
                return "* " + (spell != null ? spell.Name : "(missing)");
            }
            if (!c.HasSide) return CommandInfo.Name(c.Type);
            string side = c.Side == InteractSide.Auto ? "auto" : c.Side.ToString().ToLowerInvariant();
            return $"{CommandInfo.Name(c.Type)}  <size=12><color=#FFFFFFB0>[{side}]</color></size>";
        }

        /// <summary>Row highlight while the day runs.</summary>
        public void SetState(bool current, CommandResult result)
        {
            var color = BaseColor;
            if (current && CommandInfo.IsProblem(result))
                color = CommandInfo.IsHardProblem(result) ? Palette.Error : Palette.Warning;
            else if (current)
                color = Color.Lerp(BaseColor, Color.white, 0.3f);
            _bg.color = color;
            if (_number != null) _number.text = (current ? "> " : "") + (Index + 1).ToString("00");
            transform.localScale = current ? new Vector3(1.03f, 1.03f, 1f) : Vector3.one;
        }

        public void SetDimmed(bool dimmed) => _group.alpha = dimmed ? 0.35f : 1f;

        // Only the left button drags blocks; right/middle drags belong to the camera.
        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _editor.BeginDrag(this, e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _editor.Drag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _editor.EndDrag(e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.dragging) return;
            if (e.button == PointerEventData.InputButton.Right)
            {
                if (!FromPalette) _editor.RemoveAt(Index);
                return;
            }
            if (e.button != PointerEventData.InputButton.Left) return;

            if (FromPalette) _editor.Append(Command);
            else if (Command.IsCast) _editor.OpenSpell(Command.SpellId);
            else if (Command.HasSide) _editor.CycleSide(Index);
        }
    }
}
