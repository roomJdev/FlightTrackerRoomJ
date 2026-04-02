using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OurAirports CSV 데이터를 기반으로 공항 위치를 빨간 dot으로 표시
///
/// 데이터 준비:
///   1. https://ourairports.com/data/ 에서 airports.csv 다운로드 (무료, 공개 도메인)
///   2. 아래 경로에 배치:
///      Assets/StreamingAssets/geo/airports.csv
///
/// CSV 필드 순서 (OurAirports 표준):
///   0:id, 1:ident(ICAO), 2:type, 3:name, 4:latitude_deg, 5:longitude_deg,
///   6:elevation_ft, 7:continent, 8:iso_country, 9:iso_region, 10:municipality, ...
/// </summary>
public class AirportRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform earthSphere;

    [Header("Airport Dot Settings")]
    [SerializeField] private Color airportColor  = new Color(1f, 0.15f, 0.15f); // 빨간색
    [SerializeField] private float dotSize       = 0.2f;
    [SerializeField] private float surfaceOffset = 0.2f;

    [Header("Filter")]
    [Tooltip("표시할 공항 규모 (작을수록 많이 표시됨)\n" +
             "LargeOnly: 대형 약 500개\n" +
             "LargeAndMedium: 대+중형 약 5,000개")]
    [SerializeField] private AirportFilter filter = AirportFilter.LargeOnly;

    [Header("Coordinate Alignment")]
    [Tooltip("FlightTracker / EarthGeoRenderer 와 동일한 값 사용")]
    [SerializeField] private float longitudeOffset = 0f;

    public enum AirportFilter { LargeOnly, LargeAndMedium }

    private float EarthRadius => earthSphere != null ? earthSphere.localScale.x * 0.5f : 50f;

    private Transform _parent;
    private Material  _mat;

    void Start()
    {
        _parent = new GameObject("Airports").transform;
        _mat    = CreateUnlitMaterial(airportColor);
        StartCoroutine(LoadAirports());
    }

    void OnDestroy()
    {
        if (_mat != null) Destroy(_mat);
    }

    // ── CSV 로드 ───────────────────────────────────────────────

    private IEnumerator LoadAirports()
    {
        string filePath = System.IO.Path.Combine(
            Application.streamingAssetsPath, "geo", "airports.csv");
        string uri = filePath.Contains("://") ? filePath : "file://" + filePath;

        using (var req = UnityWebRequest.Get(uri))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[AirportRenderer] CSV 로드 실패: {req.error}\n" +
                    $"경로: {filePath}\n" +
                    "ourairports.com/data/ 에서 airports.csv를 다운로드해\n" +
                    "Assets/StreamingAssets/geo/airports.csv 에 배치하세요.");
                yield break;
            }

            int count = ParseAndDraw(req.downloadHandler.text);
            Debug.Log($"[AirportRenderer] 공항 {count}개 표시 완료");
        }
    }

    // ── CSV 파싱 & 렌더링 ──────────────────────────────────────

    private int ParseAndDraw(string csv)
    {
        int count = 0;
        bool firstLine = true;

        foreach (string rawLine in csv.Split('\n'))
        {
            // 헤더 스킵
            if (firstLine) { firstLine = false; continue; }

            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] cols = SplitCsvLine(line);
            if (cols.Length < 6) continue;

            string type = cols[2].Trim().Trim('"');

            bool include = filter == AirportFilter.LargeOnly
                ? type == "large_airport"
                : type == "large_airport" || type == "medium_airport";

            if (!include) continue;

            if (!float.TryParse(cols[4].Trim().Trim('"'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float lat)) continue;
            if (!float.TryParse(cols[5].Trim().Trim('"'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float lon)) continue;

            string name = cols.Length > 3 ? cols[3].Trim().Trim('"') : "Airport";

            PlaceDot(name, lat, lon);
            count++;
        }
        return count;
    }

    /// <summary>
    /// 큰따옴표로 묶인 필드(콤마 포함 가능)를 올바르게 분리
    /// </summary>
    private string[] SplitCsvLine(string line)
    {
        var cols = new List<string>();
        bool inQuote = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == ',' && !inQuote)
            {
                cols.Add(line.Substring(start, i - start));
                start = i + 1;
            }
        }
        cols.Add(line.Substring(start));
        return cols.ToArray();
    }

    // ── 배치 ──────────────────────────────────────────────────

    private void PlaceDot(string airportName, float latDeg, float lonDeg)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = airportName;
        go.transform.SetParent(_parent, false);
        go.transform.localScale = Vector3.one * dotSize;
        go.transform.position   = LatLonToWorld(latDeg, lonDeg);
        go.GetComponent<Renderer>().sharedMaterial = _mat;
        Destroy(go.GetComponent<Collider>());
    }

    private Vector3 LatLonToWorld(float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = (lonDeg + longitudeOffset) * Mathf.Deg2Rad;
        float r   = EarthRadius + surfaceOffset;

        float x = r * Mathf.Cos(lat) * Mathf.Cos(lon);
        float y = r * Mathf.Sin(lat);
        float z = r * Mathf.Cos(lat) * Mathf.Sin(lon);

        Vector3 local = new Vector3(x, y, z);
        return earthSphere != null
            ? earthSphere.position + earthSphere.rotation * local
            : local;
    }

    private Material CreateUnlitMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        return mat;
    }
}
