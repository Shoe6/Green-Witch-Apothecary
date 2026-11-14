using UnityEngine;

namespace GreenWitchApothecary.CameraControl
{
    /// <summary>
    /// Mobile-first Third-Person camera controller supporting touch swipe-to-pan
    /// and mouse-drag fallback in the Unity Editor.
    /// </summary>
    public class SwipePanCameraController : MonoBehaviour
    {
        [Header("Target & Framing")]
        [SerializeField] private Transform followTarget;
        [SerializeField] private Vector3 offset = new Vector3(0f, 9f, -8f);
        [SerializeField] private float followSmoothTime = 0.15f;

        [Header("Swipe-to-Pan")]
        [SerializeField] private float panSensitivity = 0.025f;
        [SerializeField] private Vector2 panBoundsX = new Vector2(-8f, 8f);
        [SerializeField] private Vector2 panBoundsZ = new Vector2(-8f, 8f);

        private Vector3 _panOffset;
        private Vector3 _velocity;
        private Vector2 _lastPointerPos;
        private bool _isPanning;

        private void LateUpdate()
        {
            HandlePointerPan();

            Vector3 anchor = followTarget != null ? followTarget.position : Vector3.zero;
            Vector3 desiredPosition = anchor + offset + _panOffset;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, followSmoothTime);
            transform.LookAt(anchor + _panOffset * 0.5f);
        }

        private void HandlePointerPan()
        {
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    _isPanning = true;
                    _lastPointerPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Moved && _isPanning)
                {
                    ApplyPanDelta(touch.position - _lastPointerPos);
                    _lastPointerPos = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _isPanning = false;
                }
                return;
            }

            // Mouse fallback for Editor testing
            if (Input.GetMouseButtonDown(0))
            {
                _isPanning = true;
                _lastPointerPos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0) && _isPanning)
            {
                Vector2 current = Input.mousePosition;
                ApplyPanDelta(current - _lastPointerPos);
                _lastPointerPos = current;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _isPanning = false;
            }
        }

        private void ApplyPanDelta(Vector2 delta)
        {
            _panOffset.x = Mathf.Clamp(_panOffset.x - delta.x * panSensitivity, panBoundsX.x, panBoundsX.y);
            _panOffset.z = Mathf.Clamp(_panOffset.z - delta.y * panSensitivity, panBoundsZ.x, panBoundsZ.y);
        }

        public void ResetPan()
        {
            _panOffset = Vector3.zero;
        }
    }
}
