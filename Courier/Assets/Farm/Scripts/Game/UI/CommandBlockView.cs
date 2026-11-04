using Farm.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Farm.Game
{
    /// <summary>
    /// One draggable command block: a palette / custom-command source that never moves, or a row in the
    /// routine being edited. Rows can carry small buttons: "-"/"+" for Grab amounts and "pick" for a
    /// "Go to tile" target. All the drag logic lives in <see cref="RoutineEditor"/>.
    /// </summary>
    public sealed class CommandBlockView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        RoutineEditor _editor;
        Image _bg;
        Text _number;
        Text _label;
        CanvasGroup _group;
        bool _dimmed;
        bool _unavailable;

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
            var mini = new Color(1f, 1f, 1f, 0.25f);

            if (fromPalette && cmd.IsRun)
            {
                // Custom commands get an "edit" button; drags still go to the block behind it.
                MiniButton(view, bg.transform, "edit", 40, 4, mini, () => editor.OpenMacro(cmd.MacroId));
                right = 48f;
            }
            else if (!fromPalette)
            {
                view._number = UIFactory.Label("No", bg.transform, (index + 1).ToString("00"), 13, new Color(1f, 1f, 1f, 0.75f));
                UIFactory.Stretch(view._number.rectTransform, 8, 0, 0, 0);
                left = 44f; // room for "> 150"

                int i = index;
                if (cmd.HasCount)
                {
                    MiniButton(view, bg.transform, "+", 24, 4, mini, () => editor.AdjustCount(i, +1));
                    MiniButton(view, bg.transform, "-", 24, 32, mini, () => editor.AdjustCount(i, -1));
                    right = 62f;
                }
                else if (cmd.NeedsTile)
                {
                    MiniButton(view, bg.transform, "pick", 44, 4, mini, () => editor.PickTile(i));
                    right = 52f;
                }
            }

            // The label sits in its own clipped strip so long names never cover the buttons.
            var clip = UIFactory.Rect("Label Clip", bg.transform);
            UIFactory.Stretch(clip, left, right, 0, 0);
            clip.gameObject.AddComponent<RectMask2D>();
            view._label = UIFactory.Label("Label", clip, label, 15, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            view._label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Stretch(view._label.rectTransform);
            return view;
        }

        static void MiniButton(CommandBlockView owner, Transform parent, string text, float width, float rightInset, Color bg, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIFactory.Button(text, parent, text, bg, Color.white, 13, onClick, out var label);
            label.fontStyle = FontStyle.Bold;
            b.gameObject.AddComponent<ForwardRightClick>().Target = owner; // right-click still deletes the row
            UIFactory.Place((RectTransform)b.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-rightInset, 0), new Vector2(width, 24));
        }

        public static string Describe(Command c, CommandLibrary library)
        {
            if (c.IsRun)
            {
                var m = library.Get(c.MacroId);
                return "* " + (m != null ? m.Name : "(missing)");
            }
            var opt = CommandInfo.Option(c);
            return opt == null
                ? CommandInfo.Name(c.Type)
                : $"{CommandInfo.Name(c.Type)}  <size=12><color=#FFFFFFC0>[{opt}]</color></size>";
        }

        public void SetLabel(string label) => _label.text = label;

        /// <summary>Row highlight while the day runs (or while previewing).</summary>
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

        /// <summary>Faded while it's being dragged.</summary>
        public void SetDimmed(bool dimmed)
        {
            _dimmed = dimmed;
            ApplyAlpha();
        }

        /// <summary>Faded because the bot being edited can't do this job (e.g. a water bot and Till).</summary>
        public void SetUnavailable(bool unavailable)
        {
            if (_unavailable == unavailable) return;
            _unavailable = unavailable;
            ApplyAlpha();
        }

        void ApplyAlpha() => _group.alpha = _dimmed ? 0.35f : _unavailable ? 0.4f : 1f;

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
            else if (Command.IsRun) _editor.OpenMacro(Command.MacroId);
            else if (Command.HasOptions) _editor.CycleOption(Index);
        }
    }
}
