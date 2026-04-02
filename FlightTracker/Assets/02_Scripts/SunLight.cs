using System;
using UnityEngine;

/// <summary>
/// Directional Light를 실시간 UTC 기반 태양 위치에 맞춰 회전
///
/// 태양 위치 계산:
///   - 경도: UTC 12시에 경도 0° 정오 기준 (지구 자전 15°/h)
///   - 위도: 계절에 따른 태양 적위 (-23.45° ~ +23.45°)
///
/// 사용법:
///   Directional Light에 이 컴포넌트 추가 후 Earth Sphere 할당
///   Longitude Offset을 다른 스크립트들과 동일하게 맞출 것
/// </summary>
public class SunLight : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform earthSphere;

    [Header("Coordinate Alignment")]
    [Tooltip("FlightTracker / EarthGeoRenderer 등과 동일한 값 사용")]
    [SerializeField] private float longitudeOffset = 0f;

    [Header("Update")]
    [Tooltip("태양 위치 업데이트 간격 (초). 10초면 충분히 부드러움.")]
    [SerializeField] private float updateInterval = 10f;

    private float _timer;

    void Start() => UpdateSunDirection();

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= updateInterval)
        {
            _timer = 0f;
            UpdateSunDirection();
        }
    }

    private void UpdateSunDirection()
    {
        DateTime utc = DateTime.UtcNow;

        double dayOfYear = utc.DayOfYear;
        double utcHours  = utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0;

        // 태양 적위: 하지 +23.45°, 동지 -23.45°
        double declination = -23.45 * Math.Cos(2.0 * Math.PI * (dayOfYear + 10.0) / 365.0);

        // 태양 경도: UTC 12시 = 경도 0° 정오. longitudeOffset으로 텍스처 정렬
        double sunLon = (12.0 - utcHours) * 15.0 - longitudeOffset;

        float lat = (float)(declination * Mathf.Deg2Rad);
        float lon = (float)(sunLon    * Mathf.Deg2Rad);

        // 구체 로컬 좌표계에서 태양 방향 벡터
        Vector3 localSunDir = new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon)
        );

        // earthSphere 회전 반영 (텍스처 정렬용 회전)
        Vector3 worldSunDir = earthSphere != null
            ? earthSphere.rotation * localSunDir
            : localSunDir;

        // 빛이 태양 위치에서 지구 중심을 향하도록
        transform.rotation = Quaternion.LookRotation(-worldSunDir);

        Debug.Log($"[SunLight] UTC {utc:HH:mm} | 적위 {declination:F1}° | 태양경도 {sunLon % 360:F1}°");
    }
}
