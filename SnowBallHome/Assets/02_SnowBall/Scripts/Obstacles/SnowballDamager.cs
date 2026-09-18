using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 부딪히면 눈덩이를 깎는 장애물. 나무·바위 모두 이걸 쓰고 수치만 다르게 준다.
    ///
    /// 여기서 플레이어가 배우는 것: **빨리 가려다 손해 본다.**
    /// 깎인 만큼 엔딩 조건에서 멀어지므로, 속도와 크기를 저울질하게 된다.
    /// </summary>
    public class SnowballDamager : MonoBehaviour
    {
        public enum Kind { Tree, Rock, Custom }

        [Header("설정")]
        public Kind kind = Kind.Tree;

        [Tooltip("Custom 일 때만 쓰는 값. Tree/Rock 은 Tuning 에서 가져온다.")]
        public float customDamage = 0.05f;

        [Tooltip("이 속도 미만으로 살짝 스치면 깎지 않는다 (m/s)")]
        public float minimumImpactSpeed = 1.2f;

        [Tooltip("연달아 깎이지 않도록 하는 쿨다운 (초)")]
        public float cooldown = 0.5f;

        [Header("반응")]
        [Tooltip("맞았을 때 눈덩이를 살짝 튕겨낼 힘. 0이면 물리에만 맡긴다.")]
        public float knockback = 0f;


        private float nextAllowedTime;


        private void OnCollisionEnter(Collision collision)
        {
            Snowball ball = collision.gameObject.GetComponentInParent<Snowball>();
            if (ball == null) return;

            if (Time.time < nextAllowedTime) return;

            // 굴러와서 부딪혀야 깎인다. 벽에 기대 있는 상태로는 깎이지 않는다.
            if (collision.relativeVelocity.magnitude < minimumImpactSpeed) return;

            nextAllowedTime = Time.time + cooldown;

            ball.Damage(ResolveDamage(ball.tuning));

            if (knockback > 0f)
            {
                Vector3 away = ball.transform.position - transform.position;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    ball.Body.AddForce(away.normalized * knockback, ForceMode.Impulse);
                }
            }
        }


        private float ResolveDamage(SnowBallTuning tuning)
        {
            if (tuning == null) return customDamage;

            switch (kind)
            {
                case Kind.Tree: return tuning.damageOnTree;
                case Kind.Rock: return tuning.damageOnRock;
                default: return customDamage;
            }
        }
    }
}
