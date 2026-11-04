using UnityEngine;
using UnityEngine.EventSystems;

namespace Farm.Game
{
    /// <summary>Sits on a small button inside a command row so right-clicking it still deletes the row.</summary>
    public sealed class ForwardRightClick : MonoBehaviour, IPointerClickHandler
    {
        public CommandBlockView Target;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right && Target != null) Target.OnPointerClick(e);
        }
    }
}
