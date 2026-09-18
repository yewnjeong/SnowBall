using UnityEngine;

namespace SnowBall
{
    public enum SurfaceType
    {
        Snow,   // 기본. 마찰이 있고, 눈덩이가 여기서만 커진다.
        Ice,    // 빙판. 아이도 미끄러진다.
        Wood,   // 나무 판자, 다리
        Rock    // 바위. 눈덩이가 크게 깎인다.
    }

    /// <summary>
    /// 콜라이더에 붙여서 "이 바닥은 무슨 재질인가"를 알려주는 표식.
    /// 붙어 있지 않은 바닥은 전부 Snow 로 간주한다.
    /// </summary>
    public class SurfaceTag : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.Snow;
    }


    /// <summary>발밑 판정 결과.</summary>
    public struct GroundInfo
    {
        public bool grounded;
        public Vector3 point;
        public Vector3 normal;

        /// <summary>지면 기울기 (도). 평지면 0.</summary>
        public float slopeDegrees;

        public SurfaceType surface;
        public Collider collider;

        public static GroundInfo None
        {
            get
            {
                GroundInfo g = new GroundInfo();
                g.grounded = false;
                g.normal = Vector3.up;
                g.surface = SurfaceType.Snow;
                return g;
            }
        }
    }


    /// <summary>
    /// 발밑 지면을 구체로 훑어서 재질과 경사를 알아낸다.
    ///
    /// 왜 충돌 이벤트(OnCollisionEnter/Exit)를 안 쓰는가:
    ///   빙판 타일 A 에서 B 로 넘어갈 때 Enter(B) 와 Exit(A) 가 연달아 들어오는데,
    ///   Exit(A) 가 나중에 처리되면 "빙판 위인데 아니라고 판정"되어 깜빡인다.
    ///   매 FixedUpdate 마다 직접 훑으면 이 문제가 구조적으로 사라지고,
    ///   덤으로 경사각(악력 소모 계산에 필요)까지 같이 얻는다.
    /// </summary>
    public static class GroundSampler
    {
        // SphereCast 결과를 담을 버퍼. 매 프레임 할당하지 않으려고 재사용한다.
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];

        public static GroundInfo Sample(Transform owner, Vector3 origin, float radius,
                                        float distance, LayerMask mask)
        {
            GroundInfo best = GroundInfo.None;
            float bestDistance = float.MaxValue;

            int count = Physics.SphereCastNonAlloc(
                origin,
                radius,
                Vector3.down,
                HitBuffer,
                distance,
                mask,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];

                if (hit.collider == null) continue;

                // 자기 자신(또는 자식)은 바닥이 아니다.
                if (owner != null && hit.collider.transform.IsChildOf(owner)) continue;

                // 움직이는 물체는 바닥이 아니다.
                //
                // 이게 없으면 아이 발밑 구체가 **바로 앞의 눈덩이**를 바닥으로 잡아버린다.
                // 그러면 ground.normal 이 구체 표면의 법선이 되어,
                // 경사 계산도 틀리고 경사면 투영 이동도 엉뚱한 방향으로 나간다.
                // 지형은 전부 정적(Rigidbody 없음)이므로 이 한 줄로 깔끔하게 걸러진다.
                if (hit.collider.attachedRigidbody != null) continue;

                // SphereCast 가 시작 지점에서 이미 겹쳐 있으면 distance 가 0,
                // normal 이 쓰레기값으로 온다. 이건 버린다.
                if (hit.distance <= 0f) continue;

                if (hit.distance >= bestDistance) continue;

                bestDistance = hit.distance;

                best.grounded = true;
                best.point = hit.point;
                best.normal = hit.normal;
                best.slopeDegrees = Vector3.Angle(hit.normal, Vector3.up);
                best.collider = hit.collider;
                best.surface = SurfaceOf(hit.collider);
            }

            return best;
        }

        public static SurfaceType SurfaceOf(Collider col)
        {
            if (col == null) return SurfaceType.Snow;

            SurfaceTag tag = col.GetComponentInParent<SurfaceTag>();
            if (tag != null) return tag.surface;

            return SurfaceType.Snow;
        }
    }
}
