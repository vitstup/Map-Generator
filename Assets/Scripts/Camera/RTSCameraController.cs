using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

[DisallowMultipleComponent]
public class RTSCameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target; // the transform actually moved by WASD / height clamp

    [Header("Input")]
    [SerializeField] private InputActionReference moveAction;      // Vector2 (WASD)
    [SerializeField] private InputActionReference rotateAction;    // Vector2 (mouse delta), button-modifier (MMB) already configured on the action
    [SerializeField] private InputActionReference zoomAction;      // Vector2 or float (mouse scroll)
    [SerializeField] private InputActionReference sprintAction;    // Button (Shift) - movement speed boost
    [SerializeField] private InputActionReference blockMovingAction;    // Button (Ctrl) 

    [Header("Cinemachine Refs")]
    [SerializeField] private CinemachineOrbitalFollow orbitalFollow;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 30f;
    [SerializeField] private float sprintMultiplier = 2.5f;
    [SerializeField] private float moveSpeedAtMinZoom = 12f;   // slower pan when zoomed in
    [SerializeField] private float moveSpeedAtMaxZoom = 60f;   // faster pan when zoomed out
    [SerializeField] private float moveSmoothTime = 0.15f;
    [SerializeField] private float edgeScreenPanBorder = 0f;   // px, 0 = disabled. Set >0 to enable edge panning.

    [Header("Rotation")]
    [SerializeField] private float rotateSpeed = 0.25f;        // degrees per pixel of mouse delta
    [SerializeField] private bool invertRotation = false;

    [Header("Zoom (Radial Axis, normalized 0..1)")]
    [SerializeField] private float zoomStepBase = 0.01f;       // minimum step size, applied near value = 0
    [SerializeField] private float zoomStepMax = 0.08f;        // maximum step size, applied near value = 1
    [SerializeField] private float zoomCurveExponent = 2f;     // >1 = step grows slowly at first then ramps up faster near 1

    [Header("Terrain Clamping")]
    [SerializeField] private LayerMask groundLayerMask;
    [SerializeField] private float heightAboveGround = 0f;     // target's own height offset above ground
    [SerializeField] private float heightSmoothTime = 0.1f;
    [SerializeField] private float fallbackHeight = 0f;        // used if raycast hits nothing (e.g. off-map)

    private const float RaycastHeight = 500f;       // ray starts this high above the target
    private const float MaxRaycastDistance = 1000f;

    // internal state
    private Vector3 _currentVelocity;
    private Vector3 _moveSmoothVelocity; // SmoothDamp's own internal velocity, must be separate from _currentVelocity
    private float _currentHeightVelocity;
    private float _targetHeight;
    private bool _heightInitialized;

    private void OnEnable()
    {
        moveAction.action.Enable();
        rotateAction.action.Enable();
        zoomAction.action.Enable();
        sprintAction.action.Enable();
        blockMovingAction.action.Enable();
    }

    private void OnDisable()
    {
        moveAction.action.Disable();
        rotateAction.action.Disable();
        zoomAction.action.Disable();
        sprintAction.action.Disable();
        blockMovingAction.action.Disable();
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("RTSCameraController: Target is not assigned.", this);
            enabled = false;
            return;
        }

        // Initialize target height to current ground height so we don't snap on frame 1.
        float groundY = SampleGroundHeight(target.position, out bool hit);
        _targetHeight = hit ? groundY : target.position.y;
        Vector3 p = target.position;
        p.y = _targetHeight + heightAboveGround;
        target.position = p;
        _heightInitialized = true;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        HandleZoom();
        HandleRotation();
        HandleMovement(dt);
        HandleHeightClamp(dt);
    }

    // ------------------------------------------------------------------
    // MOVEMENT (WASD, relative to current camera yaw, with Shift sprint)
    // ------------------------------------------------------------------
    private void HandleMovement(float dt)
    {
        Vector2 input = moveAction.action.ReadValue<Vector2>();

        if (blockMovingAction.action.IsPressed()) input = Vector2.zero;

        if (edgeScreenPanBorder > 0f)
        {
            input += GetEdgePanInput();
            input = Vector2.ClampMagnitude(input, 1f);
        }

        float yaw = orbitalFollow != null ? orbitalFollow.HorizontalAxis.Value : 0f;
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        Vector3 forward = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;
        Vector3 wishDir = (forward * input.y + right * input.x);
        wishDir.y = 0f;
        wishDir = Vector3.ClampMagnitude(wishDir, 1f);

        float speed = GetZoomScaledMoveSpeed();
        bool sprinting = sprintAction.action.ReadValue<float>() > 0.5f;
        if (sprinting) speed *= sprintMultiplier;

        Vector3 wishVelocity = wishDir * speed;

        _currentVelocity = Vector3.SmoothDamp(
            _currentVelocity, wishVelocity, ref _moveSmoothVelocity, moveSmoothTime, Mathf.Infinity, dt);

        target.position += _currentVelocity * dt;
    }

    private float GetZoomScaledMoveSpeed()
    {
        if (orbitalFollow == null) return moveSpeed;
        float range = orbitalFollow.RadialAxis.Range.y - orbitalFollow.RadialAxis.Range.x;
        float t = range > 0f
            ? Mathf.Clamp01((orbitalFollow.RadialAxis.Value - orbitalFollow.RadialAxis.Range.x) / range)
            : 0f;
        return Mathf.Lerp(moveSpeedAtMinZoom, moveSpeedAtMaxZoom, t);
    }

    private Vector2 GetEdgePanInput()
    {
        Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        Vector2 input = Vector2.zero;

        if (mousePos.x <= edgeScreenPanBorder) input.x -= 1f;
        else if (mousePos.x >= Screen.width - edgeScreenPanBorder) input.x += 1f;

        if (mousePos.y <= edgeScreenPanBorder) input.y -= 1f;
        else if (mousePos.y >= Screen.height - edgeScreenPanBorder) input.y += 1f;

        return input;
    }

    // ------------------------------------------------------------------
    // ROTATION (mouse delta while MMB held, via the action's own button modifier)
    // Pitch clamping is intentionally NOT duplicated here - it's owned by
    // OrbitalFollow's Vertical Axis Range in the inspector.
    // ------------------------------------------------------------------
    private void HandleRotation()
    {
        if (orbitalFollow == null) return;

        Vector2 delta = rotateAction.action.ReadValue<Vector2>();
        if (delta.sqrMagnitude <= 0.0001f) return;

        float sign = invertRotation ? -1f : 1f;

        float newHorizontal = orbitalFollow.HorizontalAxis.Value + delta.x * rotateSpeed * sign;
        orbitalFollow.HorizontalAxis.Value = NormalizeAngle(newHorizontal);

        // Clamp to VerticalAxis's own configured Range (set in the inspector under
        // Orbital Follow -> Vertical Axis -> Range). We read the range rather than
        // hardcoding it, so the inspector stays the single source of truth.
        float newVertical = orbitalFollow.VerticalAxis.Value - delta.y * rotateSpeed * sign;
        orbitalFollow.VerticalAxis.Value = Mathf.Clamp(
            newVertical, orbitalFollow.VerticalAxis.Range.x, orbitalFollow.VerticalAxis.Range.y);
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f) angle += 360f;
        return angle;
    }

    // ------------------------------------------------------------------
    // ZOOM (scroll wheel -> OrbitalFollow.RadialAxis.Value)
    // Step size grows non-linearly with the current normalized position within
    // RadialAxis.Range: tiny steps near the range minimum (zoomed in), large
    // steps near the range maximum (zoomed out). Result is clamped to
    // RadialAxis's own Range (inspector), never hardcoded.
    // ------------------------------------------------------------------
    private void HandleZoom()
    {
        if (orbitalFollow == null) return;

        Vector2 scroll = zoomAction.action.ReadValue<Vector2>();
        float scrollValue = Mathf.Abs(scroll.y) > Mathf.Abs(scroll.x) ? scroll.y : scroll.x;
        if (Mathf.Approximately(scrollValue, 0f)) return;

        float direction = Mathf.Sign(scrollValue); // +1 = zoom out, -1 = zoom in (scroll up)

        float rangeMin = orbitalFollow.RadialAxis.Range.x;
        float rangeMax = orbitalFollow.RadialAxis.Range.y;
        float rangeSize = rangeMax - rangeMin;

        float currentValue = orbitalFollow.RadialAxis.Value;
        float normalized = rangeSize > 0f ? Mathf.Clamp01((currentValue - rangeMin) / rangeSize) : 0f;

        float step = Mathf.Lerp(zoomStepBase, zoomStepMax, Mathf.Pow(normalized, zoomCurveExponent));
        float scaledStep = step * rangeSize; // step computed in normalized space, scaled back to axis units

        float newValue = currentValue + direction * scaledStep;
        orbitalFollow.RadialAxis.Value = Mathf.Clamp(newValue, rangeMin, rangeMax);
    }

    // ------------------------------------------------------------------
    // TERRAIN CLAMPING (keep the Target glued to ground height, smoothed)
    // ------------------------------------------------------------------
    private void HandleHeightClamp(float dt)
    {
        float groundY = SampleGroundHeight(target.position, out bool hit);
        float desiredHeight = (hit ? groundY : fallbackHeight) + heightAboveGround;

        if (!_heightInitialized)
        {
            _targetHeight = desiredHeight;
            _heightInitialized = true;
        }

        _targetHeight = Mathf.SmoothDamp(_targetHeight, desiredHeight, ref _currentHeightVelocity, heightSmoothTime, Mathf.Infinity, dt);

        Vector3 pos = target.position;
        pos.y = _targetHeight;
        target.position = pos;
    }

    private float SampleGroundHeight(Vector3 worldPos, out bool hit)
    {
        Vector3 rayOrigin = new Vector3(worldPos.x, worldPos.y + RaycastHeight, worldPos.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit raycastHit, MaxRaycastDistance, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            hit = true;
            return raycastHit.point.y;
        }
        hit = false;
        return fallbackHeight;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target.position, 0.5f);
        Gizmos.DrawLine(target.position, target.position + Vector3.down * 2f);
    }
#endif
}