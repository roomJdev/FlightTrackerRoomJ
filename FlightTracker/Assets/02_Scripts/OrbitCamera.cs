using UnityEngine;

/// <summary>
/// 지구 구체를 중심으로 공전하는 카메라
///
/// 조작:
///   - 마우스 왼쪽 버튼 드래그 → 상하좌우 공전
///   - 스크롤 휠               → 줌 인/아웃
///   - WASD                   → 상하좌우 공전 (키보드)
///   - Q / E                  → 줌 인 / 줌 아웃 (키보드)
///
/// 사용법:
///   Main Camera에 이 컴포넌트를 추가하고 Target에 Earth Sphere 할당
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform target;

    [Header("Orbit")]
    [Tooltip("드래그 감도")]
    [SerializeField] private float orbitSensitivity = 2f;
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax =  80f;

    [Header("Distance")]
    [Tooltip("구체 표면에서 카메라까지 거리 (초기값)")]
    [SerializeField] private float distanceFromSurface = 150f;
    [Tooltip("표면에서 카메라까지 최소 거리")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("표면에서 카메라까지 최대 거리")]
    [SerializeField] private float maxDistance = 500f;

    [Header("Zoom")]
    [Tooltip("스크롤 줌 감도 (거리에 비례)")]
    [SerializeField] private float zoomSensitivity = 0.15f;

    [Header("Keyboard")]
    [Tooltip("WASD 패닝 속도 (도/초)")]
    [SerializeField] private float keyOrbitSpeed = 60f;
    [Tooltip("Q/E 줌 속도 (거리 비례, /초)")]
    [SerializeField] private float keyZoomSpeed  = 1.2f;

    private float _yaw;
    private float _pitch;
    private float _surfaceDist;

    private float SphereRadius => target != null ? target.localScale.x * 0.5f : 50f;
    private Vector3 TargetPos  => target != null ? target.position : Vector3.zero;

    void Start()
    {
        _surfaceDist = distanceFromSurface;

        // 에디터에서 배치된 카메라 위치로부터 초기 yaw/pitch 역산
        Vector3 dir = transform.position - TargetPos;
        if (dir.sqrMagnitude > 0.001f)
        {
            _surfaceDist = Mathf.Max(minDistance, dir.magnitude - SphereRadius);
            _yaw         = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch       = Mathf.Asin(Mathf.Clamp01(dir.normalized.y) * 1f -
                           Mathf.Clamp01(-dir.normalized.y) * 1f) * Mathf.Rad2Deg;
            // 더 안정적인 pitch 계산
            _pitch = Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        ApplyTransform();
    }

    void LateUpdate()
    {
        HandleOrbit();
        HandleZoom();
        HandleKeyboard();
        ApplyTransform();
    }

    private void HandleOrbit()
    {
        if (!Input.GetMouseButton(0)) return;

        _yaw   += Input.GetAxis("Mouse X") * orbitSensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity;
        _pitch  = Mathf.Clamp(_pitch, pitchMin, pitchMax);
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;

        // 현재 거리에 비례한 줌 (멀수록 빠르게, 가까울수록 천천히)
        _surfaceDist -= scroll * _surfaceDist * zoomSensitivity * 10f;
        _surfaceDist  = Mathf.Clamp(_surfaceDist, minDistance, maxDistance);
    }

    private void HandleKeyboard()
    {
        // WASD: 공전 (패닝)
        float h = 0f, v = 0f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;

        if (h != 0f || v != 0f)
        {
            _yaw   += h * keyOrbitSpeed * Time.deltaTime;
            _pitch -= v * keyOrbitSpeed * Time.deltaTime;
            _pitch  = Mathf.Clamp(_pitch, pitchMin, pitchMax);
        }

        // Q/E: 줌 (거리 비례)
        if (Input.GetKey(KeyCode.Q))
            _surfaceDist = Mathf.Clamp(_surfaceDist - _surfaceDist * keyZoomSpeed * Time.deltaTime, minDistance, maxDistance);
        if (Input.GetKey(KeyCode.E))
            _surfaceDist = Mathf.Clamp(_surfaceDist + _surfaceDist * keyZoomSpeed * Time.deltaTime, minDistance, maxDistance);
    }

    private void ApplyTransform()
    {
        Quaternion rot      = Quaternion.Euler(_pitch, _yaw, 0f);
        float      totalDist = SphereRadius + _surfaceDist;

        transform.position = TargetPos + rot * new Vector3(0f, 0f, -totalDist);
        transform.LookAt(TargetPos);
    }
}
