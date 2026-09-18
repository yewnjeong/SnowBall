using System;
using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 눈사람 머리가 될 눈덩이.
    ///
    /// 핵심 규칙 (GDD §4.3): 성장은 양날의 검이다.
    ///   눈밭을 구르면 커진다 → 질량은 지름의 **세제곱**으로 늘어난다
    ///   → 지름이 2배면 질량은 8배 → 후반이 급격히 힘들어진다.
    /// 그래서 "얼마나 키워서 갈 것인가"를 플레이어가 스스로 저울질하게 된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class Snowball : MonoBehaviour
    {
        [Header("설정")]
        public SnowBallTuning tuning;

        [Tooltip("바닥 판정에 포함할 레이어. 보통 플레이어 레이어만 빼면 된다.")]
        public LayerMask groundMask = ~0;

        [Header("상태 (읽기 전용)")]
        [SerializeField] private float diameter = 0.8f;

        /// <summary>지름이 바뀔 때마다 호출. 인자는 새 지름.</summary>
        public event Action<float> SizeChanged;

        private Rigidbody rb;
        private Vector3 lastPosition;
        private GroundInfo ground;

        // Shift 로 붙잡은 순간의 위치. 잡고 있는 동안 여기에 못박는다.
        private Vector3 holdAnchor;

        /// <summary>메시 원본의 지름. 유니티 기본 Sphere 는 스케일 1에서 지름 1이다.</summary>
        private const float BaseMeshDiameter = 1f;

        public Rigidbody Body { get { return rb; } }
        public float Diameter { get { return diameter; } }
        public float Radius { get { return diameter * 0.5f; } }
        public GroundInfo Ground { get { return ground; } }

        /// <summary>목표 크기에 얼마나 도달했는지 (0~1).</summary>
        public float GrowthProgress
        {
            get
            {
                if (tuning == null) return 0f;

                float span = tuning.targetDiameter - tuning.minDiameter;
                if (span <= 0f) return 1f;

                return Mathf.Clamp01((diameter - tuning.minDiameter) / span);
            }
        }


        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // 구르는 맛을 위해 보간을 켜둔다. 무거운 물체가 저프레임에서 튀는 것도 줄여준다.
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (tuning != null)
            {
                SetDiameter(tuning.startDiameter);
            }
            else
            {
                ApplySize();
            }

            lastPosition = rb.position;
        }


        private void FixedUpdate()
        {
            if (tuning == null) return;

            SampleGround();
            ApplySurfaceDrag();
            AccumulateGrowth();
            ClampRollSpeed();

            lastPosition = rb.position;
        }


        // ==========================================
        // 바닥 판정
        // ==========================================

        private void SampleGround()
        {
            // 구체 중심에서 반지름보다 살짝 작은 구로 아래를 훑는다.
            ground = GroundSampler.Sample(
                transform,
                rb.position,
                Radius * 0.9f,
                Radius * 0.35f + 0.1f,
                groundMask
            );
        }


        // ==========================================
        // 바닥 재질에 따른 저항
        // ==========================================

        private void ApplySurfaceDrag()
        {
            bool onIce = ground.grounded && ground.surface == SurfaceType.Ice;

            if (onIce)
            {
                rb.linearDamping = tuning.iceLinearDamping;
                rb.angularDamping = tuning.iceAngularDamping;
            }
            else
            {
                rb.linearDamping = tuning.snowLinearDamping;
                rb.angularDamping = tuning.snowAngularDamping;
            }
        }


        // ==========================================
        // 성장 — 눈밭에서 구른 거리만큼
        // ==========================================

        private void AccumulateGrowth()
        {
            if (!ground.grounded) return;
            if (ground.surface != SurfaceType.Snow) return;

            // 수평으로 실제 이동한 거리만 센다.
            // (제자리에서 헛도는 경우까지 성장으로 쳐주면 어뷰징이 된다)
            Vector3 delta = rb.position - lastPosition;
            delta.y = 0f;

            float travelled = delta.magnitude;
            if (travelled <= 0f) return;

            Grow(travelled * tuning.growthPerMeterOnSnow);
        }


        // ==========================================
        // 최대 속도 제한
        // ==========================================

        private void ClampRollSpeed()
        {
            if (tuning.maxRollSpeed <= 0f) return;

            Vector3 v = rb.linearVelocity;
            if (v.magnitude > tuning.maxRollSpeed)
            {
                rb.linearVelocity = v.normalized * tuning.maxRollSpeed;
            }
        }


        // ==========================================
        // 크기 변경
        // ==========================================

        public void Grow(float delta)
        {
            SetDiameter(diameter + delta);
        }

        /// <summary>나무·바위·연못 등에 부딪혀 깎인다.</summary>
        public void Damage(float amount)
        {
            if (amount <= 0f) return;
            SetDiameter(diameter - amount);
        }

        public void SetDiameter(float value)
        {
            float min = tuning != null ? tuning.minDiameter : 0.2f;
            float max = tuning != null ? tuning.maxDiameter : 5f;

            float clamped = Mathf.Clamp(value, min, max);

            // 의미 없는 미세 변화로 매 프레임 이벤트를 쏘지 않는다.
            if (Mathf.Abs(clamped - diameter) < 0.00001f) return;

            diameter = clamped;
            ApplySize();

            if (SizeChanged != null) SizeChanged(diameter);
        }

        private void ApplySize()
        {
            float scale = diameter / BaseMeshDiameter;
            transform.localScale = new Vector3(scale, scale, scale);

            if (rb != null && tuning != null)
            {
                rb.mass = tuning.MassForDiameter(diameter);
            }
        }


        // ==========================================
        // 밀기 / 잡기 — PlayerMotor, PlayerGrab 에서 호출
        // ==========================================

        /// <summary>
        /// 아이가 미는 힘. 접촉점에 힘을 주어야 회전(구르기)이 같이 생긴다.
        /// 질량을 그대로 쓰므로, 커진 눈덩이는 자연히 느리게 밀린다.
        /// </summary>
        public void ApplyPush(Vector3 direction, float force, Vector3 contactPoint)
        {
            rb.AddForceAtPosition(direction * force, contactPoint, ForceMode.Force);
        }

        /// <summary>잡기 시작. 이 순간의 위치를 고정점으로 기억한다.</summary>
        public void BeginGrabHold()
        {
            holdAnchor = rb.position;
        }


        /// <summary>
        /// Shift 로 붙잡고 있는 동안 매 FixedUpdate 호출.
        ///
        /// 왜 속도만 깎으면 안 되는가:
        ///   경사에서는 중력이 매 스텝 속도를 더하므로, 감쇠와 중력이 평형을 이루는
        ///   지점에서 눈덩이가 계속 흐른다. 20° 경사, 감쇠 14 기준 약 0.2 m/s —
        ///   붙잡고 있는데도 스멀스멀 밀리는 그 느낌이 여기서 나온다.
        ///   그래서 기본값은 **위치 자체를 못박는 것**이다.
        ///
        /// isKinematic 을 켜지 않는 이유는 그대로다:
        ///   무한 질량 벽이 되어 경사 아래쪽의 아이를 밀어내고,
        ///   놓는 순간 속도가 0에서 튀어 어색하다.
        ///   여기서는 매 스텝 속도를 0으로 만들고 위치를 되돌리기만 한다.
        /// </summary>
        public void ApplyGrabHold(float damping, float deltaTime)
        {
            if (tuning != null && !tuning.grabFullyLocks)
            {
                // 예전 방식 — 속도만 죽인다. 경사에서 조금씩 미끄러진다.
                float factor = Mathf.Exp(-damping * deltaTime);

                rb.linearVelocity *= factor;
                rb.angularVelocity *= factor;
                return;
            }

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // 물리 스텝이 밀어낸 만큼 되돌린다. 흐름이 0이 된다.
            rb.position = holdAnchor;
        }


        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
