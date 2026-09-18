using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 눈덩이가 지나가면 활성화되는 체크포인트.
    ///
    /// 되감기 규칙 (GDD §6): 되감아도 **눈덩이 크기는 유지된다.**
    /// 성장까지 되돌리면 벌이 너무 무거워서, 실수 한 번에 판 전체가 망가진다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("진행 순서. 뒤로 돌아가도 더 낮은 번호로는 내려가지 않는다.")]
        public int order;

        [Tooltip("이 체크포인트가 속한 스테이지 이름 (HUD 배너용)")]
        public string stageName = "";

        [Tooltip("되감기 때 아이가 설 위치. 비워두면 이 오브젝트 위치.")]
        public Transform playerSpawn;

        [Tooltip("되감기 때 눈덩이가 놓일 위치. 비워두면 이 오브젝트 앞쪽.")]
        public Transform snowballSpawn;

        public bool Activated { get; private set; }

        public Vector3 PlayerSpawnPoint
        {
            get
            {
                if (playerSpawn != null) return playerSpawn.position;
                return transform.position;
            }
        }

        public Vector3 SnowballSpawnPoint
        {
            get
            {
                if (snowballSpawn != null) return snowballSpawn.position;
                return transform.position + transform.forward * 1.6f + Vector3.up * 0.6f;
            }
        }


        private void Reset()
        {
            // 편의상 트리거로 만들어 둔다.
            Collider col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }


        private void OnTriggerEnter(Collider other)
        {
            Snowball ball = other.GetComponentInParent<Snowball>();
            if (ball == null) return;

            Activated = true;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.ReportCheckpoint(this);
            }
        }


        private void OnDrawGizmos()
        {
            Gizmos.color = Activated ? Color.green : new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(transform.position, new Vector3(4f, 3f, 0.5f));

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(SnowballSpawnPoint, 0.4f);
        }
    }
}
