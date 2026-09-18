using UnityEngine;

namespace SnowBall
{
    /// <summary>마을 아이가 던지는 작은 눈덩이. 맞으면 경로가 틀어지고 조금 깎인다.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ThrownSnowball : MonoBehaviour
    {
        [Tooltip("눈덩이에 주는 충격량 (N·s)")]
        public float impulse = 8f;

        [Tooltip("맞아도 안 사라지는 일이 없게 하는 안전장치 (초)")]
        public float lifetime = 6f;


        private Rigidbody rb;
        private bool consumed;


        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            Destroy(gameObject, lifetime);
        }


        public void Launch(Vector3 velocity)
        {
            rb.linearVelocity = velocity;
        }


        private void OnCollisionEnter(Collision collision)
        {
            if (consumed) return;
            consumed = true;

            Snowball ball = collision.gameObject.GetComponentInParent<Snowball>();

            if (ball != null)
            {
                if (ball.tuning != null) ball.Damage(ball.tuning.damageOnThrownSnowball);

                Vector3 direction = rb.linearVelocity;
                direction.y = 0f;

                if (direction.sqrMagnitude > 0.0001f)
                {
                    ball.Body.AddForce(direction.normalized * impulse, ForceMode.Impulse);
                }
            }

            Destroy(gameObject);
        }
    }
}
