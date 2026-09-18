using System;
using System.Collections;
using UnityEngine;

namespace SnowBall
{
    public enum Ending
    {
        None,
        A_Complete,     // 지름 >= targetDiameter — 머리가 딱 맞게 올라간다
        B_SmallHead,    // 조금 작다. 그래도 눈사람이다
        C_AlmostMelted  // 거의 다 녹았다. 쓸쓸하지만 따뜻함
    }


    /// <summary>
    /// 돌아갈 지점. 자동 체크포인트든, R 로 직접 찍은 것이든, 게임 시작 지점이든
    /// 전부 이 형태로 통일해서 들고 있는다.
    ///
    /// Checkpoint 컴포넌트를 직접 참조하지 않는 이유:
    ///   수동 체크포인트와 시작 지점에는 대응하는 컴포넌트가 아예 없다.
    ///   좌표만 들고 있으면 셋을 같은 코드로 다룰 수 있다.
    /// </summary>
    public struct RespawnPoint
    {
        public Vector3 playerPosition;
        public Vector3 snowballPosition;
        public string stageName;

        /// <summary>자동 체크포인트의 진행 순서. 수동/시작 지점은 -1.</summary>
        public int order;

        public bool isManual;
    }


    /// <summary>
    /// 체크포인트·되감기·엔딩을 관리한다.
    ///
    /// 설계 원칙 (GDD §1.1): **게임 오버가 없다.**
    /// 눈덩이가 굴러 떨어져도 실패 화면은 뜨지 않는다. 손실은 시간과 크기뿐이다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("설정")]
        public SnowBallTuning tuning;

        [Header("참조 (비우면 자동으로 찾는다)")]
        public PlayerMotor player;
        public Snowball snowball;
        public PlayerGrab grab;

        [Header("수동 체크포인트 (F)")]
        [Tooltip("눈덩이가 굴러가는 중에도 체크포인트를 찍을 수 있게 할지.\n" +
                 "끄면 눈덩이가 거의 멈춰 있을 때만 저장된다 (굴러가는 중에 찍어두고 " +
                 "계속 되감는 꼼수 방지).")]
        public bool allowSaveWhileRolling = false;

        [Tooltip("위 옵션이 꺼져 있을 때, 이 속도 미만이어야 저장된다 (m/s).")]
        public float saveMaxSnowballSpeed = 0.8f;


        /// <summary>새 스테이지에 진입할 때. 인자는 스테이지 이름.</summary>
        public event Action<string> StageEntered;

        /// <summary>되감기가 일어났을 때.</summary>
        public event Action Respawned;

        /// <summary>체크포인트가 저장됐을 때. true 면 R 로 직접 찍은 것.</summary>
        public event Action<bool> CheckpointSaved;

        /// <summary>R 로 저장을 시도했지만 거절당했을 때 (눈덩이가 굴러가는 중 등).</summary>
        public event Action<string> CheckpointRejected;

        /// <summary>골인했을 때.</summary>
        public event Action<Ending> Finished;


        // ── 진행 상태 ──────────────────────────────
        public RespawnPoint CurrentPoint { get; private set; }
        public bool HasRespawnPoint { get; private set; }
        public string CurrentStageName { get; private set; }
        public int RespawnCount { get; private set; }
        public int ManualSaveCount { get; private set; }
        public float ElapsedTime { get; private set; }
        public bool IsFinished { get; private set; }
        public Ending Result { get; private set; }

        /// <summary>되감기 연출 중에는 입력/물리를 건드리지 않는다.</summary>
        public bool IsRespawning { get; private set; }

        // 지금까지 밟은 자동 체크포인트 중 가장 앞선 순서.
        // 수동 저장을 해도 이 값은 내려가지 않는다 — 뒤로 돌아가 다시 밟아도
        // 이미 지난 체크포인트가 현재 지점을 덮어쓰면 안 되기 때문.
        private int highestAutoOrder = -1;


        private void Awake()
        {
            Instance = this;

            if (player == null) player = FindAnyObjectByType<PlayerMotor>();
            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();
            if (grab == null && player != null) grab = player.GetComponent<PlayerGrab>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }


        private void Start()
        {
            // 시작 지점을 곧바로 되돌아갈 곳으로 잡아둔다.
            //
            // 이게 없으면 첫 자동 체크포인트를 밟기 전까지 되감기가 통째로 죽는다.
            // (코스 생성기가 눈덩이를 첫 체크포인트보다 **앞쪽**에 놓기 때문에
            //  트리거를 밟지 못한 채 게임이 시작된다)
            SeedFromCurrentPositions();
        }


        private void Update()
        {
            if (IsFinished) return;

            ElapsedTime += Time.deltaTime;

            if (IsRespawning) return;

            CheckFellTooFar();
            CheckManualKey();
        }


        // ==========================================
        // R = 즉시 재시작,  F = 체크포인트 저장
        // ==========================================

        private void CheckManualKey()
        {
            // 홀드가 아니라 탭이다.
            // 복구 비용이 0에 가까워야 "떨어짐"이 짜증이 아니라 흐름의 일부가 된다.
            if (InputBridge.RetryPressed) RequestRespawn();

            if (InputBridge.SaveCheckpointPressed) SaveManualCheckpoint();
        }


