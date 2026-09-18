using System;
using UnityEngine;

namespace SnowBall
{
    /// <summary>
    /// 스테이지 하나의 설계 수치. Docs/GDD.md §5 의 표를 그대로 옮긴 것.
    /// </summary>
    [Serializable]
    public class StageDefinition
    {
        [Header("기본")]
        public string stageName = "스테이지";

        [Tooltip("비탈을 따라간 길이 (m)")]
        public float length = 60f;

        [Tooltip("평균 경사 (도). 클수록 눈덩이가 세게 굴러 내려온다.")]
        public float slopeDegrees = 6f;

        [Tooltip("통로 폭 (m)")]
        public float width = 6f;

        [Tooltip("스테이지 전체에 걸친 좌우 굽이 총량 (도). 0이면 직선.")]
        public float turnDegrees = 0f;

        [Tooltip("이 스테이지의 체크포인트 개수 (시작점 포함).\n" +
                 "촘촘할수록 한 번 실수에 잃는 시간이 짧아진다. 20~25m 에 하나가 기준.")]
        public int checkpointCount = 1;

        [Tooltip("길 양옆에 난간을 세울지.\n" +
                 "끄면 낭떠러지가 되어 눈덩이가 실제로 떨어질 수 있다.\n" +
                 "양쪽 다 막아두면 떨어질 일이 없어서 긴장이 통째로 사라진다 —\n" +
                 "조작을 배우는 ① 구간 정도에만 켜두는 것이 좋다.")]
        public bool rails = false;

        [Header("장애물")]
        public int treeCount;
        public int rockCount;

        [Tooltip("빙판으로 바꿀 구간 수")]
        public int icePatchCount;

        [Tooltip("길 옆에 파놓을 연못 수")]
        public int pondCount;

        public int dogCount;
        public int villageKidCount;

        [Tooltip("강풍 구역을 둘지")]
        public bool gustZone;

        [Header("풍경")]
        [Tooltip("길 바깥에 세울 장식 나무/바위의 밀도 배수. 0이면 이 구간엔 장식 없음.")]
        public float decorDensity = 1f;

        [Tooltip("길 옆에 마을 집을 세울지 (⑤ 마을용)")]
        public bool villageHouses;
    }


    /// <summary>
    /// 코스 전체의 설계도. 에디터 메뉴에서 이 에셋을 읽어 그레이박스를 통째로 생성한다.
    ///
    /// 밸런싱은 여기 숫자만 바꾸고 다시 생성하면 된다.
    /// 씬에서 손으로 옮긴 것은 재생성 때 날아가므로, 확정된 수치는 꼭 여기에 반영해둘 것.
    /// </summary>
    [CreateAssetMenu(fileName = "CourseDefinition", menuName = "SnowBall/Course Definition")]
    public class CourseDefinition : ScriptableObject
    {
        [Header("코스")]
        public StageDefinition[] stages;

        // ==========================================
        // 타워 — 건물처럼 감고 올라가는 구조
        // ==========================================

        [Header("타워")]
        [Tooltip("길을 나선으로 감아 올린다. 끄면 예전처럼 한 줄로 쭉 뻗는다.")]
        public bool spiralTower = true;

        [Tooltip("맨 아래층의 타워 반지름 (m)")]
        public float towerBaseRadius = 22f;

        [Tooltip("맨 위층의 타워 반지름 (m).\n" +
                 "\n" +
                 "**이 게임에서 가장 중요한 수치 중 하나다.**\n" +
                 "반지름이 위로 갈수록 줄어들면, 아래층이 위층보다 바깥으로 더 튀어나온다.\n" +
                 "그래서 바깥으로 굴러 떨어져도 허공이 아니라 '한 바퀴 아래 길'에 착지한다.\n" +
                 "\n" +
                 "한 바퀴당 줄어드는 양이 길 폭보다 크면 그 받침이 사라지므로,\n" +
                 "base 와 top 의 차이를 너무 크게 두지 말 것.")]
        public float towerTopRadius = 10f;

