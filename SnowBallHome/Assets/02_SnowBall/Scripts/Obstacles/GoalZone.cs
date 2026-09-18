using UnityEngine;

namespace SnowBall
{
    /// <summary>집 앞 골 지점. 눈덩이가 들어오면 끝난다.</summary>
    [RequireComponent(typeof(Collider))]
    public class GoalZone : MonoBehaviour
    {
        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }


        private void OnTriggerEnter(Collider other)
        {
            Snowball ball = other.GetComponentInParent<Snowball>();
            if (ball == null) return;

            if (GameManager.Instance != null) GameManager.Instance.ReachGoal();
        }


        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.35f);

            Collider col = GetComponent<Collider>();
            if (col != null) Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
