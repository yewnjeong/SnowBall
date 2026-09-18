using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 그레이박스용 HUD. IMGUI 라 프리팹 없이 바로 뜬다.
    /// 조작감 검증이 끝나면 uGUI/UI Toolkit 으로 갈아끼울 자리다.
    ///
    /// 원칙 (GDD §8): 화면을 덮지 않는다. 미니맵 없음.
    /// 목표(집)는 UI 가 아니라 지형으로 보이게 만든다.
    /// </summary>
    public class PlayHud : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾는다)")]
        public GameManager game;
        public Snowball snowball;
        public PlayerGrab grab;

        [Header("표시")]
        [Tooltip("좌하단 눈덩이 지름 텍스트")]
        public bool showDiameter = true;

        [Tooltip("지름 텍스트 아래의 진행 막대")]
        public bool showDiameterBar = false;

        [Tooltip("눈덩이 위에 뜨는 악력 막대.\n" +
                 "주의: 이걸 꺼도 악력은 그대로 닳는다. 예고 없이 손이 떨어지는 게 싫으면 " +
                 "Tuning 의 gripDrain* 을 0으로 두어 기능 자체를 끌 것.")]
        public bool showGripGauge = false;

        [Tooltip("상단 조작 안내 텍스트")]
        public bool showControlsHint = true;


        private string stageBanner = "";
        private float stageBannerUntil;

        // 체크포인트 저장/거절 알림
        private string toast = "";
        private float toastUntil;
        private Color toastColor = Color.white;

        private GUIStyle bannerStyle;
        private GUIStyle smallStyle;
        private Texture2D barTexture;


        private void Awake()
        {
            if (game == null) game = FindAnyObjectByType<GameManager>();
            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();
            if (grab == null) grab = FindAnyObjectByType<PlayerGrab>();

            barTexture = new Texture2D(1, 1);
            barTexture.SetPixel(0, 0, Color.white);
            barTexture.Apply();
        }


        private void OnEnable()
        {
            if (game == null) return;

            game.StageEntered += OnStageEntered;
            game.CheckpointSaved += OnCheckpointSaved;
            game.CheckpointRejected += OnCheckpointRejected;
            game.Respawned += OnRespawned;
        }

        private void OnDisable()
        {
            if (game == null) return;

            game.StageEntered -= OnStageEntered;
            game.CheckpointSaved -= OnCheckpointSaved;
            game.CheckpointRejected -= OnCheckpointRejected;
            game.Respawned -= OnRespawned;
        }

        private void OnDestroy()
        {
            if (barTexture != null) Destroy(barTexture);
        }


        private void OnStageEntered(string name)
        {
            stageBanner = name;
            stageBannerUntil = Time.time + 2f;
        }


        private void OnCheckpointSaved(bool manual)
        {
            // 자동 체크포인트는 스테이지 배너로 이미 알려주므로 조용히 넘어간다.
            if (!manual) return;

            ShowToast("체크포인트 저장", new Color(0.6f, 1f, 0.7f));
        }

        private void OnCheckpointRejected(string reason)
        {
            ShowToast(reason, new Color(1f, 0.6f, 0.45f));
        }

        private void OnRespawned()
        {
            ShowToast("체크포인트로 돌아왔다", new Color(1f, 0.9f, 0.5f));
        }

        private void ShowToast(string text, Color color)
        {
            toast = text;
            toastColor = color;
            toastUntil = Time.time + 1.6f;
        }


        private void OnGUI()
        {
            // 설정 패널이 떠 있으면 HUD 는 비켜준다.
            if (SettingsOverlay.IsOpen) return;

            EnsureStyles();

            DrawStageBanner();
            DrawToast();
            DrawDiameter();
            DrawGripGauge();
            DrawControlsHint();
            DrawEndingPanel();
        }


        private void EnsureStyles()
        {
            if (bannerStyle == null)
            {
                bannerStyle = new GUIStyle(GUI.skin.label);
                bannerStyle.fontSize = 28;
                bannerStyle.alignment = TextAnchor.MiddleCenter;
                bannerStyle.normal.textColor = Color.white;
            }

            if (smallStyle == null)
            {
                smallStyle = new GUIStyle(GUI.skin.label);
                smallStyle.fontSize = 14;
                smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
        }


        // ==========================================
        // 스테이지 배너
        // ==========================================

        private void DrawStageBanner()
        {
            if (Time.time > stageBannerUntil) return;
            if (string.IsNullOrEmpty(stageBanner)) return;

            float remaining = stageBannerUntil - Time.time;
            float alpha = Mathf.Clamp01(remaining / 0.6f);

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 40f),
                      stageBanner, bannerStyle);

            GUI.color = previous;
        }


        // ==========================================
        // 눈덩이 지름
        // ==========================================

        private void DrawDiameter()
        {
            if (!showDiameter || snowball == null || snowball.tuning == null) return;

            float diameter = snowball.Diameter;
            float target = snowball.tuning.targetDiameter;

            string text = string.Format("눈덩이  {0:0.00} m   (목표 {1:0.00} m)", diameter, target);

            GUI.Label(new Rect(16f, Screen.height - 58f, 320f, 20f), text, smallStyle);

            if (!showDiameterBar) return;

            // 목표까지의 진행 막대
            Rect back = new Rect(16f, Screen.height - 36f, 220f, 8f);
            DrawBar(back, 1f, new Color(1f, 1f, 1f, 0.18f));

            Color fill = diameter >= target
                ? new Color(0.5f, 1f, 0.6f, 0.9f)
                : new Color(0.7f, 0.85f, 1f, 0.9f);

            DrawBar(back, snowball.GrowthProgress, fill);
        }


        // ==========================================
        // 악력 게이지 — 눈덩이 위에 월드 스페이스로
        // ==========================================

        private void DrawGripGauge()
        {
            if (!showGripGauge || grab == null || grab.grip == null) return;

            GripStamina grip = grab.grip;

            // 무제한 설정이면 게이지 자체를 숨긴다.
            if (grip.Unlimited) return;

            // 꽉 차 있고 잡고 있지도 않으면 굳이 보여줄 필요 없다.
            if (!grab.IsGrabbing && grip.Normalized >= 0.999f) return;

            if (snowball == null || Camera.main == null) return;

            Vector3 world = snowball.transform.position + Vector3.up * (snowball.Radius + 0.5f);
            Vector3 screen = Camera.main.WorldToScreenPoint(world);

            if (screen.z <= 0f) return;

            float width = 90f;
            Rect rect = new Rect(screen.x - width * 0.5f,
                                 Screen.height - screen.y,
                                 width, 7f);

            DrawBar(rect, 1f, new Color(0f, 0f, 0f, 0.45f));

            Color color = grip.LockedOut
                ? new Color(1f, 0.35f, 0.35f, 0.95f)
                : Color.Lerp(new Color(1f, 0.5f, 0.3f), new Color(0.6f, 1f, 0.7f), grip.Normalized);

            DrawBar(rect, grip.Normalized, color);
        }


        // ==========================================
        // 체크포인트 저장/복귀 알림
        // ==========================================

        private void DrawToast()
        {
            if (Time.time > toastUntil) return;
            if (string.IsNullOrEmpty(toast)) return;

            float alpha = Mathf.Clamp01((toastUntil - Time.time) / 0.5f);

            GUIStyle style = new GUIStyle(smallStyle);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 18;
            style.normal.textColor = new Color(toastColor.r, toastColor.g, toastColor.b, alpha);

            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 26f), toast, style);
        }


        // ==========================================
        // 조작 힌트
        // ==========================================

        private void DrawControlsHint()
        {
            if (!showControlsHint) return;
            if (game != null && game.IsFinished) return;

            string hint = "WASD 밀기   ·   Shift 붙잡기(이동 불가)   ·   R 즉시 재시작   ·   F 체크포인트 저장";

            GUI.Label(new Rect(16f, 12f, 600f, 20f), hint, smallStyle);

            if (grab != null && grab.InReach && !grab.IsGrabbing)
            {
                GUI.Label(new Rect(16f, 32f, 400f, 20f), "Shift — 눈덩이를 붙잡을 수 있다", smallStyle);
            }
        }


        // ==========================================
        // 엔딩
        // ==========================================

        private void DrawEndingPanel()
        {
            if (game == null || !game.IsFinished) return;

            Rect panel = new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.35f, 400f, 150f);

            DrawBar(panel, 1f, new Color(0f, 0f, 0f, 0.65f));

            GUI.Label(new Rect(panel.x, panel.y + 16f, panel.width, 36f), "집 도착", bannerStyle);

            string detail = string.Format(
                "{0}\n\n시간 {1:0}초   ·   되감기 {2}회   ·   최종 지름 {3:0.00} m",
                GameManager.DescribeEnding(game.Result),
                game.ElapsedTime,
                game.RespawnCount,
                snowball != null ? snowball.Diameter : 0f
            );

            GUIStyle center = new GUIStyle(smallStyle);
            center.alignment = TextAnchor.UpperCenter;
            center.wordWrap = true;

            GUI.Label(new Rect(panel.x + 20f, panel.y + 62f, panel.width - 40f, 80f), detail, center);
        }


        // ==========================================
        // 막대 그리기
        // ==========================================

        private void DrawBar(Rect rect, float fill, Color color)
        {
            if (fill <= 0f) return;

            Color previous = GUI.color;
            GUI.color = color;

            GUI.DrawTexture(
                new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height),
                barTexture
            );

            GUI.color = previous;
        }
    }
}
