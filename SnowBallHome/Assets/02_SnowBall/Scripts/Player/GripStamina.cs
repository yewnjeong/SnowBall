using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 악력 게이지 (GDD §3.2 A안).
    ///
    /// 왜 필요한가:
    ///   Shift 잡기가 무제한이면 "위험할 땐 그냥 계속 잡고 있으면 된다"가 되어
    ///   게임의 유일한 축인 딜레마(안전 ↔ 전진)의 한쪽이 공짜가 된다.
    ///   시간 제한을 걸면 "붙잡고 버티는 것"에도 비용이 생긴다.
    ///
    /// 끄고 싶으면: Tuning 의 gripDrainFlat / gripDrainOnSlope 를 둘 다 0 으로.
    /// 그러면 이 컴포넌트는 있어도 아무 제약이 없고 HUD 에서도 숨겨진다.
    /// </summary>
    public class GripStamina : MonoBehaviour
    {
        public SnowBallTuning tuning;

        private float current;
        private bool lockedOut;

        /// <summary>남은 악력 (0~gripMax).</summary>
        public float Current { get { return current; } }

        public float Normalized
        {
            get
            {
                if (tuning == null || tuning.gripMax <= 0f) return 1f;
                return Mathf.Clamp01(current / tuning.gripMax);
            }
        }

        /// <summary>게이지를 아예 쓰지 않는 설정인지.</summary>
        public bool Unlimited
        {
            get { return tuning == null || tuning.GripIsUnlimited; }
        }

        /// <summary>다 써서 강제로 놓친 뒤, 아직 다시 잡을 수 없는 상태인지.</summary>
        public bool LockedOut { get { return lockedOut; } }

        /// <summary>지금 잡을 수 있는가.</summary>
        public bool CanGrab
        {
            get { return Unlimited || (!lockedOut && current > 0f); }
        }


        private void Awake()
        {
            if (tuning != null) current = tuning.gripMax;
        }


        /// <summary>
        /// 잡고 있는 동안 호출. 더 이상 버틸 수 없으면 false 를 돌려준다.
        /// </summary>
        public bool DrainWhileHolding(float slopeDegrees, float deltaTime)
        {
            if (Unlimited) return true;

            current -= tuning.GripDrainAtSlope(slopeDegrees) * deltaTime;

            if (current <= 0f)
            {
                current = 0f;
                lockedOut = true;
                return false;
            }

            return true;
        }


        /// <summary>놓고 있는 동안 호출.</summary>
        public void RecoverWhileIdle(float deltaTime)
        {
            if (Unlimited) return;
            if (current >= tuning.gripMax) return;

            current = Mathf.Min(tuning.gripMax, current + tuning.gripRecovery * deltaTime);

            // 한 번 바닥나면, 최소한 이만큼은 회복해야 다시 잡을 수 있다.
            if (lockedOut && current >= tuning.gripExhaustedLockout)
            {
                lockedOut = false;
            }
        }


        public void RefillFull()
        {
            if (tuning == null) return;

            current = tuning.gripMax;
            lockedOut = false;
        }
    }
}
