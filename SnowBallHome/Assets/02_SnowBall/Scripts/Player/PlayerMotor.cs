using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 아이의 이동과 눈덩이 밀기.
    ///
    /// 이 게임의 재미는 **두 물체의 물성 대비**에서 나온다.
    ///   아이  = 관성 0. 키를 떼면 그 프레임에 멈춘다.
    ///   눈덩이 = 관성 덩어리. 한 번 구르면 안 멈춘다.
    ///
    /// 그래서 이동은 힘(AddForce)이 아니라 **속도 직접 제어**로 한다.
    /// 힘으로 하면 가속/감속 곡선이 생겨 "즉시 정지"가 불가능하다.
    ///
    /// 전제: 이 오브젝트의 피벗은 **발밑**에 있다. (CourseBuilder 가 그렇게 만든다)
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("설정")]
        public SnowBallTuning tuning;

        [Tooltip("비워두면 Camera.main 을 쓴다.")]
        public Transform cameraTransform;

        [Tooltip("밀 대상. 비워두면 씬에서 자동으로 찾는다.")]
        public Snowball snowball;

        [Tooltip("바닥으로 칠 레이어. 플레이어 자신의 레이어는 빼는 게 좋다.")]
        public LayerMask groundMask = ~0;

        [Tooltip("발밑 판정을 시작할 지점 (로컬 좌표)")]
        public Vector3 groundProbeOffset = new Vector3(0f, 0.4f, 0f);


        // ==========================================
        // 외부에서 읽는 상태
        // ==========================================

        /// <summary>눈덩이를 잡고 있는 동안 PlayerGrab 이 true 로 만든다. 이동이 완전히 잠긴다.</summary>
        public bool MovementLocked { get; set; }

        /// <summary>카메라 기준으로 변환된 월드 이동 방향 (크기 0~1).</summary>
        public Vector3 MoveInput { get; private set; }

        public bool HasMoveInput { get { return MoveInput.sqrMagnitude > 0.0001f; } }

        public GroundInfo Ground { get { return ground; } }

        public bool IsOnIce
        {
            get { return ground.grounded && ground.surface == SurfaceType.Ice; }
        }


        // ==========================================
        // 내부 상태
        // ==========================================

        private Rigidbody rb;
        private GroundInfo ground;

        // 빙판에서만 쓰는 관성. 일반 바닥에 올라오면 즉시 버린다.
        private Vector3 iceVelocity;

        // 눈덩이와 닿아 있는가 (직전 물리 스텝 기준)
        private bool touchingSnowball;

        // 맨몸으로 연속해서 달린 시간. 길게 달릴수록 빨라진다.
        private float freeRunTime;


        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // 아이가 넘어지면 안 된다. 회전은 코드로만 준다.
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (tuning != null) rb.mass = tuning.playerMass;

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (snowball == null)
            {
                snowball = FindAnyObjectByType<Snowball>();
            }
        }


        private void FixedUpdate()
        {
            if (tuning == null) return;

            // 입력을 Update 가 아니라 여기서 읽는 이유:
            // 한 프레임 안에서 FixedUpdate 는 Update 보다 먼저 돈다.
            // Update 에서 읽으면 이동이 항상 한 프레임 묵은 입력으로 계산되어
            // "즉시 정지"가 미묘하게 늦어진다. 홀드 입력이라 여기서 읽어도 놓치지 않는다.
            MoveInput = ReadMoveDirection();

            SampleGround();

            if (MovementLocked)
            {
                // 눈덩이를 잡고 있는 중 — 한 발짝도 못 움직인다.
                StopHorizontally();
                iceVelocity = Vector3.zero;
                freeRunTime = 0f;
            }
            else if (IsOnIce)
            {
                MoveOnIce();
            }
            else
            {
                MoveGrounded();
            }

            FaceMoveDirection();

            // OnCollisionStay 가 이번 물리 스텝에서 다시 켜준다.
            touchingSnowball = false;
        }


        // ==========================================
        // 입력 → 카메라 기준 월드 방향
        // ==========================================

        private Vector3 ReadMoveDirection()
        {
            Vector2 axis = InputBridge.MoveAxis;

            if (axis.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cameraTransform != null)
            {
                forward = cameraTransform.forward;
                right = cameraTransform.right;
            }

            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 dir = forward * axis.y + right * axis.x;

            // 대각선이 √2 배 빨라지는 것 방지
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            return dir;
        }


        // ==========================================
        // 바닥 판정
        // ==========================================

        private void SampleGround()
        {
            ground = GroundSampler.Sample(
                transform,
                transform.TransformPoint(groundProbeOffset),
                tuning.groundProbeRadius,
                tuning.groundProbeDistance,
                groundMask
            );
        }


        // ==========================================
        // 일반 바닥 — 관성 없음
        // ==========================================

        private void MoveGrounded()
        {
            iceVelocity = Vector3.zero;

            if (!HasMoveInput)
            {
                // 멈추는 건 언제나 즉시다. 달린 시간도 여기서 리셋된다.
                freeRunTime = 0f;
                StopHorizontally();
                return;
            }

            // 밀 때는 느리고, 맨몸일 때는 빠르다.
            // 이 대비가 "놓친 눈덩이를 쫓아간다"를 성립시킨다.
            float speed = ResolveMoveSpeed();

            // 무거운 눈덩이를 몸으로 뚫고 지나가지 못하게, 눈덩이 속도에 맞춰 늦춘다.
            speed = ClampSpeedAgainstSnowball(speed);

            Vector3 desired = MoveInput * speed;

            if (ground.grounded)
            {
                // 경사면을 따라가게 눕힌다.
                // 이걸 안 하면 내리막에서 붕 뜨고 오르막에서 벽에 부딪히듯 걸린다.
                Vector3 alongSlope = Vector3.ProjectOnPlane(desired, ground.normal);
                rb.linearVelocity = alongSlope;
            }
            else
            {
                // 공중 — 수평만 제어하고 낙하 속도는 건드리지 않는다.
                rb.linearVelocity = new Vector3(desired.x, rb.linearVelocity.y, desired.z);
            }
        }


        // ==========================================
        // 빙판 — "즉시 정지" 규칙의 유일한 예외
        // ==========================================

        private void MoveOnIce()
        {
            if (HasMoveInput)
            {
                float speed = ClampSpeedAgainstSnowball(tuning.iceMaxSpeed);

                iceVelocity = Vector3.MoveTowards(
                    iceVelocity,
                    MoveInput * speed,
                    tuning.iceAcceleration * Time.fixedDeltaTime
                );
            }
            else
            {
                // 키를 떼도 바로 안 멈춘다. 여기서만 아이가 눈덩이처럼 굴러다닌다.
                iceVelocity = Vector3.MoveTowards(
                    iceVelocity,
                    Vector3.zero,
                    tuning.iceDeceleration * Time.fixedDeltaTime
                );
            }

            rb.linearVelocity = new Vector3(
                iceVelocity.x,
                rb.linearVelocity.y,
                iceVelocity.z
            );
        }


        /// <summary>
        /// 지금 낼 수 있는 이동 속도.
        ///
        /// 눈덩이를 밀고 있으면 moveSpeed 로 고정.
        /// 맨몸이면 moveSpeed 에서 시작해 freeMoveRampTime 동안 freeMoveSpeed 까지 붙는다.
        ///
        /// 가속을 둔 이유: 최고 속도가 누르는 순간 바로 나오면 순간이동처럼 느껴진다.
        /// 시간을 주면 짧게 톡톡 누르는 정밀 조작은 느리게, 길게 달릴 때만 빠르게 된다.
        /// 멈추는 것은 여전히 즉시라, 기획 요구사항은 그대로다.
        /// </summary>
        private float ResolveMoveSpeed()
        {
            if (touchingSnowball)
            {
                freeRunTime = 0f;
                return tuning.moveSpeed;
            }

            freeRunTime += Time.fixedDeltaTime;

            if (tuning.freeMoveRampTime <= 0f) return tuning.freeMoveSpeed;

            float t = Mathf.Clamp01(freeRunTime / tuning.freeMoveRampTime);

            return Mathf.Lerp(tuning.moveSpeed, tuning.freeMoveSpeed, t);
        }


        private void StopHorizontally()
        {
            Vector3 v = rb.linearVelocity;
            v.x = 0f;
            v.z = 0f;
            rb.linearVelocity = v;
        }


        // ==========================================
        // 눈덩이 무게감 — 아이가 눈덩이를 추월하지 못하게
        // ==========================================

        /// <summary>
        /// 눈덩이에 닿아 밀고 있는 동안에는, 아이의 속도를 눈덩이 속도에 묶는다.
        ///
        /// 이게 없으면 아이는 속도를 직접 제어하므로 사실상 무한한 힘을 갖게 되어
        /// 184kg 짜리 눈덩이도 평지처럼 밀어버린다. 그러면 질량 설계가 전부 무의미해진다.
        /// </summary>
        private float ClampSpeedAgainstSnowball(float desiredSpeed)
        {
            if (!touchingSnowball) return desiredSpeed;
            if (snowball == null) return desiredSpeed;

            Vector3 toBall = snowball.transform.position - transform.position;
            toBall.y = 0f;

            if (toBall.sqrMagnitude < 0.0001f) return desiredSpeed;
            toBall.Normalize();

            // 눈덩이 쪽으로 밀고 있는 게 아니라면(옆으로 빠지거나 뒤로 가면) 제한하지 않는다.
            if (Vector3.Dot(MoveInput, toBall) < tuning.pushAlignmentThreshold)
            {
                return desiredSpeed;
            }

            float ballSpeedAlongPush = Vector3.Dot(snowball.Body.linearVelocity, toBall);
            float allowed = ballSpeedAlongPush + tuning.pushOverrunTolerance;

            return Mathf.Clamp(allowed, 0f, desiredSpeed);
        }


        // ==========================================
        // 바라보는 방향
        // ==========================================

        private void FaceMoveDirection()
        {
            // 잡고 있는 동안에도 방향은 유지해야 한다 (놓는 순간 홱 도는 것 방지).
            if (MovementLocked || !HasMoveInput) return;

            Quaternion target = Quaternion.LookRotation(MoveInput, Vector3.up);

            rb.MoveRotation(Quaternion.RotateTowards(
                rb.rotation,
                target,
                tuning.turnSpeed * Time.fixedDeltaTime
            ));
        }


        // ==========================================
        // 눈덩이 밀기
        // ==========================================

        private void OnCollisionStay(Collision collision)
        {
            if (tuning == null || snowball == null) return;
            if (collision.rigidbody != snowball.Body) return;

            touchingSnowball = true;

            // 잡고 있는 중에는 밀지 않는다. (잡기 = 브레이크)
            if (MovementLocked) return;
            if (!HasMoveInput) return;

            // 접촉 법선 대신 중심→중심 방향을 쓴다.
            // ContactPoint.normal 은 어느 쪽 콜라이더 기준인지 헷갈리기 쉽고,
            // 구체를 밀 때는 중심 방향이 훨씬 안정적이다.
            Vector3 pushDir = snowball.transform.position - transform.position;
            pushDir.y = 0f;

            if (pushDir.sqrMagnitude < 0.0001f) return;
            pushDir.Normalize();

            float alignment = Vector3.Dot(MoveInput, pushDir);
            if (alignment < tuning.pushAlignmentThreshold) return;

            // 접촉점에 힘을 줘야 회전(구르기)이 같이 생긴다.
            ContactPoint contact = collision.GetContact(0);

            snowball.ApplyPush(
                pushDir,
                tuning.pushForce * alignment,
                contact.point
            );
        }


        private void OnDrawGizmosSelected()
        {
            if (tuning == null) return;

            Vector3 origin = transform.TransformPoint(groundProbeOffset);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(origin, tuning.groundProbeRadius);
            Gizmos.DrawWireSphere(
                origin + Vector3.down * tuning.groundProbeDistance,
                tuning.groundProbeRadius
            );
        }
    }
}
