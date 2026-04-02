using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 국경선과 수도를 구체 표면에 직접 렌더링
///
/// 준비:
///   1. Natural Earth 110m 국가 경계 GeoJSON을 다운로드
///      (naturalearthdata.com → Downloads → Cultural → Admin 0 Countries)
///   2. 파일명을 countries.geojson으로 바꿔서 아래 경로에 배치:
///      Assets/StreamingAssets/geo/countries.geojson
///   3. 이 컴포넌트를 씬의 아무 GameObject에나 추가하고 Earth Sphere 할당
///
/// 좌표 정렬:
///   FlightTracker의 Longitude Offset과 동일한 값을 사용해야 dot 위치가 일치함
/// </summary>
public class EarthGeoRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform earthSphere;

    [Header("Material")]
    [Tooltip("URP Unlit 셰이더로 만든 Material 에셋을 할당\n" +
             "(Project > Create > Material → Shader: Universal Render Pipeline/Unlit)")]
    [SerializeField] private Material unlitMaterialTemplate;

    [Header("Border Settings")]
    [SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private float lineWidth = 0.05f;

    [Header("Capital Settings")]
    [SerializeField] private Color capitalColor = new Color(1f, 0.85f, 0f);
    [SerializeField] private float capitalDotSize = 0.25f;

    [Header("Coordinate Alignment")]
    [Tooltip("FlightTracker의 Longitude Offset과 동일하게 맞출 것")]
    [SerializeField] private float longitudeOffset = 0f;
    [SerializeField] private float surfaceOffset = 0.15f;

    private float EarthRadius => earthSphere != null ? earthSphere.localScale.x * 0.5f : 50f;

    private Transform _bordersParent;
    private Transform _capitalsParent;
    private Material _lineMat;
    private Material _capitalMat;

    void Start()
    {
        _bordersParent  = new GameObject("Borders").transform;
        _capitalsParent = new GameObject("Capitals").transform;

        _lineMat    = CreateUnlitMaterial(borderColor);
        _capitalMat = CreateUnlitMaterial(capitalColor);

        StartCoroutine(LoadBorders());
        DrawCapitals();
    }

    void OnDestroy()
    {
        if (_lineMat)    Destroy(_lineMat);
        if (_capitalMat) Destroy(_capitalMat);
    }

    // ── GeoJSON 로드 ───────────────────────────────────────────────

    private IEnumerator LoadBorders()
    {
        string filePath = System.IO.Path.Combine(Application.streamingAssetsPath, "geo", "countries.geojson");
        string uri = new System.Uri(filePath).AbsoluteUri;

        using (var req = UnityWebRequest.Get(uri))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"[EarthGeoRenderer] GeoJSON 로드 실패: {req.error}\n" +
                    $"경로: {filePath}\n" +
                    "Assets/StreamingAssets/geo/countries.geojson 파일을 배치하세요.\n" +
                    "(naturalearthdata.com에서 110m Cultural Vectors 다운로드)");
                yield break;
            }

            List<List<Vector2>> rings = ParseGeoJson(req.downloadHandler.text);
            Debug.Log($"[EarthGeoRenderer] 국경선 {rings.Count}개 링 렌더링 완료");
            DrawBorders(rings);
        }
    }

    // ── GeoJSON 파싱 ───────────────────────────────────────────────

    /// <summary>
    /// GeoJSON에서 모든 폴리곤 링의 좌표 목록을 추출
    /// Polygon / MultiPolygon 모두 처리
    /// </summary>
    private List<List<Vector2>> ParseGeoJson(string json)
    {
        var allRings = new List<List<Vector2>>();
        int searchFrom = 0;

        while (true)
        {
            int idx = json.IndexOf("\"coordinates\":", searchFrom, System.StringComparison.Ordinal);
            if (idx < 0) break;

            int pos = idx + 14;
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != '[') { searchFrom = idx + 1; continue; }

            allRings.AddRange(ParseCoordArray(json, ref pos));
            searchFrom = pos;
        }

        return allRings;
    }

    /// <summary>
    /// 재귀 파서: [lon, lat] 쌍이면 좌표 수집, 중첩 배열이면 재귀
    /// Polygon([ring]) / MultiPolygon([[ring],[ring]]) 구조를 자동 처리
    /// </summary>
    private List<List<Vector2>> ParseCoordArray(string json, ref int pos)
    {
        var rings = new List<List<Vector2>>();
        List<Vector2> currentRing = null;

        SkipWhitespace(json, ref pos);
        if (pos >= json.Length || json[pos] != '[') return rings;
        pos++; // '['

        while (pos < json.Length && json[pos] != ']')
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] == ']') break;

            if (json[pos] == '[')
            {
                int peek = pos + 1;
                SkipWhitespace(json, ref peek);
                bool isCoordPair = peek < json.Length &&
                                   (char.IsDigit(json[peek]) || json[peek] == '-');

                if (isCoordPair)
                {
                    // [lon, lat] 파싱
                    pos++; // '['
                    float lon = ParseFloat(json, ref pos);
                    SkipComma(json, ref pos);
                    float lat = ParseFloat(json, ref pos);

                    // 고도값 스킵 (GeoJSON 3D 좌표 대응)
                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ',')
                    {
                        pos++;
                        ParseFloat(json, ref pos);
                    }
                    SkipWhitespace(json, ref pos);
                    if (pos < json.Length && json[pos] == ']') pos++; // ']'

                    if (currentRing == null) currentRing = new List<Vector2>();
                    currentRing.Add(new Vector2(lon, lat));
                }
                else
                {
                    // 중첩 배열: 현재 링 마무리 후 재귀
                    if (currentRing != null && currentRing.Count > 1)
                    {
                        rings.Add(currentRing);
                        currentRing = null;
                    }
                    rings.AddRange(ParseCoordArray(json, ref pos));
                }
            }

            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ',') pos++;
        }

        if (currentRing != null && currentRing.Count > 1)
            rings.Add(currentRing);

        if (pos < json.Length && json[pos] == ']') pos++; // ']'

        return rings;
    }

    private float ParseFloat(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        int start = pos;
        if (pos < s.Length && s[pos] == '-') pos++;
        while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
        if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
        {
            pos++;
            if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
        }
        return pos == start ? 0f
            : float.Parse(s.Substring(start, pos - start),
                System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
    }

    private void SkipComma(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos < s.Length && s[pos] == ',') pos++;
        SkipWhitespace(s, ref pos);
    }

    // ── 렌더링 ────────────────────────────────────────────────────

    private void DrawBorders(List<List<Vector2>> rings)
    {
        foreach (var ring in rings)
        {
            if (ring.Count < 2) continue;

            var go = new GameObject("Ring");
            go.transform.SetParent(_bordersParent, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.material               = _lineMat;
            lr.startWidth             = lineWidth;
            lr.endWidth               = lineWidth;
            lr.loop                   = true;
            lr.useWorldSpace          = true;
            lr.shadowCastingMode      = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows         = false;
            lr.positionCount          = ring.Count;

            for (int i = 0; i < ring.Count; i++)
                lr.SetPosition(i, LatLonToWorld(ring[i].y, ring[i].x)); // Vector2: x=lon, y=lat
        }
    }

    private void DrawCapitals()
    {
        foreach (var cap in s_Capitals)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = cap.name;
            go.transform.SetParent(_capitalsParent, false);
            go.transform.localScale = Vector3.one * capitalDotSize;
            go.transform.position   = LatLonToWorld(cap.lat, cap.lon);
            go.GetComponent<Renderer>().sharedMaterial = _capitalMat;
            Destroy(go.GetComponent<Collider>());
        }
    }

    // ── 좌표 변환 ─────────────────────────────────────────────────

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

    // ── 재질 생성 ─────────────────────────────────────────────────

    private Material CreateUnlitMaterial(Color color)
    {
        if (unlitMaterialTemplate == null)
        {
            Debug.LogError("[EarthGeoRenderer] unlitMaterialTemplate이 할당되지 않았습니다.\n" +
                           "Inspector에서 URP Unlit Material을 Unlit Material Template에 할당하세요.");
            return new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        }

        var mat = new Material(unlitMaterialTemplate);
        mat.SetColor("_BaseColor", color);
        return mat;
    }

    // ── 세계 수도 목록 ────────────────────────────────────────────

    private struct Capital { public string name; public float lat, lon; }

    private static readonly Capital[] s_Capitals =
    {
        new Capital { name = "Seoul",         lat =  37.57f, lon =  126.98f },
        new Capital { name = "Tokyo",         lat =  35.68f, lon =  139.69f },
        new Capital { name = "Beijing",       lat =  39.90f, lon =  116.39f },
        new Capital { name = "Washington DC", lat =  38.91f, lon =  -77.04f },
        new Capital { name = "London",        lat =  51.51f, lon =   -0.13f },
        new Capital { name = "Paris",         lat =  48.86f, lon =    2.35f },
        new Capital { name = "Berlin",        lat =  52.52f, lon =   13.40f },
        new Capital { name = "Moscow",        lat =  55.76f, lon =   37.62f },
        new Capital { name = "New Delhi",     lat =  28.61f, lon =   77.21f },
        new Capital { name = "Canberra",      lat = -35.28f, lon =  149.13f },
        new Capital { name = "Brasilia",      lat = -15.78f, lon =  -47.93f },
        new Capital { name = "Ottawa",        lat =  45.42f, lon =  -75.69f },
        new Capital { name = "Mexico City",   lat =  19.43f, lon =  -99.13f },
        new Capital { name = "Buenos Aires",  lat = -34.61f, lon =  -58.37f },
        new Capital { name = "Cairo",         lat =  30.05f, lon =   31.25f },
        new Capital { name = "Nairobi",       lat =  -1.29f, lon =   36.82f },
        new Capital { name = "Pretoria",      lat = -25.75f, lon =   28.19f },
        new Capital { name = "Abuja",         lat =   9.08f, lon =    7.40f },
        new Capital { name = "Riyadh",        lat =  24.69f, lon =   46.72f },
        new Capital { name = "Bangkok",       lat =  13.76f, lon =  100.50f },
        new Capital { name = "Jakarta",       lat =  -6.21f, lon =  106.85f },
        new Capital { name = "Islamabad",     lat =  33.68f, lon =   73.05f },
        new Capital { name = "Dhaka",         lat =  23.81f, lon =   90.41f },
        new Capital { name = "Manila",        lat =  14.60f, lon =  120.98f },
        new Capital { name = "Kuala Lumpur",  lat =   3.14f, lon =  101.69f },
        new Capital { name = "Singapore",     lat =   1.35f, lon =  103.82f },
        new Capital { name = "Hanoi",         lat =  21.03f, lon =  105.85f },
        new Capital { name = "Tehran",        lat =  35.69f, lon =   51.39f },
        new Capital { name = "Ankara",        lat =  39.93f, lon =   32.86f },
        new Capital { name = "Rome",          lat =  41.90f, lon =   12.50f },
        new Capital { name = "Madrid",        lat =  40.42f, lon =   -3.70f },
        new Capital { name = "Kyiv",          lat =  50.45f, lon =   30.52f },
        new Capital { name = "Warsaw",        lat =  52.23f, lon =   21.01f },
        new Capital { name = "Athens",        lat =  37.98f, lon =   23.73f },
        new Capital { name = "Accra",         lat =   5.56f, lon =   -0.20f },
        new Capital { name = "Addis Ababa",   lat =   9.03f, lon =   38.74f },
        new Capital { name = "Kabul",         lat =  34.53f, lon =   69.17f },
        new Capital { name = "Pyongyang",     lat =  39.03f, lon =  125.75f },
        new Capital { name = "Taipei",        lat =  25.05f, lon =  121.57f },
        new Capital { name = "Lima",          lat = -12.05f, lon =  -77.04f },
        new Capital { name = "Bogota",        lat =   4.71f, lon =  -74.07f },
        new Capital { name = "Santiago",      lat = -33.46f, lon =  -70.65f },
        new Capital { name = "Caracas",       lat =  10.48f, lon =  -66.88f },
        new Capital { name = "Havana",        lat =  23.14f, lon =  -82.36f },
        new Capital { name = "Stockholm",     lat =  59.33f, lon =   18.07f },
        new Capital { name = "Oslo",          lat =  59.91f, lon =   10.75f },
        new Capital { name = "Helsinki",      lat =  60.17f, lon =   24.94f },
        new Capital { name = "Ulaanbaatar",   lat =  47.89f, lon =  106.91f },
        new Capital { name = "Astana",        lat =  51.18f, lon =   71.45f },
        new Capital { name = "Reykjavik",     lat =  64.13f, lon =  -21.82f },
    };
}
