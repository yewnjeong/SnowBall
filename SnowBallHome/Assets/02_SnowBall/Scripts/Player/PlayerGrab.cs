using System;
using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// Shift = 눈덩이 붙잡기. 이 게임의 핵심 기믹이다.
    ///
    /// Shift 는 "줍기" 버튼이 아니라 **브레이크**다.
    ///   경사에서 손을 떼면  → 눈덩이가 뒤로 굴러 내려간다
    ///   Shift 를 잡으면      → 눈덩이가 그 자리에 선다
    ///   그런데 잡는 동안엔   → 한 발짝도 못 움직인다 (전진 0)
    ///
    /// 즉 매 순간 이 선택에 묶인다:
    ///   안전하지만 아무 진전이 없다 ↔ 전진하지만 굴러 떨어질 위험
    ///
    /// 기획 규칙: "눈덩이를 잡고 움직이면 손을 떼야 한다."
    ///   → 이동 입력이 들어온 순간 자동으로 놓는다. (아래 UpdateWhileGrabbing)
    /// </summary>
    /// <remarks>
    /// 실행 순서를 PlayerMotor 보다 앞으로 당겨둔다.
    /// 여기서 정한 MovementLocked 를 같은 물리 스텝의 PlayerMotor 가 읽어야 하기 때문.
    /// 순서를 안 정해두면 잡기/놓기가 한 스텝씩 밀려 조작이 끈적해진다.
    /// </remarks>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerGrab : MonoBehaviour
    {
        [Header("설정")]
        public SnowBallTuning tuning;

        [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
        public Snowball snowball;

        [Tooltip("비워두면 같은 오브젝트에서 찾는다. 없으면 악력 제한 없이 동작한다.")]
        public GripStamina grip;

        [Header("손 위치 기준 (로컬)")]
        [Tooltip("이 지점에서 눈덩이까지의 거리로 잡을 수 있는지 판정한다.")]
        public Vector3 handOffset = new Vector3(0f, 1.0f, 0.3f);


        /// <summary>잡기 시작/해제 시 호출. true 면 잡기 시작.</summary>
        public event Action<bool> GrabStateChanged;

        public bool IsGrabbing { get; private set; }

        /// <summary>지금 손이 닿는 거리인가 (HUD 힌트용).</summary>
        public bool InReach { get; private set; }


        private PlayerMotor motor;

        /// <summary>
        /// 이동 입력 여부를 PlayerMotor 가 아니라 직접 읽는다.
        /// 이 컴포넌트가 PlayerMotor 보다 먼저 돌기 때문에,
        /// motor.MoveInput 은 아직 이번 스텝 값으로 갱신되기 전이다.
        /// </summary>
        private static bool HasMoveInput
        {
            get { return InputBridge.MoveAxis.sqrMagnitude > 0.0001f; }
        }

        // 움직여서 손을 뗀 경우, Shift 를 놓았다 다시 눌러야 재차 잡힌다.
        // 안 그러면 Shift 를 쥔 채 WASD 를 톡톡 누르며 조금씩 전진하는 꼼수가 생긴다.
        private bool needsRepress;

        // 달려가면서 잡은 직후, WASD 를 아직 누르고 있는 상태.
        //
        // 이게 없으면 "쫓아가서 잡기"가 불가능하다.
        // 달리는 중에는 이동 입력이 켜져 있으니, 잡자마자 같은 프레임에 손이 떨어진다.
        // 그래서 잡은 뒤 **한 번 손가락을 떼기 전까지는** 이동 입력으로 놓지 않는다.
        // 기획 규칙("잡고 움직이면 손을 뗀다")은 그대로다 —
        // 키를 떼었다가 다시 누르면 그때는 정상적으로 놓아진다.
        private bool waitingForMoveRelease;


        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();

            if (grip == null) grip = GetComponent<GripStamina>();
            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();

            if (tuning != null)
            {
                if (motor.tuning == null) motor.tuning = tuning;
                if (grip != null && grip.tuning == null) grip.tuning = tuning;
            }
        }


        private void FixedUpdate()
        {
            if (tuning == null || snowball == null) return;

            bool wantsGrab = InputBridge.GrabHeld;

            // Shift 를 완전히 놓으면 재잡기 잠금이 풀린다.
            if (!wantsGrab) needsRepress = false;

            InReach = IsWithinReach();

            if (IsGrabbing)
            {
                UpdateWhileGrabbing(wantsGrab);
            }
            else
            {
                UpdateWhileFree(wantsGrab);
            }

            // 잡고 있는 동안 이동을 완전히 잠근다.
            motor.MovementLocked = IsGrabbing;
        }


        // ==========================================
        // 잡고 있는 동안
        // ==========================================

        private void UpdateWhileGrabbing(bool wantsGrab)
        {
            // 1. Shift 를 뗐다
            if (!wantsGrab)
            {
                SetGrabbing(false);
                return;
            }

            // 달려오며 잡은 직후라면, WASD 에서 손을 한 번 뗄 때까지 기다린다.
            if (waitingForMoveRelease && !HasMoveInput) waitingForMoveRelease = false;

            // 2. 움직이려 했다 → 손을 뗀다 (기획 규칙)
            if (HasMoveInput && !waitingForMoveRelease)
            {
                SetGrabbing(false);
                needsRepress = tuning.requireRepressAfterMoveRelease;
                return;
            }

            // 3. 손이 닿지 않는 거리로 벌어졌다 (눈덩이가 미끄러져 빠져나감)
            if (!InReach)
            {
                SetGrabbing(false);
                return;
            }

            // 4. 악력이 다 떨어졌다
            float slope = snowball.Ground.grounded ? snowball.Ground.slopeDegrees : 0f;

            if (grip != null && !grip.DrainWhileHolding(slope, Time.fixedDeltaTime))
            {
                SetGrabbing(false);
                return;
            }

            // 실제로 붙잡는다.
            snowball.ApplyGrabHold(tuning.grabHoldDamping, Time.fixedDeltaTime);
        }


        // ==========================================
        // 잡고 있지 않은 동안
        // ==========================================

        private void UpdateWhileFree(bool wantsGrab)
        {
            if (grip != null) grip.RecoverWhileIdle(Time.fixedDeltaTime);

            if (!wantsGrab) return;
            if (needsRepress) return;
            if (grip != null && !grip.CanGrab) return;
            if (!InReach) return;

            // 달려가면서도 잡을 수 있다. 잡는 순간 아이는 멈춘다.
            // 굴러가는 눈덩이를 쫓아가 낚아채는 이 순간이 이 게임에서 가장 극적인 장면이다.
            waitingForMoveRelease = HasMoveInput;

            SetGrabbing(true);
        }


        // ==========================================
        // 판정
        // ==========================================

        private bool IsWithinReach()
        {
            Vector3 hand = transform.TransformPoint(handOffset);

            Vector3 toBall = snowball.transform.position - hand;

            // 눈덩이 표면까지의 거리로 잰다. 커진 눈덩이는 더 멀리서도 닿는다.
            float surfaceDistance = toBall.magnitude - snowball.Radius;

            if (surfaceDistance > tuning.grabReach) return false;

            // 정면에 있어야 한다. 등 뒤의 눈덩이를 잡을 수는 없다.
            Vector3 flat = toBall;
            flat.y = 0f;

            if (flat.sqrMagnitude < 0.0001f) return true;

            float facing = Vector3.Dot(transform.forward, flat.normalized);

            return facing >= tuning.grabFacingThreshold;
        }


        private void SetGrabbing(bool value)
        {
            if (IsGrabbing == value) return;

            IsGrabbing = value;

            if (!value) waitingForMoveRelease = false;

            // 잡는 순간의 위치를 고정점으로 잡아둬야 그 자리에 못박을 수 있다.
            if (value && snowball != null) snowball.BeginGrabHold();

            if (GrabStateChanged != null) GrabStateChanged(value);
        }


        private void OnDrawGizmosSelected()
        {
            if (tuning == null) return;

            Vector3 hand = transform.TransformPoint(handOffset);

            Gizmos.color = IsGrabbing ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(hand, tuning.grabReach);
        }
    }
}
