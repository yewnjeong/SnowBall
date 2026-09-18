using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// ⑤ 마을 구간. 아이들이 눈덩이를 던져 방해한다.
    ///
    /// 설계 원칙 (GDD §1.1): 화면 밖에서 날아온 것에 당하지 않는다.
    ///   → 던지기 전 1.5초간 **조준선**을 보여준다. 엄폐물 뒤로 피할 시간을 준다.
    ///
    /// 엄폐물 뒤는 안전하지만 경로가 멀다 — 빠른 길 vs 안전한 길.
    /// </summary>
    public class VillageKid : MonoBehaviour
    {
        [Header("대상 (비우면 자동으로 찾는다)")]
        public Snowball snowball;

        [Header("던지기")]
        public ThrownSnowball projectilePrefab;

        [Tooltip("던지는 지점 (손). 비우면 이 오브젝트 위쪽.")]
        public Transform throwOrigin;

        [Tooltip("조준하는 시간 (초). 이 동안 조준선이 보인다.")]
        public float aimDuration = 1.5f;

        [Tooltip("던지기 간격 (초). 이 범위에서 랜덤.")]
        public Vector2 throwInterval = new Vector2(3f, 5f);

        [Tooltip("눈덩이가 이 거리 안에 있을 때만 던진다 (m)")]
        public float range = 16f;

        [Tooltip("날아가는 시간 (초). 짧을수록 빠르고 피하기 어렵다.")]
        public float flightTime = 0.9f;

        [Header("시야")]
        [Tooltip("이 레이어에 가리면 던지지 않는다 (엄폐물)")]
        public LayerMask obstructionMask = ~0;


        private LineRenderer aimLine;
        private float nextThrowTime;
        private float aimStartedTime = -1f;
        private Vector3 plannedVelocity;


        private void Awake()
        {
            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();

            aimLine = GetComponent<LineRenderer>();
            if (aimLine != null) aimLine.enabled = false;

            ScheduleNextThrow();
        }


        private void Update()
        {
            if (snowball == null || projectilePrefab == null) return;

            bool aiming = aimStartedTime >= 0f;

            if (aiming)
            {
                TickAiming();
                return;
            }

            if (Time.time < nextThrowTime) return;
            if (!CanSeeSnowball()) return;

            BeginAim();
        }


        // ==========================================
        // 조준
        // ==========================================

        private void BeginAim()
        {
            aimStartedTime = Time.time;

            plannedVelocity = SolveBallisticVelocity(
                Origin,
                PredictTargetPoint(),
                flightTime
            );

            if (aimLine != null) aimLine.enabled = true;
        }


        private void TickAiming()
        {
            // 조준 중에도 계속 겨냥을 갱신해서, 조준선이 실제 궤적과 일치하게 한다.
            plannedVelocity = SolveBallisticVelocity(
                Origin,
                PredictTargetPoint(),
                flightTime
            );

            DrawAimLine();
            FaceTarget();

            if (Time.time - aimStartedTime < aimDuration) return;

            Throw();
        }


        private void Throw()
        {
            aimStartedTime = -1f;

            if (aimLine != null) aimLine.enabled = false;

            ThrownSnowball projectile = Instantiate(
                projectilePrefab,
                Origin,
                Quaternion.identity
            );

            projectile.Launch(plannedVelocity);

            ScheduleNextThrow();
        }


        private void ScheduleNextThrow()
        {
            nextThrowTime = Time.time + Random.Range(throwInterval.x, throwInterval.y);
        }


        // ==========================================
        // 계산
        // ==========================================

        private Vector3 Origin
        {
            get
            {
                if (throwOrigin != null) return throwOrigin.position;
                return transform.position + Vector3.up * 1.2f;
            }
        }


        /// <summary>눈덩이가 움직이는 만큼 조금 앞을 겨냥한다.</summary>
        private Vector3 PredictTargetPoint()
        {
            Vector3 point = snowball.transform.position;

            if (snowball.Body != null)
            {
                point += snowball.Body.linearVelocity * flightTime * 0.6f;
            }

            return point;
        }


        /// <summary>start 에서 던져 flightTime 뒤에 target 에 닿는 초기 속도.</summary>
        private static Vector3 SolveBallisticVelocity(Vector3 start, Vector3 target, float time)
        {
            if (time <= 0.01f) time = 0.01f;

            Vector3 displacement = target - start;

            // s = v·t + ½·g·t²  →  v = (s − ½·g·t²) / t
            return (displacement - 0.5f * Physics.gravity * time * time) / time;
        }


        private bool CanSeeSnowball()
        {
            Vector3 origin = Origin;
            Vector3 toBall = snowball.transform.position - origin;

            float distance = toBall.magnitude;
            if (distance > range) return false;

            // 눈덩이 표면까지만 재고, 엄폐물에 가리면 던지지 않는다.
            float checkDistance = Mathf.Max(0.1f, distance - snowball.Radius);

            return !Physics.Raycast(
                origin,
                toBall.normalized,
                checkDistance,
                obstructionMask,
                QueryTriggerInteraction.Ignore
            );
        }


        private void FaceTarget()
        {
            Vector3 dir = snowball.transform.position - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }


        // ==========================================
        // 조준선
        // ==========================================

        private void DrawAimLine()
        {
            if (aimLine == null) return;

            const int steps = 16;

            aimLine.positionCount = steps;

            Vector3 start = Origin;

            for (int i = 0; i < steps; i++)
            {
                float t = flightTime * i / (steps - 1);

                Vector3 point = start
                                + plannedVelocity * t
                                + 0.5f * Physics.gravity * t * t;

                aimLine.SetPosition(i, point);
            }
        }


        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
