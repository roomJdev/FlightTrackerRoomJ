using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OpenSkyAPI에서 받은 항공기 위치를 지구 구체 표면의 dot으로 표시
///
/// 사용법:
///   1. Hierarchy에서 빈 GameObject를 만들고 이 컴포넌트와 OpenSkyAPI 컴포넌트를 붙인다.
///   2. 씬에 Scale(100,100,100) Sphere를 만들어 EarthSphere에 할당.
///   3. Play하면 10초마다 실시간 항공기 dot이 업데이트된다.
/// </summary>
[RequireComponent(typeof(OpenSkyAPI))]
public class FlightTracker : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Scale 100짜리 지구 구체")]
    [SerializeField] private Transform earthSphere;

    [Header("Dot Settings")]
    [Tooltip("항공기 dot 크기 (기본 0.3)")]
    [SerializeField] private float dotSize = 0.3f;

    [Tooltip("구체 반지름 + 이 값만큼 띄워서 dot을 표면 위에 올림")]
    [SerializeField] private float surfaceOffset = 0.3f;

    [Header("Material")]
    [Tooltip("URP Unlit 셰이더로 만든 Material 에셋을 할당\n" +
             "(Project > Create > Material → Shader: Universal Render Pipeline/Unlit)\n" +
             "미할당 시 빌드에서 분홍색으로 보임")]
    [SerializeField] private Material dotMaterialTemplate;

    [Header("Continent Colors")]
    [SerializeField] private Color colorAsia          = new Color(0.2f, 0.85f, 1.0f);  // 하늘색
    [SerializeField] private Color colorEurope        = new Color(0.5f, 0.5f,  1.0f);  // 파란보라
    [SerializeField] private Color colorNorthAmerica  = new Color(0.3f, 1.0f,  0.4f);  // 초록
    [SerializeField] private Color colorSouthAmerica  = new Color(1.0f, 0.85f, 0.1f);  // 노랑
    [SerializeField] private Color colorAfrica        = new Color(1.0f, 0.5f,  0.1f);  // 주황
    [SerializeField] private Color colorOceania       = new Color(1.0f, 0.35f, 0.8f);  // 분홍
    [SerializeField] private Color colorUnknown       = new Color(0.55f,0.55f, 0.55f); // 회색

    [Header("Coordinate Alignment")]
    [Tooltip("경도 보정값 (도). dot 위치와 텍스처가 어긋날 때 조정.\n" +
             "Unity 기본 Sphere + 표준 등장방형 텍스처 기준 -90이 일반적.")]
    [SerializeField] private float longitudeOffset = -90f;

    // 구체 반지름: scale이 100이면 Unity sphere 기본 반지름 0.5 * 100 = 50
    private float EarthRadius => earthSphere != null
        ? earthSphere.localScale.x * 0.5f
        : 50f;

    private OpenSkyAPI _api;
    private Dictionary<string, GameObject> _dots = new Dictionary<string, GameObject>();

    // dot 재사용을 위한 풀
    private readonly Queue<GameObject> _dotPool = new Queue<GameObject>();
    private Transform _dotsParent;

    // 대륙별 재질 캐시 (continent name → Material)
    private Dictionary<string, Material> _continentMats = new Dictionary<string, Material>();

    void Awake()
    {
        _api = GetComponent<OpenSkyAPI>();
        _api.OnDataReceived += UpdateDots;

        _dotsParent = new GameObject("FlightDots").transform;

        // 대륙별 재질 사전 생성
        _continentMats["Asia"]          = CreateDotMaterial(colorAsia);
        _continentMats["Europe"]        = CreateDotMaterial(colorEurope);
        _continentMats["NorthAmerica"]  = CreateDotMaterial(colorNorthAmerica);
        _continentMats["SouthAmerica"]  = CreateDotMaterial(colorSouthAmerica);
        _continentMats["Africa"]        = CreateDotMaterial(colorAfrica);
        _continentMats["Oceania"]       = CreateDotMaterial(colorOceania);
        _continentMats["Unknown"]       = CreateDotMaterial(colorUnknown);
    }

    void OnDestroy()
    {
        if (_api != null)
            _api.OnDataReceived -= UpdateDots;

        foreach (var mat in _continentMats.Values)
            if (mat != null) Destroy(mat);
    }

    private void UpdateDots(List<AircraftState> states)
    {
        // 이전 dot을 모두 풀로 반환
        ReturnAllToPool();

        foreach (var state in states)
        {
            // 위/경도가 유효한 항공기만 표시
            if (state.latitude == 0f && state.longitude == 0f) continue;

            GameObject dot = GetOrCreateDot();
            PositionDot(dot, state.latitude, state.longitude);

            var renderer = dot.GetComponent<Renderer>();
            string continent = GetContinent(state.originCountry);
            renderer.sharedMaterial = _continentMats[continent];

            dot.name = string.IsNullOrEmpty(state.callsign) ? state.icao24 : state.callsign.Trim();
            dot.SetActive(true);

            _dots[state.icao24] = dot;
        }
    }

    /// <summary>
    /// 위도/경도를 구체 표면의 3D 위치로 변환 (Unity 좌표계: Y-up)
    /// </summary>
    private void PositionDot(GameObject dot, float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = (lonDeg + longitudeOffset) * Mathf.Deg2Rad;
        float r = EarthRadius + surfaceOffset;

        // 구면 → 직교 좌표 (Y-up)
        float x = r * Mathf.Cos(lat) * Mathf.Cos(lon);
        float y = r * Mathf.Sin(lat);
        float z = r * Mathf.Cos(lat) * Mathf.Sin(lon);

        // earthSphere의 rotation을 반영해서 텍스처 방향과 dot 위치를 일치시킴
        Vector3 localOffset = new Vector3(x, y, z);
        dot.transform.position = earthSphere != null
            ? earthSphere.position + earthSphere.rotation * localOffset
            : localOffset;

        // dot이 구체 중심을 바라보도록 회전 (normal 방향)
        dot.transform.LookAt(earthSphere != null ? earthSphere.position : Vector3.zero);
        dot.transform.Rotate(90f, 0f, 0f);
    }

    // ── Object Pool ──────────────────────────────────────────

    private void ReturnAllToPool()
    {
        foreach (var kvp in _dots)
        {
            kvp.Value.SetActive(false);
            _dotPool.Enqueue(kvp.Value);
        }
        _dots.Clear();
    }

    private GameObject GetOrCreateDot()
    {
        if (_dotPool.Count > 0)
            return _dotPool.Dequeue();

        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.transform.SetParent(_dotsParent);
        dot.transform.localScale = Vector3.one * dotSize;

        // Collider 제거 (성능)
        Destroy(dot.GetComponent<Collider>());

        return dot;
    }

    // ── 대륙 매핑 ─────────────────────────────────────────────

    private string GetContinent(string country)
    {
        if (string.IsNullOrEmpty(country)) return "Unknown";
        if (s_CountryToContinent.TryGetValue(country, out string continent))
            return continent;
        return "Unknown";
    }

    private static readonly Dictionary<string, string> s_CountryToContinent =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        // ── Asia ──────────────────────────────────────────────
        { "China",                "Asia" }, { "Japan",               "Asia" },
        { "South Korea",          "Asia" }, { "Korea",               "Asia" },
        { "India",                "Asia" }, { "Taiwan",              "Asia" },
        { "Hong Kong",            "Asia" }, { "Macao",               "Asia" },
        { "Singapore",            "Asia" }, { "Malaysia",            "Asia" },
        { "Thailand",             "Asia" }, { "Indonesia",           "Asia" },
        { "Vietnam",              "Asia" }, { "Philippines",         "Asia" },
        { "Myanmar",              "Asia" }, { "Cambodia",            "Asia" },
        { "Laos",                 "Asia" }, { "Brunei",              "Asia" },
        { "Bangladesh",           "Asia" }, { "Sri Lanka",           "Asia" },
        { "Nepal",                "Asia" }, { "Pakistan",            "Asia" },
        { "Afghanistan",          "Asia" }, { "Maldives",            "Asia" },
        { "Mongolia",             "Asia" }, { "North Korea",         "Asia" },
        { "Timor-Leste",          "Asia" }, { "Bhutan",              "Asia" },
        { "United Arab Emirates", "Asia" }, { "Saudi Arabia",        "Asia" },
        { "Qatar",                "Asia" }, { "Kuwait",              "Asia" },
        { "Bahrain",              "Asia" }, { "Oman",                "Asia" },
        { "Jordan",               "Asia" }, { "Lebanon",             "Asia" },
        { "Israel",               "Asia" }, { "Syria",               "Asia" },
        { "Iraq",                 "Asia" }, { "Iran",                "Asia" },
        { "Yemen",                "Asia" }, { "Turkey",              "Asia" },
        { "Türkiye",              "Asia" }, { "Cyprus",              "Asia" },
        { "Azerbaijan",           "Asia" }, { "Georgia",             "Asia" },
        { "Armenia",              "Asia" }, { "Kazakhstan",          "Asia" },
        { "Uzbekistan",           "Asia" }, { "Turkmenistan",        "Asia" },
        { "Kyrgyzstan",           "Asia" }, { "Tajikistan",          "Asia" },

        // ── Europe ────────────────────────────────────────────
        { "Russia",               "Europe" }, { "Germany",           "Europe" },
        { "France",               "Europe" }, { "United Kingdom",    "Europe" },
        { "Italy",                "Europe" }, { "Spain",             "Europe" },
        { "Netherlands",          "Europe" }, { "Switzerland",       "Europe" },
        { "Austria",              "Europe" }, { "Belgium",           "Europe" },
        { "Sweden",               "Europe" }, { "Norway",            "Europe" },
        { "Denmark",              "Europe" }, { "Finland",           "Europe" },
        { "Poland",               "Europe" }, { "Czech Republic",    "Europe" },
        { "Czechia",              "Europe" }, { "Slovakia",          "Europe" },
        { "Hungary",              "Europe" }, { "Romania",           "Europe" },
        { "Bulgaria",             "Europe" }, { "Greece",            "Europe" },
        { "Portugal",             "Europe" }, { "Croatia",           "Europe" },
        { "Serbia",               "Europe" }, { "Slovenia",          "Europe" },
        { "Ukraine",              "Europe" }, { "Belarus",           "Europe" },
        { "Moldova",              "Europe" }, { "Estonia",           "Europe" },
        { "Latvia",               "Europe" }, { "Lithuania",         "Europe" },
        { "Iceland",              "Europe" }, { "Ireland",           "Europe" },
        { "Luxembourg",           "Europe" }, { "Malta",             "Europe" },
        { "Albania",              "Europe" }, { "Bosnia and Herzegovina", "Europe" },
        { "Montenegro",           "Europe" }, { "North Macedonia",   "Europe" },
        { "Kosovo",               "Europe" }, { "Liechtenstein",     "Europe" },
        { "Monaco",               "Europe" }, { "Andorra",           "Europe" },
        { "San Marino",           "Europe" }, { "Vatican City",      "Europe" },

        // ── North America ─────────────────────────────────────
        { "United States",        "NorthAmerica" }, { "Canada",       "NorthAmerica" },
        { "Mexico",               "NorthAmerica" }, { "Cuba",         "NorthAmerica" },
        { "Guatemala",            "NorthAmerica" }, { "Honduras",     "NorthAmerica" },
        { "El Salvador",          "NorthAmerica" }, { "Nicaragua",    "NorthAmerica" },
        { "Costa Rica",           "NorthAmerica" }, { "Panama",       "NorthAmerica" },
        { "Jamaica",              "NorthAmerica" }, { "Haiti",        "NorthAmerica" },
        { "Dominican Republic",   "NorthAmerica" }, { "Bahamas",      "NorthAmerica" },
        { "Trinidad and Tobago",  "NorthAmerica" }, { "Barbados",     "NorthAmerica" },
        { "Belize",               "NorthAmerica" }, { "Grenada",      "NorthAmerica" },
        { "Saint Lucia",          "NorthAmerica" }, { "Dominica",     "NorthAmerica" },

        // ── South America ─────────────────────────────────────
        { "Brazil",               "SouthAmerica" }, { "Argentina",    "SouthAmerica" },
        { "Chile",                "SouthAmerica" }, { "Colombia",     "SouthAmerica" },
        { "Peru",                 "SouthAmerica" }, { "Venezuela",    "SouthAmerica" },
        { "Ecuador",              "SouthAmerica" }, { "Bolivia",      "SouthAmerica" },
        { "Paraguay",             "SouthAmerica" }, { "Uruguay",      "SouthAmerica" },
        { "Guyana",               "SouthAmerica" }, { "Suriname",     "SouthAmerica" },
        { "French Guiana",        "SouthAmerica" }, { "Trinidad",     "SouthAmerica" },

        // ── Africa ────────────────────────────────────────────
        { "South Africa",         "Africa" }, { "Nigeria",           "Africa" },
        { "Ethiopia",             "Africa" }, { "Kenya",             "Africa" },
        { "Egypt",                "Africa" }, { "Morocco",           "Africa" },
        { "Algeria",              "Africa" }, { "Tunisia",           "Africa" },
        { "Libya",                "Africa" }, { "Ghana",             "Africa" },
        { "Tanzania",             "Africa" }, { "Uganda",            "Africa" },
        { "Angola",               "Africa" }, { "Mozambique",        "Africa" },
        { "Zimbabwe",             "Africa" }, { "Zambia",            "Africa" },
        { "Cameroon",             "Africa" }, { "Senegal",           "Africa" },
        { "Côte d'Ivoire",        "Africa" }, { "Ivory Coast",       "Africa" },
        { "Sudan",                "Africa" }, { "Rwanda",            "Africa" },
        { "Botswana",             "Africa" }, { "Namibia",           "Africa" },
        { "Mauritius",            "Africa" }, { "Madagascar",        "Africa" },
        { "Democratic Republic of the Congo", "Africa" }, { "Congo", "Africa" },
        { "Somalia",              "Africa" }, { "Djibouti",          "Africa" },
        { "Eritrea",              "Africa" }, { "Malawi",            "Africa" },
        { "Gabon",                "Africa" }, { "Benin",             "Africa" },
        { "Niger",                "Africa" }, { "Mali",              "Africa" },
        { "Burkina Faso",         "Africa" }, { "Togo",              "Africa" },
        { "Sierra Leone",         "Africa" }, { "Liberia",           "Africa" },

        // ── Oceania ───────────────────────────────────────────
        { "Australia",            "Oceania" }, { "New Zealand",      "Oceania" },
        { "Papua New Guinea",     "Oceania" }, { "Fiji",             "Oceania" },
        { "Solomon Islands",      "Oceania" }, { "Vanuatu",          "Oceania" },
        { "Samoa",                "Oceania" }, { "Tonga",            "Oceania" },
        { "Kiribati",             "Oceania" }, { "Micronesia",       "Oceania" },
        { "Palau",                "Oceania" }, { "Marshall Islands", "Oceania" },
    };

    private Material CreateDotMaterial(Color color)
    {
        if (dotMaterialTemplate == null)
        {
            Debug.LogError("[FlightTracker] dotMaterialTemplate이 할당되지 않았습니다.\n" +
                           "Inspector에서 URP Unlit Material을 Dot Material Template에 할당하세요.");
            // 에디터 전용 폴백 (빌드에서는 분홍)
            return new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Hidden/InternalErrorShader"));
        }

        var mat = new Material(dotMaterialTemplate);
        mat.SetColor("_BaseColor", color);
        mat.enableInstancing = true;
        return mat;
    }
}
