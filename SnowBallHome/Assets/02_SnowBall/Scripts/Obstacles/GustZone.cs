using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// ⑥ 마지막 산길의 강풍.
    ///
    /// 주기적으로 옆에서 민다. 상시로 불면 그냥 조작이 무거워지기만 하므로,
    /// **불다 그쳤다를 반복해서** 플레이어가 리듬을 읽고 타이밍을 잡게 한다.
    /// (잦아든 틈에 밀고, 불 때 Shift 로 버틴다)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class GustZone : MonoBehaviour
    {
        [Header("바람")]
        [Tooltip("미는 방향 (월드 기준). 정규화해서 쓴다.")]
        public Vector3 direction = Vector3.right;

        [Tooltip("최대 세기 (N). 눈덩이 질량에 비례해 보정된다.")]
        public float force = 45f;

        [Tooltip("한 주기 길이 (초)")]
        public float period = 6f;

        [Tooltip("한 주기 안에서 실제로 부는 비율 (0~1)")]
        [Range(0.05f, 1f)] public float activeRatio = 0.45f;

        [Tooltip("세기가 오르내리는 시간 (초). 갑자기 켜지면 반칙처럼 느껴진다.")]
        public float rampTime = 0.6f;

        [Header("대상")]
        public bool affectSnowball = true;
        public bool affectPlayer = true;

        [Tooltip("아이에게 주는 힘의 비율. 아이까지 세게 밀면 조작이 뺏긴 느낌이 든다.")]
        [Range(0f, 1f)] public float playerForceRatio = 0.35f;


        /// <summary>지금 바람 세기 (0~1). HUD/연출에서 읽어 쓸 수 있다.</summary>
        public float CurrentStrength { get; private set; }


        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }


        private void FixedUpdate()
        {
            CurrentStrength = EvaluateStrength(Time.time);
        }


        private float EvaluateStrength(float time)
        {
            if (period <= 0f) return 1f;

            float phase = Mathf.Repeat(time, period);
            float activeLength = period * activeRatio;

            if (phase > activeLength) return 0f;

            // 시작과 끝에서 부드럽게 오르내린다.
            float ramp = Mathf.Min(rampTime, activeLength * 0.5f);
            if (ramp <= 0f) return 1f;

            float rise = Mathf.Clamp01(phase / ramp);
            float fall = Mathf.Clamp01((activeLength - phase) / ramp);

            return Mathf.Min(rise, fall);
        }


        private void OnTriggerStay(Collider other)
        {
            if (CurrentStrength <= 0f) return;

            Vector3 push = direction.normalized * force * CurrentStrength;

            if (affectSnowball)
            {
                Snowball ball = other.GetComponentInParent<Snowball>();

                if (ball != null)
                {
                    // 질량에 비례해 보정하지 않으면, 커진 눈덩이에겐 바람이 무의미해진다.
                    ball.Body.AddForce(push * ball.Body.mass * 0.02f, ForceMode.Force);
                    return;
                }
            }

            if (affectPlayer)
            {
                PlayerMotor player = other.GetComponentInParent<PlayerMotor>();

                if (player != null)
                {
                    Rigidbody body = player.GetComponent<Rigidbody>();

                    // 아이는 속도를 직접 제어하므로 힘이 거의 먹지 않는다.
                    // 그래서 VelocityChange 로 한 번에 밀어준다.
                    body.AddForce(
                        push * playerForceRatio * 0.02f,
                        ForceMode.VelocityChange
                    );
                }
            }
        }


        private void OnDrawGizmos()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.15f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);

            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(col.bounds.center, direction.normalized * 3f);
        }
    }
}
