using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// ④ 강아지 구간.
    ///
    /// 강아지는 **적이 아니라 신난 것**이다. 눈덩이 주위를 맴돌다 가끔 몸으로 들이받는다.
    ///
    /// 설계 원칙 (GDD §1.1): 실패의 원인은 항상 보였어야 한다.
    ///   → 들이받기 전에 반드시 1.2초간 자세를 낮추는 **예비 동작**을 한다.
    ///     화면 밖에서 갑자기 당하는 일이 없어야 한다.
    ///
    /// 아이가 다가가면 도망간다. 쫓아 보내면 잠깐 평화가 오지만,
    /// 그동안 눈덩이에서 손을 떼야 한다 — 딜레마의 또 다른 변주다.
    ///
    /// NavMesh 없이 단순 이동으로 동작한다 (그레이박스 단계).
    /// </summary>
    public class DogAgent : MonoBehaviour
    {
        private enum State { Circling, Telegraphing, Charging, Recovering, Fleeing }

        [Header("대상 (비우면 자동으로 찾는다)")]
        public Snowball snowball;
        public Transform player;

        [Header("이동")]
        public float moveSpeed = 4.5f;
        public float circleRadius = 3.2f;
        public float circleSpeed = 70f;

        [Header("들이받기")]
        [Tooltip("다음 들이받기까지의 간격 (초). 이 범위에서 랜덤.")]
        public Vector2 chargeInterval = new Vector2(4f, 7f);

        [Tooltip("자세를 낮추고 짖는 예비 동작 시간 (초). 예고 없이 치면 안 된다.")]
        public float telegraphDuration = 1.2f;

        [Tooltip("들이받는 힘 (N)")]
        public float chargeForce = 60f;

        public float chargeDuration = 0.8f;
        public float recoverDuration = 1.0f;

        [Header("회피")]
        [Tooltip("아이가 이 거리 안으로 오면 도망간다 (m)")]
        public float fleeTriggerDistance = 2.5f;

        public float fleeDuration = 3f;
        public float fleeSpeed = 6f;


        private State state = State.Circling;
        private float stateTimer;
        private float nextChargeTime;
        private float circleAngle;
        private Vector3 chargeDirection;


        private void Awake()
        {
            if (snowball == null) snowball = FindAnyObjectByType<Snowball>();

            if (player == null)
            {
                PlayerMotor motor = FindAnyObjectByType<PlayerMotor>();
                if (motor != null) player = motor.transform;
            }

            circleAngle = Random.Range(0f, 360f);
            ScheduleNextCharge();
        }


        private void Update()
        {
            if (snowball == null) return;

            stateTimer -= Time.deltaTime;

            if (ShouldFlee() && state != State.Fleeing) EnterFlee();

            switch (state)
            {
                case State.Circling: TickCircling(); break;
                case State.Telegraphing: TickTelegraphing(); break;
                case State.Charging: TickCharging(); break;
                case State.Recovering: TickRecovering(); break;
                case State.Fleeing: TickFleeing(); break;
            }
        }


        // ==========================================
        // 상태별 처리
        // ==========================================

        private void TickCircling()
        {
            circleAngle += circleSpeed * Time.deltaTime;

            Vector3 offset = Quaternion.Euler(0f, circleAngle, 0f) * Vector3.forward * circleRadius;
            Vector3 goal = snowball.transform.position + offset;

            MoveToward(goal, moveSpeed);
            FaceToward(snowball.transform.position);

            if (Time.time >= nextChargeTime) EnterTelegraph();
        }


        private void TickTelegraphing()
        {
            // 자세를 낮추고 눈덩이를 노려본다. 여기서 플레이어가 대비할 수 있어야 한다.
            FaceToward(snowball.transform.position);

            Vector3 scale = transform.localScale;
            scale.y = Mathf.Lerp(scale.y, 0.65f, Time.deltaTime * 8f);
            transform.localScale = scale;

            if (stateTimer <= 0f) EnterCharge();
        }


        private void TickCharging()
        {
            RestoreScale();

            transform.position += chargeDirection * moveSpeed * 1.8f * Time.deltaTime;
            FaceToward(transform.position + chargeDirection);

            if (stateTimer <= 0f) EnterRecover();
        }


        private void TickRecovering()
        {
            RestoreScale();

            if (stateTimer <= 0f)
            {
                state = State.Circling;
                ScheduleNextCharge();
            }
        }


        private void TickFleeing()
        {
            RestoreScale();

            if (player != null)
            {
                Vector3 away = transform.position - player.position;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    Vector3 goal = transform.position + away.normalized * 2f;
                    MoveToward(goal, fleeSpeed);
                    FaceToward(goal);
                }
            }

            if (stateTimer <= 0f)
            {
                state = State.Circling;
                ScheduleNextCharge();
            }
        }


        // ==========================================
        // 상태 전이
        // ==========================================

        private void EnterTelegraph()
        {
            state = State.Telegraphing;
            stateTimer = telegraphDuration;
        }

        private void EnterCharge()
        {
            state = State.Charging;
            stateTimer = chargeDuration;

            chargeDirection = snowball.transform.position - transform.position;
            chargeDirection.y = 0f;

            if (chargeDirection.sqrMagnitude < 0.0001f) chargeDirection = transform.forward;
            else chargeDirection.Normalize();
        }

        private void EnterRecover()
        {
            state = State.Recovering;
            stateTimer = recoverDuration;
        }

        private void EnterFlee()
        {
            state = State.Fleeing;
            stateTimer = fleeDuration;

            RestoreScale();
        }


        private void ScheduleNextCharge()
        {
            nextChargeTime = Time.time + Random.Range(chargeInterval.x, chargeInterval.y);
        }


        private bool ShouldFlee()
        {
            if (player == null) return false;

            return Vector3.Distance(transform.position, player.position) < fleeTriggerDistance;
        }


        // ==========================================
        // 이동 보조
        // ==========================================

        private void MoveToward(Vector3 goal, float speed)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);
            next.y = transform.position.y;

            transform.position = next;
        }

        private void FaceToward(Vector3 point)
        {
            Vector3 dir = point - transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir, Vector3.up),
                Time.deltaTime * 10f
            );
        }

        private void RestoreScale()
        {
            Vector3 scale = transform.localScale;
            scale.y = Mathf.Lerp(scale.y, 1f, Time.deltaTime * 8f);
            transform.localScale = scale;
        }


        // ==========================================
        // 실제 충격
        // ==========================================

        private void OnTriggerEnter(Collider other)
        {
            if (state != State.Charging) return;

            Snowball ball = other.GetComponentInParent<Snowball>();
            if (ball == null) return;

            // 옆에서 밀어야 경로가 틀어진다. 정면으로 밀면 그냥 도와주는 꼴이 된다.
            Vector3 push = chargeDirection;
            push.y = 0f;

            ball.Body.AddForce(push.normalized * chargeForce, ForceMode.Impulse);

            EnterRecover();
        }


        private void OnDrawGizmosSelected()
        {
            if (snowball != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(snowball.transform.position, circleRadius);
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, fleeTriggerDistance);
        }
    }
}