        [Tooltip("안쪽(타워 중심 쪽) 벽 높이 (m). 중심으로는 떨어질 수 없게 막는다.\n" +
                 "0이면 벽 없음.")]
        public float towerInnerWallHeight = 2.5f;


        [Header("길 생성")]
        [Tooltip("길을 잘게 쪼갤 단위 (m). 작을수록 굽이가 매끄럽지만 오브젝트가 많아진다.")]
        public float segmentLength = 6f;

        [Tooltip("바닥 판의 두께 (m)")]
        public float floorThickness = 0.6f;

        [Tooltip("길 양옆 난간 높이 (m). 0이면 난간 없음 — 떨어지기 쉬워진다.")]
        public float railHeight = 0.7f;

        public float railWidth = 0.35f;

        [Tooltip("구간마다 경사를 이만큼 흔든다 (도). 0이면 밋밋한 일정 비탈.\n" +
                 "\n" +
                 "보기 좋으라고 넣은 값이 아니다. 일정한 비탈에서는 놓친 눈덩이가\n" +
                 "늘 똑같이 굴러가서 쫓아갈 여지가 없다. 기복이 있으면 가파른 데서 확 가속하고\n" +
                 "완만한 데서 늦춰져, 따라잡아 낚아챌 수 있는 순간이 생긴다.")]
        public float slopeVariance = 8f;

        [Tooltip("더 이상 쓰이지 않는다.\n" +
                 "\n" +
                 "예전에는 바닥이 박스라서 이음매에 틈이 생겼고, 판을 겹쳐 메웠다.\n" +
                 "그런데 겹친 판의 끝이 다음 판 위로 솟아올라 턱이 되었고,\n" +
                 "눈덩이가 거기 걸려 저절로 멈췄다.\n" +
                 "지금은 이웃 구간이 모서리를 공유하는 연속 메시라 틈도 턱도 없다.")]
        public float segmentOverlap = 1f;

        [Tooltip("생성에 쓸 난수 시드. 같은 시드면 같은 코스가 나온다.")]
        public int randomSeed = 20260918;


        // ==========================================
        // 복구 루트 — 떨어져도 스스로 돌아올 수 있게
        // ==========================================

        [Header("복구 루트")]
        [Tooltip("길 아래에 넓은 복구 지대를 깐다.\n" +
                 "\n" +
                 "기본적으로 꺼져 있다. Get To Work 가 복구를 쾌적하게 만든 방법은\n" +
                 "'떨어져도 안 죽게' 가 아니라 '재시도를 공짜로' 였기 때문이다.\n" +
                 "안전 지대를 깔면 떨어져도 안 죽는 대신, 걸어서 되돌아가는 지루함이 생긴다.\n" +
                 "\n" +
                 "켜고 싶다면 R 즉시 재시작과 함께 쓰지 말 것 — 둘 중 하나면 충분하다.")]
        public bool buildRecoveryApron = false;

        [Tooltip("복구 지대의 전체 폭 (m). 길 폭보다 훨씬 넓어야 의미가 있다.")]
        public float apronWidth = 22f;

        [Tooltip("복구 지대가 길보다 얼마나 아래인지 (m).\n" +
                 "너무 깊으면 램프가 가팔라지고, 너무 얕으면 떨어진 느낌이 안 난다.")]
        public float apronDrop = 2.8f;

        [Tooltip("몇 구간마다 복구 램프를 놓을지. 작을수록 복구가 쉬워진다.")]
        public int rampEverySegments = 5;

        [Tooltip("복구 램프의 폭 (m)")]
        public float rampWidth = 4f;

        [Tooltip("복구 램프가 옆으로 뻗는 거리 (m). 클수록 완만해진다.")]
        public float rampRun = 6f;

        [Tooltip("복구 지대 바깥을 벽으로 막는다. 끄면 세상 밖으로 떨어질 수 있다.")]
        public bool buildBoundaryWalls = true;

        public float boundaryWallHeight = 4f;


        // ==========================================
        // 풍경
        // ==========================================

