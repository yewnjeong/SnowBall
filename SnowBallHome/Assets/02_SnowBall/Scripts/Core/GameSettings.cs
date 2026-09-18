using System;
using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 플레이어가 바꾸는 설정. PlayerPrefs 에 저장된다.
    ///
    /// 왜 SnowBallTuning 과 분리했는가:
    ///   Tuning 은 **에셋**이다. 런타임에 값을 바꾸면 에디터에서는 에셋 파일이
    ///   실제로 수정되어 다음 실행에도 남는다. 기획자가 정한 기본값이
    ///   플레이어의 감도 조절 때문에 조용히 덮어써지는 사고가 난다.
    ///
    ///   그래서 Tuning 은 **기본값**만 제공하고, 실제로 쓰이는 값은 여기에 둔다.
    ///   나중에 설정 화면은 이 클래스만 읽고 쓰면 된다.
    /// </summary>
    public static class GameSettings
    {
        private const string KeyMouse = "snowball.mouse_sensitivity";
        private const string KeyInvertY = "snowball.invert_y";
        private const string KeyFov = "snowball.base_fov";
        private const string KeySpeedFov = "snowball.speed_fov";

        /// <summary>설정이 바뀔 때마다 호출. UI 갱신용.</summary>
        public static event Action Changed;

        private static bool loaded;

        // 기본값 — Initialize 에서 Tuning 값으로 덮어쓴다.
        private static float mouseSensitivity = 0.12f;
        private static bool invertY;
        private static float baseFov = 60f;
        private static bool speedFovEnabled = true;


        public static float MouseSensitivity
        {
            get { return mouseSensitivity; }
            set
            {
                float clamped = Mathf.Clamp(value, 0.01f, 1f);
                if (Mathf.Approximately(clamped, mouseSensitivity)) return;

                mouseSensitivity = clamped;
                PlayerPrefs.SetFloat(KeyMouse, clamped);
                Raise();
            }
        }

        public static bool InvertY
        {
            get { return invertY; }
            set
            {
                if (invertY == value) return;

                invertY = value;
                PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0);
                Raise();
            }
        }

        public static float BaseFov
        {
            get { return baseFov; }
            set
            {
                float clamped = Mathf.Clamp(value, 40f, 100f);
                if (Mathf.Approximately(clamped, baseFov)) return;

                baseFov = clamped;
                PlayerPrefs.SetFloat(KeyFov, clamped);
                Raise();
            }
        }

        /// <summary>속도가 붙을 때 시야각이 넓어지는 연출. 멀미가 나면 끈다.</summary>
        public static bool SpeedFovEnabled
        {
            get { return speedFovEnabled; }
            set
            {
                if (speedFovEnabled == value) return;

                speedFovEnabled = value;
                PlayerPrefs.SetInt(KeySpeedFov, value ? 1 : 0);
                Raise();
            }
        }


        /// <summary>
        /// Tuning 의 값을 기본값으로 삼아 한 번만 불러온다.
        /// 저장된 값이 있으면 그쪽이 이긴다.
        /// </summary>
        public static void Initialize(SnowBallTuning tuning)
        {
            if (loaded) return;
            loaded = true;

            if (tuning != null)
            {
                mouseSensitivity = tuning.mouseSensitivity;
                baseFov = tuning.cameraBaseFov;
            }

            mouseSensitivity = PlayerPrefs.GetFloat(KeyMouse, mouseSensitivity);
            baseFov = PlayerPrefs.GetFloat(KeyFov, baseFov);
            invertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            speedFovEnabled = PlayerPrefs.GetInt(KeySpeedFov, 1) == 1;
        }


        public static void ResetToDefaults(SnowBallTuning tuning)
        {
            PlayerPrefs.DeleteKey(KeyMouse);
            PlayerPrefs.DeleteKey(KeyInvertY);
            PlayerPrefs.DeleteKey(KeyFov);
            PlayerPrefs.DeleteKey(KeySpeedFov);

            loaded = false;
            Initialize(tuning);

            Raise();
        }


        public static void Save()
        {
            PlayerPrefs.Save();
        }


        private static void Raise()
        {
            if (Changed != null) Changed();
        }
    }
}
