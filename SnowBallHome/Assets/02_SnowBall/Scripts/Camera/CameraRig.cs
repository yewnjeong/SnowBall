using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 아이 뒤를 따라가는 3인칭 카메라.
    ///
    /// 그냥 아이만 따라가면 안 된다. 이 게임에서 플레이어가 실제로 신경 쓰는 대상은
    /// **눈덩이**이기 때문에, 바라보는 지점을 아이와 눈덩이 사이로 살짝 당겨둔다.
    /// (Tuning 의 cameraLookBias)
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CameraRig : MonoBehaviour
    {
        [Header("설정")]
        public SnowBallTuning tuning;

        [Tooltip("따라갈 대상 (아이). 비워두면 PlayerMotor 를 찾는다.")]
        public Transform target;

        [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
        public Snowball snowball;

        [Tooltip("대상의 발밑이 아니라 가슴 높이를 보도록 올려준다.")]
        public Vector3 targetOffset = new Vector3(0f, 1.2f, 0f);

        [Tooltip("카메라가 통과하지 못할 레이어 (벽/지형)")]
        public LayerMask obstructionMask = ~0;

        [Tooltip("시작할 때 마우스 커서를 잠글지")]
        public bool lockCursor = true;


        private float yaw;
        private float pitch;
        private Vector3 smoothedLookPoint;
        private Vector3 followVelocity;

        private Camera cam;

        /// <summary>눈덩이 속도를 0~1 로 환산한 값. 부드럽게 따라간다.</summary>
        private float speedBlend;


        private void Start()
        {
            cam = GetComponent<Camera>();

            GameSettings.Initialize(tuning);

            if (target == null)
            {
                PlayerMotor motor = FindAnyObjectByType<PlayerMotor>();
                if (motor != null) target = motor.transform;
            }

            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();

            if (tuning != null) pitch = tuning.cameraStartPitch;

            yaw = target != null ? target.eulerAngles.y : 0f;
            smoothedLookPoint = ResolveLookPoint();

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }


        private void LateUpdate()
        {
            if (tuning == null || target == null) return;

            ApplyMouseLook();
            UpdateSpeedFeel();

            // 바라볼 지점을 부드럽게 따라간다.
            smoothedLookPoint = Vector3.SmoothDamp(
                smoothedLookPoint,
                ResolveLookPoint(),
                ref followVelocity,
                tuning.cameraFollowSmoothing
            );

            float distance = tuning.cameraDistance + tuning.cameraSpeedDistance * speedBlend;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desired = smoothedLookPoint + rotation * new Vector3(0f, 0f, -distance);

            transform.position = PullOutOfWalls(smoothedLookPoint, desired);
            transform.rotation = Quaternion.LookRotation(smoothedLookPoint - transform.position, Vector3.up);
        }


        /// <summary>
        /// 눈덩이가 빨라질수록 시야각을 넓히고 카메라를 뒤로 뺀다.
        ///
        /// 밀고 올라갈 때는 아무 일도 일어나지 않는다 (임계 속도 미만).
        /// 눈덩이를 놓쳐 굴러가기 시작하는 순간에만 화면이 반응하므로,
        /// "아 큰일났다" 가 머리보다 눈으로 먼저 온다.
        /// </summary>
        private void UpdateSpeedFeel()
        {
            float target = 0f;

            if (snowball != null && snowball.Body != null)
            {
                float speed = snowball.Body.linearVelocity.magnitude;
                float top = Mathf.Max(0.1f, tuning.maxRollSpeed);
                float threshold = Mathf.Clamp(tuning.cameraSpeedThreshold, 0f, top - 0.1f);

                target = Mathf.Clamp01((speed - threshold) / (top - threshold));
            }

            if (!GameSettings.SpeedFovEnabled) target = 0f;

            // 설정 패널을 열면 timeScale 이 0이라 deltaTime 도 0이다.
            // 그래도 시야각 슬라이더는 바로 반영되어야 하므로 unscaled 를 쓴다.
            float dt = Time.unscaledDeltaTime;

            speedBlend = Mathf.Lerp(
                speedBlend, target,
                1f - Mathf.Exp(-tuning.cameraSpeedResponse * dt)
            );

            if (cam != null)
            {
                cam.fieldOfView = GameSettings.BaseFov + tuning.cameraSpeedFov * speedBlend;
            }
        }


        private void ApplyMouseLook()
        {
            // 설정 패널이 열려 있는 동안에는 화면이 돌지 않아야 한다.
            if (SettingsOverlay.IsOpen) return;

            Vector2 look = InputBridge.LookDelta;

            // 감도는 Tuning(에셋)이 아니라 GameSettings(플레이어 설정)에서 읽는다.
            float sensitivity = GameSettings.MouseSensitivity;
            float vertical = GameSettings.InvertY ? -look.y : look.y;

            yaw += look.x * sensitivity;
            pitch -= vertical * sensitivity;

            pitch = Mathf.Clamp(pitch, tuning.cameraMinPitch, tuning.cameraMaxPitch);
        }


        /// <summary>아이와 눈덩이 사이의 한 점. 눈덩이 쪽으로 살짝 치우쳐 있다.</summary>
        private Vector3 ResolveLookPoint()
        {
            Vector3 playerPoint = target.position + targetOffset;

            if (snowball == null) return playerPoint;

            return Vector3.Lerp(playerPoint, snowball.transform.position, tuning.cameraLookBias);
        }


        // 스피어캐스트 결과 버퍼. 매 프레임 할당하지 않으려고 재사용한다.
        private static readonly RaycastHit[] ObstructionBuffer = new RaycastHit[16];

        /// <summary>
        /// 지형에 가리면 카메라를 앞으로 당긴다.
        ///
        /// 주의할 점이 두 가지 있다. 둘 다 안 지키면 **카메라가 1인칭처럼 처박힌다.**
        ///
        /// 1) 시작점(아이와 눈덩이 사이)은 이미 아이 캡슐 안이다.
        ///    겹친 채로 시작한 스피어캐스트는 distance 0 을 돌려주므로,
        ///    그대로 쓰면 카메라가 머리 속으로 들어간다. → distance 0 은 버린다.
        ///
        /// 2) 아이·눈덩이·던져진 눈덩이는 "벽"이 아니다.
        ///    지형은 전부 정적이므로, Rigidbody 가 붙은 것은 장애물에서 제외한다.
        ///    (GroundSampler 와 같은 규칙)
        /// </summary>
        private Vector3 PullOutOfWalls(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float distance = direction.magnitude;

            if (distance < 0.001f) return to;

            direction /= distance;

            int count = Physics.SphereCastNonAlloc(
                from,
                tuning.cameraCollisionRadius,
                direction,
                ObstructionBuffer,
                distance,
                obstructionMask,
                QueryTriggerInteraction.Ignore
            );

            float nearest = distance;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = ObstructionBuffer[i];

                if (hit.collider == null) continue;

                // (1) 시작 지점에서 이미 겹쳐 있는 것
                if (hit.distance <= 0f) continue;

                // (2) 움직이는 물체 — 아이, 눈덩이 등
                if (hit.collider.attachedRigidbody != null) continue;

                if (hit.distance < nearest) nearest = hit.distance;
            }

            // 진짜 벽에 가렸더라도 최소 거리는 지킨다.
            // 단, cameraDistance 를 일부러 아주 짧게 설정한 경우까지 밀어내지는 않는다.
            float minAllowed = Mathf.Min(tuning.cameraMinDistance, distance);
            float pulled = Mathf.Clamp(nearest, minAllowed, distance);

            return from + direction * pulled;
        }
    }
}
