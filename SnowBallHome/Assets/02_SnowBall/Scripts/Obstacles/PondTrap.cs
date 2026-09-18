using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 얼어붙은 계곡의 연못. 빠지면 크게 깎이고 체크포인트로 되감긴다.
    /// 게임에서 가장 아픈 벌이라, 빙판 구간의 긴장을 만드는 핵심 장치다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PondTrap : MonoBehaviour
    {
        [Tooltip("아이만 빠졌을 때도 되감을지. 끄면 눈덩이가 빠질 때만 되감는다.")]
        public bool respawnOnPlayerFall = true;


        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }


        private void OnTriggerEnter(Collider other)
        {
            GameManager game = GameManager.Instance;
            if (game == null || game.IsRespawning) return;

            Snowball ball = other.GetComponentInParent<Snowball>();

            if (ball != null)
            {
                if (ball.tuning != null) ball.Damage(ball.tuning.damageOnPond);

                game.RequestRespawn();
                return;
            }

            if (!respawnOnPlayerFall) return;

            PlayerMotor player = other.GetComponentInParent<PlayerMotor>();
            if (player != null) game.RequestRespawn();
        }


        private void OnDrawGizmos()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
