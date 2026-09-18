using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// Esc 로 여는 설정 패널. 그레이박스용 IMGUI 라 프리팹 없이 바로 뜬다.
    ///
    /// 지금 이게 있는 이유는 "설정 화면이 필요해서"가 아니라,
    /// **플레이하면서 감도를 바로 바꿀 수 있어야 조작감 튜닝이 빨라지기 때문**이다.
    /// Play 중지 → 에셋 수정 → 다시 Play 를 반복하면 감각이 끊긴다.
    ///
    /// 나중에 정식 설정 화면은 GameSettings 만 읽고 쓰면 되므로 이 파일은 버려도 된다.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SettingsOverlay : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동으로 찾는다. 기본값 되돌리기에 쓰인다.")]
        public SnowBallTuning tuning;

        /// <summary>열려 있는 동안 카메라 회전과 커서 잠금을 멈춘다.</summary>
        public static bool IsOpen { get; private set; }

        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private float previousTimeScale = 1f;


        private void Awake()
        {
            if (tuning == null)
            {
                GameManager game = FindAnyObjectByType<GameManager>();
                if (game != null) tuning = game.tuning;
            }

            GameSettings.Initialize(tuning);
        }


        private void OnDestroy()
        {
            // 패널을 연 채로 Play 를 멈춰도 시간이 멈춘 채 남지 않게.
            if (IsOpen) Time.timeScale = previousTimeScale;

            IsOpen = false;
        }


        private void Update()
        {
            if (InputBridge.PausePressed) Toggle();
        }


        public void Toggle()
        {
            IsOpen = !IsOpen;

            if (IsOpen)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Time.timeScale = previousTimeScale;

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                GameSettings.Save();
            }
        }


        private void OnGUI()
        {
            if (!IsOpen) return;

            EnsureStyles();

            const float width = 380f;
            const float height = 300f;

            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width, height
            );

            GUI.Box(panel, GUIContent.none);

            GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));

            GUILayout.Label("설정", titleStyle);
            GUILayout.Space(14f);

            // ── 마우스 감도 ──────────────────────
            GUILayout.Label(
                string.Format("마우스 감도    {0:0.000}", GameSettings.MouseSensitivity),
                labelStyle);

            GameSettings.MouseSensitivity = GUILayout.HorizontalSlider(
                GameSettings.MouseSensitivity, 0.02f, 0.5f);

            GUILayout.Space(10f);

            GameSettings.InvertY = GUILayout.Toggle(
                GameSettings.InvertY, "  상하 반전");

            GUILayout.Space(16f);

            // ── 시야각 ──────────────────────────
            GUILayout.Label(
                string.Format("시야각 (FOV)    {0:0}", GameSettings.BaseFov),
                labelStyle);

            GameSettings.BaseFov = GUILayout.HorizontalSlider(
                GameSettings.BaseFov, 45f, 95f);

            GUILayout.Space(10f);

            GameSettings.SpeedFovEnabled = GUILayout.Toggle(
                GameSettings.SpeedFovEnabled, "  속도감 연출 (빠를 때 시야 넓힘)");

            GUILayout.Space(20f);

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("기본값으로", GUILayout.Height(28f)))
            {
                GameSettings.ResetToDefaults(tuning);
            }

            if (GUILayout.Button("닫기  (Esc)", GUILayout.Height(28f)))
            {
                Toggle();
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }


        private void EnsureStyles()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label);
                titleStyle.fontSize = 22;
                titleStyle.normal.textColor = Color.white;
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = 14;
                labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            }
        }
    }
}