        /// <summary>지금 서 있는 자리를 돌아갈 지점으로 저장한다.</summary>
        public void SaveManualCheckpoint()
        {
            if (player == null || snowball == null) return;

            if (!allowSaveWhileRolling)
            {
                float speed = snowball.Body.linearVelocity.magnitude;

                if (speed > saveMaxSnowballSpeed)
                {
                    if (CheckpointRejected != null)
                    {
                        CheckpointRejected("눈덩이가 멈춰야 저장할 수 있다");
                    }
                    return;
                }
            }

            RespawnPoint point = new RespawnPoint();
            point.playerPosition = player.transform.position;
            point.snowballPosition = snowball.transform.position;
            point.stageName = CurrentStageName;
            point.order = -1;
            point.isManual = true;

            CurrentPoint = point;
            HasRespawnPoint = true;
            ManualSaveCount++;

            if (CheckpointSaved != null) CheckpointSaved(true);
        }


        private void SeedFromCurrentPositions()
        {
            if (player == null || snowball == null) return;

            RespawnPoint point = new RespawnPoint();
            point.playerPosition = player.transform.position;
            point.snowballPosition = snowball.transform.position;
            point.stageName = CurrentStageName;
            point.order = -1;
            point.isManual = false;

            CurrentPoint = point;
            HasRespawnPoint = true;
        }


        // ==========================================
        // 자동 체크포인트
        // ==========================================

        public void ReportCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint == null) return;

            // 뒤로 되돌아가 이미 지난 체크포인트를 다시 밟아도 현재 지점이 내려가지 않는다.
            if (checkpoint.order <= highestAutoOrder) return;

            highestAutoOrder = checkpoint.order;

            RespawnPoint point = new RespawnPoint();
            point.playerPosition = checkpoint.PlayerSpawnPoint;
            point.snowballPosition = checkpoint.SnowballSpawnPoint;
            point.stageName = checkpoint.stageName;
            point.order = checkpoint.order;
            point.isManual = false;

            CurrentPoint = point;
            HasRespawnPoint = true;

            if (CheckpointSaved != null) CheckpointSaved(false);

            if (!string.IsNullOrEmpty(checkpoint.stageName) &&
                checkpoint.stageName != CurrentStageName)
            {
                CurrentStageName = checkpoint.stageName;

                if (StageEntered != null) StageEntered(CurrentStageName);
            }
        }


        // ==========================================
        // 되감기
        // ==========================================

        private void CheckFellTooFar()
        {
            if (!HasRespawnPoint || snowball == null || tuning == null) return;

            float drop = CurrentPoint.snowballPosition.y - snowball.transform.position.y;

            if (drop >= tuning.fallRespawnDepth)
            {
                RequestRespawn();
            }
        }


        public void RequestRespawn()
        {
            if (IsRespawning || IsFinished) return;
            if (!HasRespawnPoint) return;

            StartCoroutine(RespawnRoutine());
        }


        private IEnumerator RespawnRoutine()
        {
            IsRespawning = true;
            RespawnCount++;

            float fade = tuning != null ? tuning.respawnFadeDuration : 0.6f;

            // 페이드 아웃 자리. 지금은 그레이박스라 그냥 기다린다.
            yield return new WaitForSeconds(fade * 0.5f);

            TeleportToPoint();

            yield return new WaitForSeconds(fade * 0.5f);

            IsRespawning = false;

            if (Respawned != null) Respawned();
        }


        private void TeleportToPoint()
        {
            if (snowball != null)
            {
                Rigidbody ballBody = snowball.Body;

                ballBody.position = CurrentPoint.snowballPosition;
                ballBody.rotation = Quaternion.identity;
                ballBody.linearVelocity = Vector3.zero;
                ballBody.angularVelocity = Vector3.zero;

                // 크기는 그대로 둔다 — GDD §6.
            }

            if (player != null)
            {
                Rigidbody playerBody = player.GetComponent<Rigidbody>();

                playerBody.position = CurrentPoint.playerPosition;
                playerBody.linearVelocity = Vector3.zero;
                playerBody.angularVelocity = Vector3.zero;
            }

            // 되감았으면 악력은 채워준다. 벌을 두 번 줄 이유가 없다.
            if (grab != null && grab.grip != null) grab.grip.RefillFull();
        }


        // ==========================================
        // 골인
        // ==========================================

        public void ReachGoal()
        {
            if (IsFinished) return;

            IsFinished = true;
            Result = EvaluateEnding();

            if (Finished != null) Finished(Result);
        }


        private Ending EvaluateEnding()
        {
            if (snowball == null || tuning == null) return Ending.B_SmallHead;

            float d = snowball.Diameter;

            if (d >= tuning.targetDiameter) return Ending.A_Complete;
            if (d >= 1.0f) return Ending.B_SmallHead;

            return Ending.C_AlmostMelted;
        }


        public static string DescribeEnding(Ending ending)
        {
            switch (ending)
            {
                case Ending.A_Complete:
                    return "머리가 딱 맞게 올라갔다.";
                case Ending.B_SmallHead:
                    return "머리가 조금 작다. 그래도 눈사람이다.";
                case Ending.C_AlmostMelted:
                    return "거의 다 녹았다. 남은 눈을 뭉쳐 올렸다.";
                default:
                    return "";
            }
        }
    }
}