        [Header("풍경")]
        [Tooltip("길 바깥에 장식용 나무·바위·눈더미를 세운다. 충돌 피해는 없다.")]
        public bool buildDecor = true;

        [Tooltip("한 구간당 장식 개수 (stage 의 decorDensity 가 곱해진다)")]
        public float decorPerSegment = 2.5f;

        [Tooltip("복구 지대 바깥에 세울 원경 봉우리 — 실루엣용")]
        public bool buildDistantPeaks = true;

        [Tooltip("안개와 하늘색을 겨울 톤으로 맞춘다")]
        public bool applyAtmosphere = true;


        /// <summary>GDD §5 의 기본 6스테이지 구성.</summary>
        public void ResetToGddDefaults()
        {
            spiralTower = true;
            towerBaseRadius = 22f;
            towerTopRadius = 10f;
            towerInnerWallHeight = 2.5f;

            // 나선은 구간마다 방향이 꺾이므로, 직선일 때보다 잘게 쪼개야 매끄럽다.
            segmentLength = 3.5f;
            floorThickness = 0.6f;
            railHeight = 0.7f;
            railWidth = 0.35f;
            slopeVariance = 5f;
            segmentOverlap = 1f;
            randomSeed = 20260918;

            // 안전 지대는 기본으로 끈다. 떨어짐은 막지 않고, 복구를 공짜로 만든다.
            buildRecoveryApron = false;
            apronWidth = 22f;
            apronDrop = 2.8f;
            rampEverySegments = 5;
            rampWidth = 4f;
            rampRun = 6f;
            buildBoundaryWalls = false;
            boundaryWallHeight = 4f;

            buildDecor = true;
            decorPerSegment = 2.5f;
            buildDistantPeaks = true;
            applyAtmosphere = true;

            stages = new StageDefinition[]
            {
                // 체크포인트는 대략 20~25m 에 하나.
                // 한 번 실수로 잃는 시간이 20~30초를 넘지 않게 하는 것이 목표다.

                // 미니게임 규모. 한 섹션이 30~45초, 전체가 4~6분이 목표다.
                // 섹션마다 성격이 뚜렷해야 하므로 길이보다 '무엇이 있는가'가 중요하다.

                new StageDefinition
                {
                    stageName = "① 눈밭",
                    length = 32f, slopeDegrees = 6f, width = 7f,
                    checkpointCount = 2,
                    rails = true,   // 조작 배우는 섹션이라 여기만 막아둔다
                    decorDensity = 0.6f
                },

                new StageDefinition
                {
                    stageName = "② 숲",
                    length = 42f, slopeDegrees = 14f, width = 5f,
                    checkpointCount = 2,
                    treeCount = 8, rockCount = 2,
                    decorDensity = 2.2f
                },

                new StageDefinition
                {
                    stageName = "③ 얼어붙은 계곡",
                    length = 40f, slopeDegrees = 11f, width = 6f,
                    checkpointCount = 2,
                    icePatchCount = 5, pondCount = 2, rockCount = 2,
                    decorDensity = 0.9f
                },

                new StageDefinition
                {
                    stageName = "④ 강아지 구간",
                    length = 30f, slopeDegrees = 9f, width = 7f,
                    checkpointCount = 2,
                    dogCount = 1, treeCount = 2,
                    decorDensity = 1.2f
                },

                new StageDefinition
                {
                    stageName = "⑤ 마을",
                    length = 38f, slopeDegrees = 12f, width = 6.5f,
                    checkpointCount = 2,
                    villageKidCount = 3, rockCount = 3,
                    decorDensity = 0.7f, villageHouses = true
                },

                new StageDefinition
                {
                    stageName = "⑥ 마지막 산길",
                    length = 55f, slopeDegrees = 20f, width = 4.5f,
                    checkpointCount = 3,
                    treeCount = 5, rockCount = 2, icePatchCount = 3, pondCount = 1,
                    gustZone = true,
                    decorDensity = 1.6f
                }
            };
        }


        private void Reset()
        {
            ResetToGddDefaults();
        }
    }
}
