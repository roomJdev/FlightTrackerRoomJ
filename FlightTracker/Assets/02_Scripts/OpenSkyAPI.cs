using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OpenSky Network API 호출 및 파싱 (OAuth2 Client Credentials Flow)
///
/// 무료 계정 한도: 4000 크레딧/일
/// 권장 폴링 간격: 10초
///
/// Inspector에서 Client ID / Client Secret 입력 (credentials.json 참고)
/// ※ credentials를 코드에 하드코딩하거나 버전 관리에 포함하지 말 것
/// </summary>
public class OpenSkyAPI : MonoBehaviour
{
    private const string API_URL        = "https://opensky-network.org/api/states/all";
    private const string TOKEN_ENDPOINT = "https://auth.opensky-network.org/auth/realms/opensky-network/protocol/openid-connect/token";

    [Header("API Settings")]
    [Tooltip("API 호출 간격 (초). 최소 10초 권장.")]
    [SerializeField] private float pollInterval = 10f;

    public event Action<List<AircraftState>> OnDataReceived;

    private string _clientId;
    private string _clientSecret;
    private string _accessToken;
    private float  _tokenExpiresAt;
    private Coroutine _pollCoroutine;

    void Start()
    {
        LoadCredentials();
        _pollCoroutine = StartCoroutine(PollLoop());
    }

    /// <summary>
    /// credentials.json 로드
    ///
    /// 에디터:  프로젝트 루트 (Assets 폴더 옆) — .gitignore로 커밋 방지
    /// 빌드:    실행 파일 옆 (Windows) / .app 바깥 폴더 (macOS)
    ///          → 빌드 배포 시 이 파일을 함께 넣지 말 것
    ///          → 파일 없으면 익명 모드로 자동 폴백
    /// </summary>
    private void LoadCredentials()
    {
        string path;

#if UNITY_EDITOR
        // 에디터: Assets 폴더 한 단계 위 = 프로젝트 루트
        path = System.IO.Path.Combine(Application.dataPath, "..", "credentials.json");
#else
        // 빌드: 실행 파일(또는 .app)과 같은 디렉터리
        path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Application.dataPath), "credentials.json");
