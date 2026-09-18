using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 게임 전체의 조작감 수치를 한 곳에 모은 에셋.
    /// Docs/GDD.md §4 의 값이 그대로 기본값이며, 튜닝은 전부 여기서 한다.
    ///
    /// 만드는 법: Project 창 우클릭 > Create > SnowBall > Tuning
    /// </summary>
    [CreateAssetMenu(fileName = "SnowBallTuning", menuName = "SnowBall/Tuning")]
    public class SnowBallTuning : ScriptableObject
    {
        // ==========================================
        // 아이 — 이동
        // ==========================================

        [Header("아이 — 이동")]
        [Tooltip("눈덩이를 밀고 있을 때의 속도 (m/s)")]
        public float moveSpeed = 3.2f;

        [Tooltip("눈덩이에서 손을 뗀 채 **계속 달렸을 때** 도달하는 속도 (m/s).\n" +
                 "'놓친 눈덩이를 쫓아가 잡는' 순간을 가능하게 만드는 값이다 —\n" +
                 "눈덩이 최고 속도(maxRollSpeed)보다 느려야 매번 잡히지는 않는다.")]
        public float freeMoveSpeed = 5f;

        [Tooltip("맨몸 이동이 moveSpeed 에서 freeMoveSpeed 까지 붙는 데 걸리는 시간 (초).\n" +
                 "\n" +
                 "0 이면 누르는 순간 최고 속도라 순간이동처럼 느껴진다.\n" +
                 "시간을 주면 짧게 톡톡 누르는 정밀 조작은 느린 속도로 되고,\n" +
                 "길게 눌러 달릴 때만 빨라진다. **멈추는 건 여전히 즉시다.**")]
        public float freeMoveRampTime = 0.45f;

        [Tooltip("이동 방향으로 도는 속도 (도/초)")]
        public float turnSpeed = 720f;

        [Tooltip("오를 수 있는 최대 경사 (도)")]
        public float maxSlopeAngle = 40f;

        [Tooltip("질량 (kg). 눈덩이보다 가벼워야 '밀리는' 느낌이 산다.")]
        public float playerMass = 35f;

        [Tooltip("지면 판정에 쓸 구체 반지름 (m)")]
        public float groundProbeRadius = 0.28f;

        [Tooltip("발밑으로 이만큼까지 훑어서 지면을 찾는다 (m)")]
        public float groundProbeDistance = 0.45f;


        // ==========================================
        // 아이 — 밀기
        // ==========================================

        [Header("아이 — 밀기")]
        [Tooltip("눈덩이에 닿아 밀 때의 힘 (N). 질량이 클수록 F=ma 로 느리게 밀린다.")]
        public float pushForce = 220f;

        [Tooltip("이 값보다 정면으로 밀 때만 힘이 들어간다 (0~1). 스쳐 지나가며 미는 것 방지.")]
        [Range(0f, 1f)] public float pushAlignmentThreshold = 0.25f;

        [Tooltip("눈덩이를 미는 동안, 아이는 눈덩이 속도 + 이 값 이상으로는 못 간다 (m/s).\n" +
                 "이게 있어야 무거운 눈덩이를 몸으로 뚫고 지나가지 못한다.")]
        public float pushOverrunTolerance = 0.35f;


        // ==========================================
        // 아이 — 빙판 (즉시 정지 규칙의 유일한 예외)
        // ==========================================

        [Header("아이 — 빙판")]
        [Tooltip("빙판 위 최대 속도 (m/s)")]
        public float iceMaxSpeed = 4.2f;

        [Tooltip("빙판 위에서 속도가 붙는 정도. 낮을수록 더 미끄럽다.")]
        public float iceAcceleration = 6f;

        [Tooltip("빙판 위에서 입력이 없을 때 감속하는 정도. 낮을수록 오래 미끄러진다.")]
        public float iceDeceleration = 1.2f;


        // ==========================================
        // 잡기 (Shift)
        // ==========================================

        [Header("잡기 (Shift)")]
        [Tooltip("눈덩이 표면으로부터 이 거리 안에 있어야 잡을 수 있다 (m)")]
        public float grabReach = 0.9f;

        [Tooltip("눈덩이가 이 정도는 정면에 있어야 잡힌다 (0~1, 1이면 정확히 정면)")]
        [Range(-1f, 1f)] public float grabFacingThreshold = 0.35f;

        [Tooltip("잡으면 눈덩이를 그 자리에 완전히 고정한다.\n" +
                 "끄면 아래 grabHoldDamping 으로 속도만 죽여서, 경사에서 조금씩 미끄러진다.")]
        public bool grabFullyLocks = true;

        [Tooltip("grabFullyLocks 가 꺼져 있을 때만 쓰인다.\n" +
                 "잡았을 때 눈덩이 속도를 죽이는 세기 (1/초). 클수록 덜 미끄러진다.")]
        public float grabHoldDamping = 14f;

        [Tooltip("움직여서 손을 뗀 뒤, Shift를 놓았다 다시 눌러야 재차 잡히게 할지")]
        public bool requireRepressAfterMoveRelease = true;


        // ==========================================
        // 악력 게이지 — GDD §3.2 A안
        // ==========================================

        [Header("악력 게이지")]
        [Tooltip("drainFlat 과 drainOnSlope 를 모두 0 으로 두면 무제한(C안)이 되어 게이지가 숨겨진다.")]
        public float gripMax = 100f;

        [Tooltip("평지에서 잡고 있을 때 초당 소모")]
        public float gripDrainFlat = 8f;

        [Tooltip("기준 경사에서 잡고 있을 때 초당 소모")]
        public float gripDrainOnSlope = 30f;

        [Tooltip("위 소모량의 기준이 되는 경사 (도)")]
        public float gripDrainReferenceSlope = 20f;

        [Tooltip("놓고 있을 때 초당 회복")]
        public float gripRecovery = 25f;

        [Tooltip("0까지 떨어져 놓친 뒤, 이만큼 회복되어야 다시 잡을 수 있다")]
        public float gripExhaustedLockout = 30f;


        // ==========================================
        // 눈덩이 — 크기
        // ==========================================

        [Header("눈덩이 — 크기")]
        public float startDiameter = 0.8f;
        public float minDiameter = 0.35f;
        public float maxDiameter = 2.2f;

        [Tooltip("엔딩 A 조건 지름")]
        public float targetDiameter = 1.6f;


        // ==========================================
        // 눈덩이 — 물성
        // ==========================================

        [Header("눈덩이 — 물성")]
        [Tooltip("질량 = 지름³ × 이 값. 세제곱이라 지름이 2배면 질량은 8배가 된다.")]
        public float massPerDiameterCubed = 45f;

        [Tooltip("굴러갈 수 있는 최대 속도 (m/s). 통제 불능으로 튀는 것을 막는다.")]
        public float maxRollSpeed = 9f;

        public float snowLinearDamping = 0.20f;
        public float iceLinearDamping = 0.01f;
        public float snowAngularDamping = 0.35f;
        public float iceAngularDamping = 0.05f;


        // ==========================================
        // 눈덩이 — 성장/손상 (GDD §4.3)
        // ==========================================

        [Header("눈덩이 — 성장/손상")]
        [Tooltip("눈밭 위에서 1m 구를 때 지름 증가량")]
        public float growthPerMeterOnSnow = 0.004f;

        public float damageOnTree = 0.06f;
        public float damageOnRock = 0.10f;
        public float damageOnPond = 0.25f;
        public float damageOnThrownSnowball = 0.02f;


        // ==========================================
        // 되감기
        // ==========================================

        [Header("되감기")]
        [Tooltip("체크포인트보다 이만큼 아래로 떨어지면 자동 복구 (m).\n" +
                 "\n" +
                 "**타워 한 층 높이보다 반드시 커야 한다.**\n" +
                 "작으면 아래층에 착지하기 전에 되감겨버려서,\n" +
                 "'건물에서 떨어져 이전 층에 착지한다'는 설계가 통째로 죽는다.\n" +
                 "코스 생성 로그에 찍히는 층간 높이를 보고 맞출 것.")]
        public float fallRespawnDepth = 32f;

        [Tooltip("복구에 걸리는 시간 (초).\n" +
                 "Get To Work 처럼 재시도를 공짜로 만들려면 거의 0이어야 한다.\n" +
                 "이 시간이 길수록 '또 기다려야 하네' 가 쌓인다.")]
        public float respawnFadeDuration = 0.12f;


        // ==========================================
        // 카메라
        // ==========================================

        [Header("카메라")]
        public float cameraDistance = 6.5f;
        public float cameraMinPitch = -5f;
        public float cameraMaxPitch = 55f;
        public float cameraStartPitch = 20f;
        public float mouseSensitivity = 0.12f;

        [Tooltip("위치 따라가는 부드러움 (초). 0이면 즉시.")]
        public float cameraFollowSmoothing = 0.08f;

        [Tooltip("카메라가 바라보는 지점을 아이(0)와 눈덩이(1) 사이 어디로 둘지")]
        [Range(0f, 1f)] public float cameraLookBias = 0.45f;

        [Tooltip("벽에 가릴 때 카메라를 앞으로 당기기 위한 구체 반지름")]
        public float cameraCollisionRadius = 0.25f;

        [Tooltip("벽에 가려도 이 거리보다 가까이는 당기지 않는다 (m).\n" +
                 "너무 작으면 1인칭처럼 아이 머리 속으로 들어가 버린다.")]
        public float cameraMinDistance = 2.2f;


        [Header("카메라 — 속도감")]
        [Tooltip("평상시 시야각")]
        public float cameraBaseFov = 60f;

        [Tooltip("눈덩이가 최고 속도일 때 시야각이 이만큼 넓어진다.\n" +
                 "속도를 화면으로 느끼게 하는 가장 값싸고 확실한 수단이다. 0이면 끔.")]
        public float cameraSpeedFov = 16f;

        [Tooltip("눈덩이가 빠를수록 카메라가 이만큼 더 뒤로 빠진다 (m)")]
        public float cameraSpeedDistance = 1.6f;

        [Tooltip("시야각·거리가 속도를 따라가는 부드러움 (1/초). 클수록 즉각적.")]
        public float cameraSpeedResponse = 4f;

        [Tooltip("이 속도부터 속도감 연출이 시작된다 (m/s).\n" +
                 "밀고 올라가는 평상시에는 화면이 흔들리지 않아야 한다.")]
        public float cameraSpeedThreshold = 3.5f;


        // ==========================================
        // 파생 값
        // ==========================================

        public float MassForDiameter(float diameter)
        {
            return diameter * diameter * diameter * massPerDiameterCubed;
        }

        /// <summary>경사(도)에 따른 초당 악력 소모량.</summary>
        public float GripDrainAtSlope(float slopeDegrees)
        {
            if (gripDrainReferenceSlope <= 0f) return gripDrainFlat;

            float t = Mathf.Clamp01(slopeDegrees / gripDrainReferenceSlope);
            return Mathf.Lerp(gripDrainFlat, gripDrainOnSlope, t);
        }

        /// <summary>악력 게이지를 아예 쓰지 않는 설정인지 (GDD §3.2 C안).</summary>
        public bool GripIsUnlimited
        {
            get { return gripDrainFlat <= 0f && gripDrainOnSlope <= 0f; }
        }
    }
}
