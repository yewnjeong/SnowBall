using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SnowBall
{
    /// <summary>
    /// 입력 읽기를 한 곳으로 모은 창구.
    ///
    /// 이 프로젝트는 Active Input Handling 이 "Both" 라서 신·구 입력이 둘 다 살아 있다.
    /// 나중에 "Input System Package (New)" 단독으로 바꿔도 깨지지 않도록 조건부 컴파일로 감쌌다.
    ///
    /// 중요: 이동 입력은 **절대 스무딩하지 않는다.**
    /// 기획 요구사항이 "키를 누르지 않으면 즉시 멈춤" 이기 때문에,
    /// 구 입력에서도 GetAxis 가 아니라 GetAxisRaw 를 쓴다.
    /// (GetAxis 는 약 0.2초에 걸쳐 0으로 수렴해서 손을 뗀 뒤에도 미끄러지듯 밀린다)
    /// </summary>
    public static class InputBridge
    {
        /// <summary>WASD. 스무딩 없는 -1/0/1 값.</summary>
        public static Vector2 MoveAxis
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                if (kb == null) return Vector2.zero;

                float x = 0f;
                float y = 0f;

                if (kb.aKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed) x += 1f;
                if (kb.sKey.isPressed) y -= 1f;
                if (kb.wKey.isPressed) y += 1f;

                return new Vector2(x, y);
#else
                return new Vector2(
                    Input.GetAxisRaw("Horizontal"),
                    Input.GetAxisRaw("Vertical")
                );
#endif
            }
        }

        /// <summary>Shift — 눈덩이 잡기 (홀드).</summary>
        public static bool GrabHeld
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                if (kb == null) return false;

                return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
#else
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
            }
        }

        /// <summary>
        /// R — 즉시 재시작 (눌린 순간).
        ///
        /// Get To Work 의 "give up button" 에 해당한다.
        /// 홀드가 아니라 **탭**인 것이 핵심이다. 복구에 드는 비용이 0에 가까워야
        /// 떨어지는 게 짜증이 아니라 그냥 흐름의 일부가 된다.
        /// </summary>
        public static bool RetryPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.rKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.R);
#endif
            }
        }

        /// <summary>F — 지금 자리를 체크포인트로 저장 (눌린 순간).</summary>
        public static bool SaveCheckpointPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.fKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.F);
#endif
            }
        }

        /// <summary>Esc — 일시정지 (눌린 순간).</summary>
        public static bool PausePressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard kb = Keyboard.current;
                return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.Escape);
#endif
            }
        }

        /// <summary>마우스 이동량. 신·구 입력의 스케일 차이를 맞춰서 돌려준다.</summary>
        public static Vector2 LookDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                if (mouse == null) return Vector2.zero;

                // 신규 입력의 delta 는 픽셀 단위라 값이 훨씬 크다.
                return mouse.delta.ReadValue();
#else
                // 구 입력의 Mouse X/Y 는 이미 작게 정규화되어 있어 픽셀 스케일로 맞춰준다.
                return new Vector2(
                    Input.GetAxisRaw("Mouse X"),
                    Input.GetAxisRaw("Mouse Y")
                ) * 10f;
#endif
            }
        }
    }
}
