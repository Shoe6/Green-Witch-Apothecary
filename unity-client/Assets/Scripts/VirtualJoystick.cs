using UnityEngine;
using UnityEngine.EventSystems;

namespace GreenWitchApothecary.InputUI
{
    /// <summary>
    /// Touchscreen virtual joystick for Third-Person homestead movement.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [SerializeField] private float handleRange = 60f;

        public Vector2 Direction { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (background == null || handle == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                Vector2 clamped = Vector2.ClampMagnitude(localPoint, handleRange);
                handle.anchoredPosition = clamped;
                Direction = clamped / Mathf.Max(1f, handleRange);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Direction = Vector2.zero;
            if (handle != null)
            {
                handle.anchoredPosition = Vector2.zero;
            }
        }
    }
}