#endif
        path = System.IO.Path.GetFullPath(path);

        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[OpenSkyAPI] credentials.json 없음: {path}\n익명 모드로 실행 (크레딧 제한)");
            return;
        }

        try
        {
            string json = System.IO.File.ReadAllText(path);
            var creds = JsonUtility.FromJson<CredentialsFile>(json);
            _clientId     = creds.clientId;
            _clientSecret = creds.clientSecret;
            Debug.Log($"[OpenSkyAPI] credentials 로드 완료 (clientId: {_clientId})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[OpenSkyAPI] credentials.json 파싱 실패: {e.Message}");
        }
    }

    void OnDestroy()
    {
        if (_pollCoroutine != null)
            StopCoroutine(_pollCoroutine);
    }

    // ── 메인 루프 ──────────────────────────────────────────────

    private IEnumerator PollLoop()
    {
        while (true)
        {
            // 토큰이 없거나 만료 60초 전이면 갱신
            if (IsOAuthConfigured() && IsTokenExpiredOrMissing())
                yield return StartCoroutine(FetchToken());

            yield return StartCoroutine(FetchStates());
            yield return new WaitForSeconds(pollInterval);
        }
    }

    // ── OAuth2 토큰 발급 ───────────────────────────────────────

    private bool IsOAuthConfigured() =>
        !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

    private bool IsTokenExpiredOrMissing() =>
        string.IsNullOrEmpty(_accessToken) ||
        Time.realtimeSinceStartup >= _tokenExpiresAt;

    private IEnumerator FetchToken()
    {
        string body = $"grant_type=client_credentials&client_id={Uri.EscapeDataString(_clientId)}&client_secret={Uri.EscapeDataString(_clientSecret)}";
        byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);

        using (UnityWebRequest req = new UnityWebRequest(TOKEN_ENDPOINT, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            req.timeout = 10;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenSkyAPI] 토큰 발급 실패: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            TokenResponse tr = JsonUtility.FromJson<TokenResponse>(req.downloadHandler.text);
            if (tr == null || string.IsNullOrEmpty(tr.access_token))
            {
                Debug.LogError("[OpenSkyAPI] 토큰 응답 파싱 실패");
                yield break;
            }

            _accessToken    = tr.access_token;
            // 만료 60초 전에 갱신하도록 여유분 확보
            _tokenExpiresAt = Time.realtimeSinceStartup + tr.expires_in - 60f;
            Debug.Log($"[OpenSkyAPI] 토큰 발급 성공 (만료까지 {tr.expires_in}초)");
        }
    }

    // ── 항공기 상태 조회 ───────────────────────────────────────

    private IEnumerator FetchStates()
    {
        using (UnityWebRequest req = UnityWebRequest.Get(API_URL))
        {
            if (!string.IsNullOrEmpty(_accessToken))
                req.SetRequestHeader("Authorization", $"Bearer {_accessToken}");

            req.timeout = 8;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[OpenSkyAPI] 요청 실패 ({(int)req.responseCode}): {req.error}");

                // 401이면 토큰 만료 → 강제 갱신
                if (req.responseCode == 401)
                    _accessToken = null;

                yield break;
            }

            List<AircraftState> states = ParseStatesManually(req.downloadHandler.text);
            if (states != null)
            {
                Debug.Log($"[OpenSkyAPI] 항공기 수신: {states.Count}대");
                OnDataReceived?.Invoke(states);
            }
        }
    }

    // ── JSON 파싱 ──────────────────────────────────────────────

    /// <summary>
    /// OpenSky states 배열은 mixed-type jagged array라서 수동 파싱
    /// states[i] = [icao24, callsign, country, time_pos, last_contact,
    ///              longitude(5), latitude(6), baro_alt, on_ground(8), ...]
    /// </summary>
    private List<AircraftState> ParseStatesManually(string json)
    {
        var results = new List<AircraftState>();

        int statesStart = json.IndexOf("\"states\":");
        if (statesStart < 0) return results;

        int arrayStart = json.IndexOf('[', statesStart + 9);
        if (arrayStart < 0) return results;

        int depth = 0;
        int recordStart = -1;

        for (int i = arrayStart; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '[')
            {
                depth++;
                if (depth == 2) recordStart = i;
            }
            else if (c == ']')
            {
                if (depth == 2 && recordStart >= 0)
                {
                    AircraftState state = ParseRecord(json.Substring(recordStart, i - recordStart + 1));
                    if (state != null) results.Add(state);
                    recordStart = -1;
                }
                depth--;
                if (depth == 0) break;
            }
        }

        return results;
    }

    private AircraftState ParseRecord(string record)
    {
        var tokens = new List<string>();
        bool inString = false;
        int start = 1;

        for (int i = 1; i < record.Length - 1; i++)
        {
            char c = record[i];
            if (c == '"') inString = !inString;
            else if (c == ',' && !inString)
            {
                tokens.Add(record.Substring(start, i - start).Trim().Trim('"'));
                start = i + 1;
            }
        }
        tokens.Add(record.Substring(start, record.Length - 1 - start).Trim().Trim('"'));

        if (tokens.Count < 9) return null;

        if (!float.TryParse(tokens[5], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float lon)) return null;
        if (!float.TryParse(tokens[6], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float lat)) return null;

        return new AircraftState
        {
            icao24        = tokens[0],
            callsign      = tokens[1].Trim(),
            originCountry = tokens[2].Trim(),
            latitude      = lat,
            longitude     = lon,
            onGround      = tokens[8] == "true"
        };
    }

    // ── 내부 데이터 클래스 ─────────────────────────────────────

    [Serializable]
    private class CredentialsFile
    {
        public string clientId;
        public string clientSecret;
    }

    [Serializable]
    private class TokenResponse
    {
        public string access_token;
        public int    expires_in;
        public string token_type;
    }
}

[Serializable]
public class AircraftState
{
    public string icao24;
    public string callsign;
    public string originCountry;
    public float  latitude;
    public float  longitude;
    public bool   onGround;
}
