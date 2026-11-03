using Courier.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Courier.Game
{
    /// <summary>
    /// One draggable command block, either in the palette (a source that never moves) or a row in
    /// a courier's schedule. All the actual drag logic lives in <see cref="ScheduleEditor"/>.
    /// </summary>
    public sealed class CommandBlockView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        ScheduleEditor _editor;
        Image _bg;
        Text _label;
        Text _number;
        CanvasGroup _group;

        public Command Command { get; private set; }
        public bool FromPalette { get; private set; }
        public int Index { get; private set; }
        public Color BaseColor { get; private set; }

        public static CommandBlockView Create(ScheduleEditor editor, Transform parent, Command cmd, bool fromPalette, int index)
        {
            var bg = UIFactory.Panel(fromPalette ? "Palette " + cmd.Type : "Row " + index, parent, Palette.For(cmd.Type), true, 0.7f);
            var view = bg.gameObject.AddComponent<CommandBlockView>();
            view._editor = editor;
            view._bg = bg;
            view._group = bg.gameObject.AddComponent<CanvasGroup>();
            view.Command = cmd;
            view.FromPalette = fromPalette;
            view.Index = index;
            view.BaseColor = Palette.For(cmd.Type);

            float left = 10f;
            if (!fromPalette)
            {
                view._number = UIFactory.Label("No", bg.transform, (index + 1).ToString("00"), 13, new Color(1f, 1f, 1f, 0.75f));
                UIFactory.Stretch(view._number.rectTransform, 8, 0, 0, 0);
                left = 36f;
            }

            view._label = UIFactory.Label("Label", bg.transform, Describe(cmd), 16, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(view._label.rectTransform, left, 8, 0, 0);
            return view;
        }

        static string Describe(Command c)
        {
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
                color = Color.Lerp(BaseColor, Color.white, 0.25f);
            _bg.color = color;
            _number.text = (current ? "> " : "") + (Index + 1).ToString("00");
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
            else if (Command.HasSide) _editor.CycleSide(Index);
        }
    }
}
