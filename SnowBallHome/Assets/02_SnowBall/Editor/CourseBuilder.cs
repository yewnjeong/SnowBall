using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowBall.EditorTools
{
    /// <summary>
    /// CourseDefinition 을 읽어 그레이박스 코스를 통째로 생성한다.
    ///
    /// 왜 씬을 손으로 안 만드는가:
    ///   이 게임의 재미는 전부 **경사·폭·마찰 수치**에서 나온다.
    ///   손으로 배치하면 "경사를 2도 낮춰볼까"를 시도하는 데 한나절이 걸리지만,
    ///   데이터에서 생성하면 숫자 하나 고치고 다시 누르면 끝난다.
    ///   밸런싱 반복 속도가 곧 게임의 완성도다.
    ///
    /// 지형 단면 (스테이지를 가로로 자른 모습):
    ///
    ///     벽│   장식     램프╱  난간│  길  │난간  ╲램프    장식    │벽
    ///       │▁▁▁▁▁▁▁▁▁▁▁╱     └──────┘     ╲▁▁▁▁▁▁▁▁▁▁▁│
    ///       │        복구 지대(에이프런)  ▲  복구 지대        │
    ///       └────────────────── 여기서 벗어날 수 없다 ────────┘
    ///
    /// 난간 밖으로 굴러 떨어져도 허공이 아니라 복구 지대에 떨어지고,
    /// 일정 간격의 램프로 길에 다시 올릴 수 있다. 맨 아래는 벽으로 막혀 있다.
    /// → 어디까지 굴러가도 **되감기 없이 스스로 복구할 수 있다.**
    ///
    /// 메뉴: SnowBall > 코스 생성 (그레이박스)
    /// </summary>
    public static class CourseBuilder
    {
        private const string RootName = "== SnowBall Course ==";
        private const string BaseFolder = "Assets/02_SnowBall";

        private const string TuningPath = BaseFolder + "/Tuning/SnowBallTuning.asset";
        private const string CoursePath = BaseFolder + "/Tuning/CourseDefinition.asset";
        private const string MaterialFolder = BaseFolder + "/Materials";
        private const string PrefabFolder = BaseFolder + "/Prefabs";


        // 생성 중 쓰는 재료들
        private static SnowBallTuning tuning;
        private static CourseDefinition course;
        private static Materials mats;
        private static ThrownSnowball projectilePrefab;
        private static int checkpointOrder;
        private static int globalSegmentIndex;

        /// <summary>0보다 크면 앞에서 이만큼의 스테이지만 만든다. 튜닝용.</summary>
        private static int stageLimit;

        /// <summary>
        /// 스테이지 길이에 곱하는 배율. 1 이면 설계 그대로.
        ///
        /// 연습용 코스에서 쓴다. 길이만 줄이고 장애물 개수를 그대로 두면
        /// 밀도가 배로 뛰어 원래 설계와 다른 구간이 되므로, 개수와 굽이도 같이 줄인다.
        /// </summary>
        private static float stageScale = 1f;

        // 나선 진행 상태
        private static float spiralAngle;
        private static float spiralHeight;
        private static int totalSegmentsPlanned;

        // 섹션(= 층)별 반지름. 층마다 계단식으로 줄어든다.
        private static float sectionRadius;
        private static float previousSectionRadius;


        /// <summary>압축 배율이 적용된 장애물 개수. 원래 0이 아니었다면 최소 1개는 남긴다.</summary>
        private static int ScaleCount(int original)
        {
            if (original <= 0) return 0;
            if (stageScale >= 1f) return original;

            return Mathf.Max(1, Mathf.RoundToInt(original * stageScale));
        }


        private class Materials
        {
            public Material snow, apron, ice, rock, tree, rail, ball, player,
                            dog, kid, house, pond, aim, wall, peak, ramp;
        }


        private struct PathNode
        {
            public Vector3 position;    // 구간 시작점 (길 표면)
            public Quaternion rotation; // 경사 + 방향
            public float width;
            public Transform segment;
            public GameObject floor;
            public GameObject railLeft;
            public GameObject railRight;
        }


        // ==========================================
        // 메뉴
        // ==========================================

        [MenuItem("SnowBall/코스 생성 (그레이박스)", false, 0)]
        public static void BuildCourse()
        {
            tuning = LoadOrCreateTuning();
            course = LoadOrCreateCourse();

            if (course.stages == null || course.stages.Length == 0)
            {
                EditorUtility.DisplayDialog("SnowBall",
                    "CourseDefinition 에 스테이지가 없습니다.\n" +
                    "에셋을 선택하고 Inspector 우상단 메뉴 > Reset 을 누르면 기본값이 들어갑니다.",
                    "확인");
                return;
            }

            mats = EnsureMaterials();
            projectilePrefab = EnsureProjectilePrefab();

            RemoveExistingCourse();

            Random.InitState(course.randomSeed);
            checkpointOrder = 0;
            globalSegmentIndex = 0;

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build SnowBall Course");

            List<PathNode> allNodes = new List<PathNode>();
            Vector3 cursor = Vector3.zero;
            float baseYaw = 0f;

            int stageCount = stageLimit > 0
                ? Mathf.Min(stageLimit, course.stages.Length)
                : course.stages.Length;

            // 나선은 전체 진행도를 알아야 반지름을 줄여갈 수 있으므로 먼저 세어둔다.
            spiralAngle = 0f;
            spiralHeight = 0f;
            totalSegmentsPlanned = 0;

            for (int i = 0; i < stageCount; i++)
            {
                float len = course.stages[i].length * stageScale;
                totalSegmentsPlanned += Mathf.Max(3,
                    Mathf.RoundToInt(len / Mathf.Max(1f, course.segmentLength)));
            }

            previousSectionRadius = course.towerBaseRadius;

            for (int i = 0; i < stageCount; i++)
            {
                StageDefinition stage = course.stages[i];

                GameObject stageRoot = new GameObject(stage.stageName);
                stageRoot.transform.SetParent(root.transform, false);

                if (course.spiralTower)
                {
                    // 섹션마다 반지름이 한 단계씩 줄어든다.
                    // 안쪽으로 들어갈수록 아래 섹션이 바깥으로 남으므로,
                    // 떨어지면 바닥까지 가지 않고 **자기 섹션 바닥**에 걸린다.
                    float t = stageCount > 1 ? (float)i / (stageCount - 1) : 0f;
                    sectionRadius = Mathf.Lerp(course.towerBaseRadius, course.towerTopRadius, t);

                    // 섹션 입구에 착지용 테라스. 이게 층처럼 보이게 만드는 실체다.
                    if (i > 0)
                    {
                        BuildSectionTerrace(stageRoot.transform, spiralHeight,
                                            sectionRadius + stage.width * 0.5f,
                                            previousSectionRadius + stage.width * 0.5f + 4f);
                    }

                    previousSectionRadius = sectionRadius;
                }

                List<PathNode> nodes = BuildStagePath(stage, stageRoot.transform, ref cursor, baseYaw);

                DecorateStage(stage, stageRoot.transform, nodes);

                allNodes.AddRange(nodes);
            }

            BuildStartBackstop(root.transform, allNodes);
            BuildTowerCore(root.transform);

            // 골은 마지막 구간이 향하던 방향 그대로 놓아야 길과 이어진다.
            Quaternion goalRotation = allNodes.Count > 0
                ? Quaternion.Euler(0f, allNodes[allNodes.Count - 1].rotation.eulerAngles.y, 0f)
                : Quaternion.Euler(0f, baseYaw, 0f);

            BuildGoal(root.transform, cursor, goalRotation);
            BuildActors(root.transform, allNodes);

            if (course.applyAtmosphere) ApplyAtmosphere();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            float totalLength = 0f;
            for (int i = 0; i < stageCount; i++) totalLength += course.stages[i].length * stageScale;

            if (course.spiralTower)
            {
                float laps = spiralAngle / (2f * Mathf.PI);

                Debug.Log(string.Format(
                    "[SnowBall] 타워 생성 완료 — 스테이지 {0}개, 구간 {1}개, 길이 {2:0} m, " +
                    "높이 {3:0.0} m, {4:0.0}바퀴 (반지름 {5:0}→{6:0} m)",
                    stageCount, allNodes.Count, totalLength, spiralHeight, laps,
                    course.towerBaseRadius, course.towerTopRadius));
            }
            else
            {
                Debug.Log(string.Format(
                    "[SnowBall] 코스 생성 완료 — 스테이지 {0}개, 구간 {1}개, 총 길이 {2:0} m, 총 고도 {3:0.0} m",
                    stageCount, allNodes.Count, totalLength, cursor.y));
            }
        }


        /// <summary>
        /// 새 씬을 만들어 코스를 통째로 생성하고 저장한다.
        /// 다른 씬과 섞이지 않게 하는 가장 안전한 방법.
        /// </summary>
        [MenuItem("SnowBall/새 씬에 코스 생성 + 저장", false, 1)]
        public static void BuildIntoNewScene()
        {
            BuildIntoScene("SnowBall_Play", 0, 1f);
        }


        /// <summary>
        /// 전 구간을 절반 길이로 압축한 연습 코스.
        ///
        /// 스테이지를 잘라내지 않는 것이 핵심이다. 나무·빙판·연못·강아지·마을아이·강풍을
        /// 전부 한 번씩 겪어야 "이 게임이 어떤 흐름인지"를 알 수 있다.
        /// 길이만 줄이면 밀도가 배로 뛰므로 장애물 개수와 굽이도 같이 줄인다.
        /// </summary>
        [MenuItem("SnowBall/연습용 짧은 코스 (전 구간 압축)", false, 2)]
        public static void BuildPracticeScene()
        {
            BuildIntoScene("SnowBall_Practice", 0, 0.5f);
        }


        /// <summary>① 눈밭만. 조작감 하나만 반복해서 볼 때.</summary>
        [MenuItem("SnowBall/튜닝용 (① 눈밭만)", false, 3)]
        public static void BuildTuningScene()
        {
            BuildIntoScene("SnowBall_Tuning", 1, 1f);
        }


        private static void BuildIntoScene(string sceneName, int limit, float scale)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects,
                NewSceneMode.Single
            );

            stageLimit = limit;
            stageScale = scale;

            try
            {
                BuildCourse();
            }
            finally
            {
                stageLimit = 0;
                stageScale = 1f;
            }

            EnsureFolder(BaseFolder + "/Scenes");

            string path = string.Format("{0}/Scenes/{1}.unity", BaseFolder, sceneName);

            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();

            Debug.Log("[SnowBall] 씬 저장 완료 — " + path);
        }


        [MenuItem("SnowBall/코스 삭제", false, 20)]
        public static void DeleteCourse()
        {
            RemoveExistingCourse();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }


        private static void RemoveExistingCourse()
        {
            GameObject existing = GameObject.Find(RootName);

            while (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                existing = GameObject.Find(RootName);
            }
        }


        // ==========================================
        // 길 만들기
        // ==========================================

        private static List<PathNode> BuildStagePath(StageDefinition stage, Transform parent,
                                                     ref Vector3 cursor, float baseYaw)
        {
            List<PathNode> nodes = new List<PathNode>();

            float segLength = Mathf.Max(1f, course.segmentLength);

            float length = stage.length * stageScale;

            // 너무 짧아지면 체크포인트·강아지·강풍 배치가 무너지므로 바닥을 둔다.
            int count = Mathf.Max(3, Mathf.RoundToInt(length / segLength));

            float turn = stage.turnDegrees * stageScale;

            // ══════════════════════════════════════════════════════════
            // 1단계 — 중심선을 먼저 전부 구한다 (구간 수 + 1개의 점)
            //
            // 예전에는 구간을 하나씩 만들면서 박스를 놓았는데, 그러면 이웃한 박스의
            // 끝면이 서로 다른 각도라 이음매마다 턱과 틈이 생겼다.
            // 눈덩이가 그 턱에 걸려 저절로 멈춘다.
            //
            // 점을 먼저 다 구해두면 각 이음매의 방향을 하나로 정할 수 있고,
            // 이웃한 구간이 **같은 모서리를 공유**하게 만들 수 있다.
            // ══════════════════════════════════════════════════════════

            Vector3[] points = new Vector3[count + 1];

            Vector3 walk = course.spiralTower
                ? new Vector3(Mathf.Cos(spiralAngle) * sectionRadius,
                              spiralHeight,
                              Mathf.Sin(spiralAngle) * sectionRadius)
                : cursor;

            float angle = spiralAngle;
            float height = spiralHeight;

            for (int i = 0; i <= count; i++)
            {
                points[i] = walk;

                if (i == count) break;

                float t = count > 1 ? (float)i / (count - 1) : 0f;

                // 경사를 구간마다 흔든다. 평균은 stage.slopeDegrees 그대로 유지된다.
                // 일정한 비탈에서는 놓친 눈덩이가 늘 똑같이 굴러가서 쫓아갈 여지가 없다.
                //
                // 주기를 낮게 잡는 것이 중요하다. 주기가 높으면 이웃한 구간의 경사가
                // 10도 넘게 차이 나서, 아무리 이음매를 맞춰도 계단처럼 꺾인다.
                float wobble = Mathf.Sin(t * Mathf.PI * 2f) * course.slopeVariance;
                float slope = Mathf.Max(-4f, stage.slopeDegrees + wobble);
                float slopeRad = slope * Mathf.Deg2Rad;

                if (course.spiralTower)
                {
                    // 비탈을 따라 segLength 만큼 가면 수평으로는 cos 만큼만 나아간다.
                    angle += segLength * Mathf.Cos(slopeRad) / Mathf.Max(2f, sectionRadius);
                    height += segLength * Mathf.Sin(slopeRad);

                    walk = new Vector3(Mathf.Cos(angle) * sectionRadius,
                                       height,
                                       Mathf.Sin(angle) * sectionRadius);
                }
                else
                {
                    float yaw = baseYaw + turn * Mathf.Sin(2f * Mathf.PI * t);
                    walk += Quaternion.Euler(-slope, yaw, 0f) * Vector3.forward * segLength;
                }
            }

            spiralAngle = angle;
            spiralHeight = height;
            cursor = points[count];

            // ══════════════════════════════════════════════════════════
            // 2단계 — 각 이음매의 방향을 하나로 정한다
            //
            // 가운데 점들은 앞뒤 점의 중앙 차분을 쓴다. 그래야 이음매를 사이에 둔
            // 두 구간이 **같은 방향**을 보게 되어 모서리가 정확히 맞물린다.
            // ══════════════════════════════════════════════════════════

            Quaternion[] ringRotation = new Quaternion[count + 1];

            for (int i = 0; i <= count; i++)
            {
                Vector3 dir;

                if (i == 0) dir = points[1] - points[0];
                else if (i == count) dir = points[count] - points[count - 1];
                else dir = points[i + 1] - points[i - 1];

                if (dir.sqrMagnitude < 0.000001f) dir = Vector3.forward;

                ringRotation[i] = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            // ══════════════════════════════════════════════════════════
            // 3단계 — 구간을 만든다
            // ══════════════════════════════════════════════════════════

            for (int i = 0; i < count; i++)
            {
                Vector3 position = points[i];
                Quaternion rot = ringRotation[i];

                float span = Vector3.Distance(points[i], points[i + 1]);

                GameObject segment = new GameObject(string.Format("Seg_{0:000}", i));
                segment.transform.SetParent(parent, false);
                segment.transform.SetPositionAndRotation(position, rot);

                PathNode node = new PathNode();
                node.position = position;
                node.rotation = rot;
                node.width = stage.width;
                node.segment = segment.transform;

                float slabLength = span;
                float halfSpan = span * 0.5f;

                // ── 길 바닥 — 이웃 구간과 모서리를 공유하는 연속 메시.
                node.floor = CreatePathSlab(
                    "Floor", segment.transform, stage.width,
                    points[i], ringRotation[i],
                    points[i + 1], ringRotation[i + 1],
                    course.floorThickness, mats.snow
                );

                AddSurfaceTag(node.floor, SurfaceType.Snow);

                // ── 안쪽 벽 (나선 모드)
                //
                // 나선에서 로컬 -X 는 항상 타워 중심 쪽이다.
                // 중심으로 떨어지면 받아줄 것이 없으므로 여기는 늘 막는다.
                // 바깥쪽은 열어둔다 — 떨어지면 한 바퀴 아래 길이 받아준다.
                if (course.spiralTower && course.towerInnerWallHeight > 0f)
                {
                    node.railLeft = CreateBox(
                        "InnerWall", segment.transform,
                        new Vector3(-stage.width * 0.5f, course.towerInnerWallHeight * 0.5f, halfSpan),
                        new Vector3(0.5f, course.towerInnerWallHeight, slabLength),
                        mats.wall
                    );
                }

                // ── 바깥 난간 — 스테이지가 켜둔 경우에만.
                // 막아두면 떨어질 수가 없어 낭떠러지의 긴장이 사라진다.
                if (stage.rails && course.railHeight > 0f)
                {
                    node.railRight = CreateBox(
                        "Rail_Outer", segment.transform,
                        new Vector3(stage.width * 0.5f, course.railHeight * 0.5f, halfSpan),
                        new Vector3(course.railWidth, course.railHeight, slabLength),
                        mats.rail
                    );

                    if (!course.spiralTower)
                    {
                        node.railLeft = CreateBox(
                            "Rail_L", segment.transform,
                            new Vector3(-stage.width * 0.5f, course.railHeight * 0.5f, halfSpan),
                            new Vector3(course.railWidth, course.railHeight, slabLength),
                            mats.rail
                        );
                    }
                }

                // ── 복구 지대 + 경계벽 (나선에서는 쓰지 않는다)
                BuildApron(node, span, slabLength);

                // ── 복구 램프 — 일정 간격마다, 좌우 번갈아
                bool isRampSegment = course.buildRecoveryApron
                                     && course.rampEverySegments > 0
                                     && globalSegmentIndex % course.rampEverySegments == 0
                                     && i > 0;

                if (isRampSegment)
                {
                    bool leftSide = (globalSegmentIndex / Mathf.Max(1, course.rampEverySegments)) % 2 == 0;
                    BuildRecoveryRamp(ref node, segLength, leftSide);
                }

                // ── 장식
                if (course.buildDecor && stage.decorDensity > 0f)
                {
                    BuildDecor(node, span, stage.decorDensity);
                }

                nodes.Add(node);
                globalSegmentIndex++;
            }

            return nodes;
        }


        /// <summary>
        /// 이웃 구간과 모서리를 공유하는 길 바닥 한 조각.
        ///
        /// **왜 박스를 안 쓰는가:**
        ///   박스는 자기 축에 수직인 끝면을 갖는다. 이웃한 구간은 방향이 다르므로
        ///   두 끝면이 어긋나 이음매마다 턱과 틈이 생긴다. 판을 겹쳐 틈을 메우면
        ///   이번엔 겹친 끝이 다음 판 위로 솟아올라 더 나쁜 턱이 된다.
        ///   눈덩이는 그 턱에 걸려 저절로 멈춘다.
        ///
        /// **대신:**
        ///   시작 모서리는 ring0, 끝 모서리는 ring1 에서 그대로 가져온다.
        ///   이웃 구간도 같은 ring 을 쓰므로 두 조각의 모서리가 정확히 맞물린다.
        ///   턱도 틈도 없는 연속면이 된다.
        /// </summary>
        private static GameObject CreatePathSlab(string name, Transform segment, float width,
                                                 Vector3 p0, Quaternion r0,
                                                 Vector3 p1, Quaternion r1,
                                                 float thickness, Material material)
        {
            GameObject slab = new GameObject(name);
            slab.transform.SetParent(segment, false);

            float half = width * 0.5f;

            Vector3 right0 = r0 * Vector3.right;
            Vector3 up0 = r0 * Vector3.up;
            Vector3 right1 = r1 * Vector3.right;
            Vector3 up1 = r1 * Vector3.up;

            // 월드 좌표의 여덟 꼭짓점 → 구간 로컬 좌표로 옮긴다.
            Vector3[] v = new Vector3[8];

            v[0] = segment.InverseTransformPoint(p0 - right0 * half);                     // 앞 왼쪽 위
            v[1] = segment.InverseTransformPoint(p1 - right1 * half);                     // 뒤 왼쪽 위
            v[2] = segment.InverseTransformPoint(p1 + right1 * half);                     // 뒤 오른쪽 위
            v[3] = segment.InverseTransformPoint(p0 + right0 * half);                     // 앞 오른쪽 위
            v[4] = segment.InverseTransformPoint(p0 - right0 * half - up0 * thickness);   // 아래 네 점
            v[5] = segment.InverseTransformPoint(p1 - right1 * half - up1 * thickness);
            v[6] = segment.InverseTransformPoint(p1 + right1 * half - up1 * thickness);
            v[7] = segment.InverseTransformPoint(p0 + right0 * half - up0 * thickness);

            int[] tris = new int[]
            {
                0, 1, 2,  0, 2, 3,     // 윗면 — 눈덩이가 구르는 곳
                4, 6, 5,  4, 7, 6,     // 아랫면 (아래층에서 올려다보인다)
                0, 4, 5,  0, 5, 1,     // 왼쪽 옆면
                3, 2, 6,  3, 6, 7,     // 오른쪽 옆면
                0, 3, 7,  0, 7, 4,     // 시작 마구리
                1, 5, 6,  1, 6, 2      // 끝 마구리
            };

            Mesh mesh = new Mesh();
            mesh.name = "PathSlab";
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = slab.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = slab.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            MeshCollider collider = slab.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            MarkStatic(slab);

            return slab;
        }


        // ==========================================
        // 복구 지대 — 이 게임에서 "떨어짐"이 곧 죽음이 되지 않게 하는 장치
        // ==========================================

        private static void BuildApron(PathNode node, float segLength, float slabLength)
        {
            if (!course.buildRecoveryApron) return;

            GameObject apron = CreateBox(
                "Apron", node.segment,
                new Vector3(0f, -course.apronDrop - course.floorThickness * 0.5f, segLength * 0.5f),
                new Vector3(course.apronWidth, course.floorThickness, slabLength),
                mats.apron
            );

            AddSurfaceTag(apron, SurfaceType.Snow);

            if (!course.buildBoundaryWalls) return;

            // 복구 지대 바깥 벽. 이게 있어야 "어디로도 못 벗어난다"가 성립한다.
            float half = course.apronWidth * 0.5f;

            CreateBox(
                "Wall_L", node.segment,
                new Vector3(-half, course.boundaryWallHeight * 0.5f - course.apronDrop, segLength * 0.5f),
                new Vector3(0.6f, course.boundaryWallHeight, slabLength),
                mats.wall
            );

            CreateBox(
                "Wall_R", node.segment,
                new Vector3(half, course.boundaryWallHeight * 0.5f - course.apronDrop, segLength * 0.5f),
                new Vector3(0.6f, course.boundaryWallHeight, slabLength),
                mats.wall
            );
        }


        /// <summary>
        /// 복구 지대에서 길로 다시 올라오는 비스듬한 램프.
        /// 이 구간의 난간을 열어야 실제로 올라올 수 있다.
        /// </summary>
        private static void BuildRecoveryRamp(ref PathNode node, float segLength, bool leftSide)
        {
            // 램프가 붙는 쪽 난간을 연다.
            GameObject rail = leftSide ? node.railLeft : node.railRight;

            if (rail != null)
            {
                Object.DestroyImmediate(rail);

                if (leftSide) node.railLeft = null;
                else node.railRight = null;
            }

            float run = Mathf.Max(1f, course.rampRun);
            float rise = course.apronDrop;

            float slabLength = Mathf.Sqrt(run * run + rise * rise);
            float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            float side = leftSide ? -1f : 1f;
            float edge = node.width * 0.5f;

            // 램프 중심은 길 가장자리와 (가장자리 + run) 의 중간, 높이는 낙차의 절반 아래.
            Vector3 localPos = new Vector3(
                side * (edge + run * 0.5f),
                -rise * 0.5f,
                segLength * 0.5f
            );

            // 로컬 +X 로 갈 때 내려가야 하는 쪽이 오른쪽이므로 부호가 반대다.
            float roll = leftSide ? angle : -angle;

            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = leftSide ? "Ramp_L" : "Ramp_R";
            ramp.transform.SetParent(node.segment, false);
            ramp.transform.localPosition = localPos;
            ramp.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
            ramp.transform.localScale = new Vector3(slabLength, course.floorThickness, course.rampWidth);

            SetMaterial(ramp, mats.ramp);
            AddSurfaceTag(ramp, SurfaceType.Snow);
            MarkStatic(ramp);
        }


        /// <summary>
        /// 코스 맨 아래를 막는 벽.
        /// 눈덩이가 ① 눈밭 시작점보다 더 내려가면 세상 밖이라, 여기서 받아준다.
        /// </summary>
        private static void BuildStartBackstop(Transform parent, List<PathNode> nodes)
        {
            if (nodes.Count == 0) return;

            PathNode first = nodes[0];

            GameObject backstop = new GameObject("Backstop_시작벽");
            backstop.transform.SetParent(parent, false);
            backstop.transform.SetPositionAndRotation(first.position, first.rotation);

            float width = course.buildRecoveryApron ? course.apronWidth : first.width;

            // 길 높이의 벽
            CreateBox("Wall", backstop.transform,
                      new Vector3(0f, 1.5f, -0.5f),
                      new Vector3(width, 4f, 1f),
                      mats.wall);

            // 복구 지대 높이까지 이어지는 벽
            if (course.buildRecoveryApron)
            {
                CreateBox("Wall_Apron", backstop.transform,
                          new Vector3(0f, -course.apronDrop + 1.5f, -0.5f),
                          new Vector3(width, 4f, 1f),
                          mats.wall);

                // 시작점 뒤쪽 복구 지대 바닥 — 여기까지 굴러와도 발 디딜 곳이 있다.
                GameObject floor = CreateBox("Apron_Back", backstop.transform,
                          new Vector3(0f, -course.apronDrop - course.floorThickness * 0.5f, -3f),
                          new Vector3(width, course.floorThickness, 6f),
                          mats.apron);

                AddSurfaceTag(floor, SurfaceType.Snow);
            }
        }


        // ==========================================
        // 풍경
        // ==========================================

        private static void BuildDecor(PathNode node, float segLength, float density)
        {
            // 나선에서는 길 양옆이 안쪽 벽 아니면 허공이라, 장식을 놓으면 공중에 뜬다.
            // 타워의 외관은 나중에 실제 에셋으로 붙이는 편이 낫다.
            if (course.spiralTower) return;

            int count = Mathf.RoundToInt(course.decorPerSegment * density);

            // 복구 지대를 막지 않도록, 길 가장자리에서 조금 떨어진 바깥쪽에만 세운다.
            float inner = node.width * 0.5f + 3.5f;
            float outer = course.buildRecoveryApron
                ? course.apronWidth * 0.5f - 1.2f
                : node.width * 0.5f + 10f;

            if (outer <= inner) return;

            float baseY = course.buildRecoveryApron ? -course.apronDrop : 0f;

            for (int i = 0; i < count; i++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                float lateral = side * Random.Range(inner, outer);
                float along = Random.Range(0.1f, 0.9f) * segLength;

                Vector3 pos = new Vector3(lateral, baseY, along);

                float roll = Random.value;

                if (roll < 0.55f) CreateDecorTree(node.segment, pos);
                else if (roll < 0.8f) CreateDecorRock(node.segment, pos);
                else CreateDecorMound(node.segment, pos);
            }
        }


        private static void CreateDecorTree(Transform parent, Vector3 localPos)
        {
            float height = Random.Range(2.5f, 5.5f);

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Decor_Tree";
            trunk.transform.SetParent(parent, false);
            trunk.transform.localPosition = localPos + new Vector3(0f, height * 0.5f, 0f);
            trunk.transform.localScale = new Vector3(0.45f, height * 0.5f, 0.45f);

            SetMaterial(trunk, mats.tree);

            // 장식은 눈덩이와 부딪힐 일이 없다. 콜라이더를 지워 물리 부하를 줄인다.
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            MarkStatic(trunk);
        }


        private static void CreateDecorRock(Transform parent, Vector3 localPos)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Decor_Rock";
            rock.transform.SetParent(parent, false);
            rock.transform.localPosition = localPos + new Vector3(0f, 0.4f, 0f);
            rock.transform.localRotation = Quaternion.Euler(
                Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
            rock.transform.localScale = new Vector3(
                Random.Range(0.8f, 2.2f), Random.Range(0.6f, 1.6f), Random.Range(0.8f, 2.2f));

            SetMaterial(rock, mats.rock);
            Object.DestroyImmediate(rock.GetComponent<Collider>());
            MarkStatic(rock);
        }


        private static void CreateDecorMound(Transform parent, Vector3 localPos)
        {
            float size = Random.Range(1.6f, 3.6f);

            GameObject mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "Decor_Mound";
            mound.transform.SetParent(parent, false);

            // 절반쯤 파묻힌 눈더미
            mound.transform.localPosition = localPos + new Vector3(0f, size * 0.22f, 0f);
            mound.transform.localScale = new Vector3(size, size * 0.6f, size);

            SetMaterial(mound, mats.snow);
            Object.DestroyImmediate(mound.GetComponent<Collider>());
            MarkStatic(mound);
        }


        private static void BuildDistantPeaks(Transform parent, List<PathNode> nodes)
        {
            if (!course.buildDistantPeaks || nodes.Count == 0) return;

            // 나선에서는 길 안쪽이 타워 중심이라, 여기에 봉우리를 세우면 타워 속을 채워버린다.
            if (course.spiralTower) return;

            GameObject peaks = new GameObject("DistantPeaks");
            peaks.transform.SetParent(parent, false);

            int step = Mathf.Max(3, nodes.Count / 18);

            for (int i = 0; i < nodes.Count; i += step)
            {
                PathNode node = nodes[i];

                for (int s = -1; s <= 1; s += 2)
                {
                    float distance = Random.Range(40f, 110f);
                    float height = Random.Range(18f, 55f);
                    float radius = height * Random.Range(0.5f, 0.9f);

                    // 원뿔 대용 — 실린더 위쪽을 좁힐 수 없으니 그냥 넓은 덩어리로 실루엣만 만든다.
                    GameObject peak = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    peak.name = "Peak";
                    peak.transform.SetParent(peaks.transform, false);

                    peak.transform.position = node.position
                        + node.rotation * new Vector3(s * distance, -course.apronDrop, Random.Range(-20f, 20f))
                        + Vector3.up * height * 0.5f;

                    peak.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-6f, 6f));
                    peak.transform.localScale = new Vector3(radius, height, radius);

                    SetMaterial(peak, mats.peak);
                    Object.DestroyImmediate(peak.GetComponent<Collider>());
                    MarkStatic(peak);
                }
            }
        }


        private static void BuildVillageHouses(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            if (!stage.villageHouses || nodes.Count == 0) return;

            int count = Mathf.Max(3, nodes.Count / 4);

            for (int i = 0; i < count; i++)
            {
                int index = Mathf.Clamp(Mathf.RoundToInt((float)i / count * (nodes.Count - 1)), 0, nodes.Count - 1);
                PathNode node = nodes[index];

                float side = (i % 2 == 0) ? -1f : 1f;
                float lateral = side * Random.Range(node.width * 0.5f + 5f, course.apronWidth * 0.5f - 2f);

                float w = Random.Range(3.5f, 6f);
                float h = Random.Range(2.8f, 4.5f);
                float d = Random.Range(3.5f, 6f);

                GameObject house = GameObject.CreatePrimitive(PrimitiveType.Cube);
                house.name = "House";
                house.transform.SetParent(node.segment, false);
                house.transform.localPosition = new Vector3(lateral, -course.apronDrop + h * 0.5f, 3f);
                house.transform.localRotation = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f);
                house.transform.localScale = new Vector3(w, h, d);

                SetMaterial(house, mats.house);
                MarkStatic(house);
            }
        }


        private static void ApplyAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = new Color(0.78f, 0.84f, 0.92f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.70f, 0.78f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientGroundColor = new Color(0.48f, 0.52f, 0.58f);
        }


        // ==========================================
        // 스테이지 꾸미기
        // ==========================================

        private static void DecorateStage(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            if (nodes.Count == 0) return;

            PlaceCheckpoints(stage, parent, nodes);
            PlaceIcePatches(stage, nodes);
            PlacePonds(stage, parent, nodes);

            PlaceScatter(ScaleCount(stage.treeCount), parent, nodes, "Tree", SpawnTree);
            PlaceScatter(ScaleCount(stage.rockCount), parent, nodes, "Rock", SpawnRock);

            PlaceDogs(stage, parent, nodes);
            PlaceVillageKids(stage, parent, nodes);
            BuildVillageHouses(stage, parent, nodes);
            PlaceGust(stage, parent, nodes);
            BuildDistantPeaks(parent, nodes);
        }


        private static void PlaceCheckpoints(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            int count = Mathf.Max(1, ScaleCount(stage.checkpointCount));

            for (int i = 0; i < count; i++)
            {
                // 첫 체크포인트는 스테이지 입구에 둔다.
                float t = count == 1 ? 0f : (float)i / count;
                int index = Mathf.Clamp(Mathf.RoundToInt(t * (nodes.Count - 1)), 0, nodes.Count - 1);

                PathNode node = nodes[index];

                GameObject cp = new GameObject(string.Format("Checkpoint_{0}", checkpointOrder));
                cp.transform.SetParent(parent, false);
                cp.transform.SetPositionAndRotation(
                    node.position + node.rotation * new Vector3(0f, 1.2f, 0.5f),
                    node.rotation
                );

                BoxCollider trigger = cp.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(stage.width, 3f, 0.6f);

                Checkpoint checkpoint = cp.AddComponent<Checkpoint>();
                checkpoint.order = checkpointOrder++;
                checkpoint.stageName = stage.stageName;

                // 되감기 위치 — 아이는 눈덩이보다 조금 아래(비탈 쪽)에 세운다.
                GameObject ballSpawn = new GameObject("SnowballSpawn");
                ballSpawn.transform.SetParent(cp.transform, false);
                ballSpawn.transform.localPosition = new Vector3(0f, -0.4f, 1.2f);
                checkpoint.snowballSpawn = ballSpawn.transform;

                GameObject playerSpawn = new GameObject("PlayerSpawn");
                playerSpawn.transform.SetParent(cp.transform, false);
                playerSpawn.transform.localPosition = new Vector3(0f, -1.2f, -0.6f);
                checkpoint.playerSpawn = playerSpawn.transform;
            }
        }


        private static void PlaceIcePatches(StageDefinition stage, List<PathNode> nodes)
        {
            int patches = ScaleCount(stage.icePatchCount);

            for (int i = 0; i < patches; i++)
            {
                PathNode node = nodes[Random.Range(0, nodes.Count)];

                if (node.floor == null) continue;

                Renderer renderer = node.floor.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mats.ice;

                AddSurfaceTag(node.floor, SurfaceType.Ice);
            }
        }


        private static void PlacePonds(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            int ponds = ScaleCount(stage.pondCount);

            for (int i = 0; i < ponds; i++)
            {
                // 스테이지 초입에 연못이 나오면 너무 가혹하다. 가운데 이후로 민다.
                int index = Random.Range(nodes.Count / 3, nodes.Count);
                index = Mathf.Clamp(index, 0, nodes.Count - 1);

                PathNode node = nodes[index];

                bool leftSide = Random.value < 0.5f;

                // 그 구간의 난간을 연다 — 이쪽으로 빠질 수 있다는 예고이기도 하다.
                GameObject rail = leftSide ? node.railLeft : node.railRight;
                if (rail != null) Object.DestroyImmediate(rail);

                float side = leftSide ? -1f : 1f;
                float offset = node.width * 0.5f + 2.2f;

                // 복구 지대 위에 얕게 판 연못. 빠지면 크게 깎이고 되감긴다.
                float y = course.buildRecoveryApron ? -course.apronDrop + 0.4f : -0.8f;

                GameObject pond = CreateBox(
                    "Pond", node.segment,
                    new Vector3(side * offset, y, course.segmentLength * 0.5f),
                    new Vector3(4.2f, 1.2f, course.segmentLength * 0.9f),
                    mats.pond
                );

                BoxCollider col = pond.GetComponent<BoxCollider>();
                col.isTrigger = true;

                pond.AddComponent<PondTrap>();
            }
        }


        private static void PlaceScatter(int count, Transform parent, List<PathNode> nodes,
                                         string label, System.Func<Transform, string, GameObject> factory)
        {
            for (int i = 0; i < count; i++)
            {
                PathNode node = nodes[Random.Range(0, nodes.Count)];

                // 길 가장자리에서 조금 안쪽까지만. 완전히 길을 막지 않도록 여유를 둔다.
                float half = Mathf.Max(0.5f, node.width * 0.5f - 1.0f);
                float lateral = Random.Range(-half, half);
                float along = Random.Range(0.2f, 0.8f) * course.segmentLength;

                GameObject obj = factory(parent, string.Format("{0}_{1:00}", label, i));

                obj.transform.SetPositionAndRotation(
                    node.position + node.rotation * new Vector3(lateral, 0f, along),
                    Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                );
            }
        }


        private static GameObject SpawnTree(Transform parent, string name)
        {
            // 피벗을 발밑에 두려고 빈 오브젝트를 뿌리로 쓴다.
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);

            // 기본 실린더는 높이 2 → 스케일 y 1.5 면 높이 3, 중심을 1.5 올리면 바닥에 선다.
            trunk.transform.localScale = new Vector3(0.6f, 1.5f, 0.6f);
            trunk.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            SetMaterial(trunk, mats.tree);

            // OnCollisionEnter 는 콜라이더가 붙은 오브젝트로 간다.
            // 그래서 뿌리가 아니라 줄기에 붙여야 한다.
            SnowballDamager damager = trunk.AddComponent<SnowballDamager>();
            damager.kind = SnowballDamager.Kind.Tree;

            MarkStatic(trunk);

            return root;
        }


        private static GameObject SpawnRock(Transform parent, string name)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = name;
            rock.transform.SetParent(parent, false);
            rock.transform.localScale = new Vector3(
                Random.Range(1.0f, 1.8f),
                Random.Range(0.8f, 1.4f),
                Random.Range(1.0f, 1.8f)
            );

            SetMaterial(rock, mats.rock);

            SnowballDamager damager = rock.AddComponent<SnowballDamager>();
            damager.kind = SnowballDamager.Kind.Rock;
            damager.knockback = 1.5f;

            MarkStatic(rock);

            return rock;
        }


        private static void PlaceDogs(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            int dogs = ScaleCount(stage.dogCount);

            for (int i = 0; i < dogs; i++)
            {
                int index = Mathf.Clamp(
                    Mathf.RoundToInt((i + 1f) / (dogs + 1f) * (nodes.Count - 1)),
                    0, nodes.Count - 1);

                PathNode node = nodes[index];

                GameObject dog = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dog.name = string.Format("Dog_{0}", i);
                dog.transform.SetParent(parent, false);
                dog.transform.localScale = new Vector3(0.45f, 0.35f, 0.45f);

                dog.transform.position = node.position
                                         + node.rotation * new Vector3(2f, 0.4f, 0f);

                SetMaterial(dog, mats.dog);

                // 눈덩이를 밀칠 때만 반응해야 하므로 트리거로 둔다.
                Collider col = dog.GetComponent<Collider>();
                col.isTrigger = true;

                dog.AddComponent<DogAgent>();
            }
        }


        private static void PlaceVillageKids(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            int kids = ScaleCount(stage.villageKidCount);

            for (int i = 0; i < kids; i++)
            {
                float t = (i + 0.5f) / Mathf.Max(1, kids);
                int index = Mathf.Clamp(Mathf.RoundToInt(t * (nodes.Count - 1)), 0, nodes.Count - 1);

                PathNode node = nodes[index];

                GameObject kid = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                kid.name = string.Format("VillageKid_{0}", i);
                kid.transform.SetParent(parent, false);
                kid.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);

                float side = (i % 2 == 0) ? -1f : 1f;
                float drop = course.buildRecoveryApron ? -course.apronDrop : 0f;

                kid.transform.position = node.position
                    + node.rotation * new Vector3(side * (node.width * 0.5f + 2.6f), drop + 0.6f, 0f);

                SetMaterial(kid, mats.kid);

                // 아이 본체에 부딪혀도 눈덩이가 막히지 않도록 트리거로.
                kid.GetComponent<Collider>().isTrigger = true;

                LineRenderer line = kid.AddComponent<LineRenderer>();
                line.sharedMaterial = mats.aim;
                line.widthMultiplier = 0.06f;
                line.useWorldSpace = true;
                line.enabled = false;

                VillageKid thrower = kid.AddComponent<VillageKid>();
                thrower.projectilePrefab = projectilePrefab;
            }
        }


        private static void PlaceGust(StageDefinition stage, Transform parent, List<PathNode> nodes)
        {
            if (!stage.gustZone) return;

            // 구간 몇 개 분량으로 제한한다.
            // 나선에서는 멀리 떨어진 두 구간의 직선 거리가 실제 길이와 전혀 달라서,
            // 스테이지의 1/3 같은 식으로 잡으면 타워를 관통하는 거대한 상자가 된다.
            int start = nodes.Count / 3;
            int end = Mathf.Min(nodes.Count - 1, start + 4);

            PathNode a = nodes[start];
            PathNode b = nodes[end];

            GameObject gust = new GameObject("GustZone");
            gust.transform.SetParent(parent, false);

            Vector3 mid = (a.position + b.position) * 0.5f;
            gust.transform.SetPositionAndRotation(mid + Vector3.up * 3f, a.rotation);

            BoxCollider col = gust.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(a.width + 6f, 8f, Vector3.Distance(a.position, b.position));

            GustZone zone = gust.AddComponent<GustZone>();

            // 길을 가로지르는 방향으로 분다.
            zone.direction = a.rotation * Vector3.right;
        }


        // ==========================================
        // 골 지점
        // ==========================================

        /// <summary>
        /// 섹션 입구의 착지 테라스 — 도넛 모양 바닥.
        ///
        /// 이 게임에서 "층"을 실제로 만들어내는 것이 이 테라스다.
        /// 위 섹션은 안쪽(작은 반지름)에서 돌기 때문에, 이 고리가 그 바깥에 남는다.
        /// 위에서 바깥으로 굴러 떨어지면 바닥까지 가지 않고 여기에 착지한다.
        ///
        /// 원형 메시를 만들 수 없으니 상자 여러 개를 원주에 둘러 근사한다.
        /// </summary>
        private static void BuildSectionTerrace(Transform parent, float height,
                                                float innerRadius, float outerRadius)
        {
            if (outerRadius <= innerRadius + 0.5f) return;

            const int Sides = 24;

            GameObject terrace = new GameObject("SectionTerrace");
            terrace.transform.SetParent(parent, false);
            terrace.transform.position = new Vector3(0f, height, 0f);

            float mid = (innerRadius + outerRadius) * 0.5f;
            float depth = outerRadius - innerRadius;

            // 조각 사이가 벌어지지 않게 호 길이보다 조금 길게 잡는다.
            float arc = 2f * Mathf.PI * mid / Sides * 1.15f;

            for (int i = 0; i < Sides; i++)
            {
                float angle = (float)i / Sides * Mathf.PI * 2f;

                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = string.Format("Piece_{0:00}", i);
                piece.transform.SetParent(terrace.transform, false);

                piece.transform.localPosition = outward * mid
                                                + Vector3.down * course.floorThickness * 0.5f;

                piece.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
                piece.transform.localScale = new Vector3(arc, course.floorThickness, depth);

                SetMaterial(piece, mats.apron);
                AddSurfaceTag(piece, SurfaceType.Snow);
                MarkStatic(piece);
            }

            // 테라스 바깥 가장자리 턱 — 여기까지 떨어진 눈덩이가 또 굴러 나가면 의미가 없다.
            for (int i = 0; i < Sides; i++)
            {
                float angle = (float)i / Sides * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                GameObject lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lip.name = string.Format("Lip_{0:00}", i);
                lip.transform.SetParent(terrace.transform, false);

                lip.transform.localPosition = outward * outerRadius + Vector3.up * 0.5f;
                lip.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
                lip.transform.localScale = new Vector3(
                    2f * Mathf.PI * outerRadius / Sides * 1.15f, 1.2f, 0.5f);

                SetMaterial(lip, mats.rail);
                MarkStatic(lip);
            }
        }


        /// <summary>
        /// 타워 중심을 채우는 기둥. 가운데가 뻥 뚫려 있으면 건물로 안 보인다.
        /// 순전히 외관용이고 길과 닿지 않는다.
        /// </summary>
        private static void BuildTowerCore(Transform parent)
        {
            if (!course.spiralTower) return;
            if (spiralHeight <= 1f) return;

            // 맨 위 층의 안쪽 가장자리보다 살짝 안쪽까지만 채운다.
            float radius = Mathf.Max(1f, course.towerTopRadius - 4f);

            GameObject core = CreateBox(
                "TowerCore", parent,
                Vector3.zero,
                new Vector3(radius * 2f, spiralHeight + 6f, radius * 2f),
                mats.wall
            );

            core.transform.position = new Vector3(0f, (spiralHeight + 6f) * 0.5f - 3f, 0f);

            // 맨 아래 층에서 떨어지면 받아줄 것이 없다. 타워 발치에 바닥을 깐다.
            float groundRadius = course.towerBaseRadius + 18f;

            GameObject ground = CreateBox(
                "TowerGround", parent,
                Vector3.zero,
                new Vector3(groundRadius * 2f, 1f, groundRadius * 2f),
                mats.apron
            );

            ground.transform.position = new Vector3(0f, -3.5f, 0f);
            AddSurfaceTag(ground, SurfaceType.Snow);
        }


        private static void BuildGoal(Transform parent, Vector3 cursor, Quaternion rot)
        {
            GameObject goalRoot = new GameObject("Goal_집");
            goalRoot.transform.SetParent(parent, false);
            goalRoot.transform.SetPositionAndRotation(cursor, rot);

            // 평평한 마당
            CreateBox("Yard", goalRoot.transform,
                      new Vector3(0f, -0.3f, 7f),
                      new Vector3(18f, 0.6f, 18f),
                      mats.snow);

            // 마당 난간 — 다 와서 굴러 떨어지면 너무 억울하다
            CreateBox("Yard_Rail_L", goalRoot.transform,
                      new Vector3(-9f, 0.5f, 7f), new Vector3(0.5f, 1.4f, 18f), mats.rail);
            CreateBox("Yard_Rail_R", goalRoot.transform,
                      new Vector3(9f, 0.5f, 7f), new Vector3(0.5f, 1.4f, 18f), mats.rail);

            // 집 — 장식이지만, 정상 30m 전부터 보여야 하는 목표물이다.
            CreateBox("House", goalRoot.transform,
                      new Vector3(0f, 2.2f, 12f),
                      new Vector3(8f, 4.4f, 7f),
                      mats.house);

            CreateBox("Roof", goalRoot.transform,
                      new Vector3(0f, 4.8f, 12f),
                      new Vector3(9f, 1.2f, 8f),
                      mats.rock);

            // 눈사람 몸통 — 여기에 머리를 올려야 한다
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "SnowmanBody";
            body.transform.SetParent(goalRoot.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.1f, 4f);
            body.transform.localScale = Vector3.one * 2.2f;
            SetMaterial(body, mats.ball);
            MarkStatic(body);

            GameObject goal = new GameObject("GoalZone");
            goal.transform.SetParent(goalRoot.transform, false);
            goal.transform.localPosition = new Vector3(0f, 1.5f, 4f);

            BoxCollider trigger = goal.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(7f, 3f, 7f);

            goal.AddComponent<GoalZone>();
        }


        // ==========================================
        // 플레이어 / 눈덩이 / 카메라 / 매니저
        // ==========================================

        private static void BuildActors(Transform parent, List<PathNode> nodes)
        {
            if (nodes.Count == 0) return;

            PathNode start = nodes[0];

            // ── 눈덩이 ──────────────────────────────
            GameObject ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballObject.name = "Snowball";
            ballObject.transform.SetParent(parent, false);
            ballObject.transform.position = start.position
                                            + start.rotation * new Vector3(0f, 0.6f, 4f);
            SetMaterial(ballObject, mats.ball);

            Rigidbody ballBody = ballObject.AddComponent<Rigidbody>();
            ballBody.mass = tuning.MassForDiameter(tuning.startDiameter);

            Snowball snowball = ballObject.AddComponent<Snowball>();
            snowball.tuning = tuning;

            // ── 아이 ────────────────────────────────
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            player.transform.position = start.position
                                        + start.rotation * new Vector3(0f, 0.1f, 1.5f);

            // 피벗이 발밑이라는 전제로 PlayerMotor 가 동작한다.
            CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.35f;

            Rigidbody playerBody = player.AddComponent<Rigidbody>();
            playerBody.mass = tuning.playerMass;
            playerBody.freezeRotation = true;

            GameObject playerMesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerMesh.name = "Mesh";
            playerMesh.transform.SetParent(player.transform, false);
            playerMesh.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            playerMesh.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(playerMesh.GetComponent<Collider>());
            SetMaterial(playerMesh, mats.player);

            // 어느 쪽을 보고 있는지 알아볼 수 있게 코를 하나 달아준다.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            nose.transform.SetParent(player.transform, false);
            nose.transform.localPosition = new Vector3(0f, 1.3f, 0.35f);
            nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.3f);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            SetMaterial(nose, mats.kid);

            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            motor.tuning = tuning;
            motor.snowball = snowball;

            GripStamina grip = player.AddComponent<GripStamina>();
            grip.tuning = tuning;

            PlayerGrab grab = player.AddComponent<PlayerGrab>();
            grab.tuning = tuning;
            grab.snowball = snowball;
            grab.grip = grip;

            // ── 카메라 ──────────────────────────────
            Camera camera = Camera.main;

            if (camera == null)
            {
                GameObject camObject = new GameObject("Main Camera");
                camObject.tag = "MainCamera";
                camera = camObject.AddComponent<Camera>();
                camObject.AddComponent<AudioListener>();
            }

            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 400f);

            CameraRig rig = camera.GetComponent<CameraRig>();
            if (rig == null) rig = camera.gameObject.AddComponent<CameraRig>();

            rig.tuning = tuning;
            rig.target = player.transform;
            rig.snowball = snowball;

            motor.cameraTransform = camera.transform;

            // ── 매니저 / HUD ────────────────────────
            GameObject managerObject = new GameObject("GameManager");
            managerObject.transform.SetParent(parent, false);

            GameManager manager = managerObject.AddComponent<GameManager>();
            manager.tuning = tuning;
            manager.player = motor;
            manager.snowball = snowball;
            manager.grab = grab;

            PlayHud hud = managerObject.AddComponent<PlayHud>();
            hud.game = manager;
            hud.snowball = snowball;
            hud.grab = grab;

            SettingsOverlay settings = managerObject.AddComponent<SettingsOverlay>();
            settings.tuning = tuning;

            // 빛이 없으면 아무것도 안 보인다.
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            }
        }


        // ==========================================
        // 에셋 준비
        // ==========================================

        private static SnowBallTuning LoadOrCreateTuning()
        {
            SnowBallTuning asset = AssetDatabase.LoadAssetAtPath<SnowBallTuning>(TuningPath);
            if (asset != null) return asset;

            EnsureFolder(BaseFolder + "/Tuning");

            asset = ScriptableObject.CreateInstance<SnowBallTuning>();
            AssetDatabase.CreateAsset(asset, TuningPath);
            AssetDatabase.SaveAssets();

            return asset;
        }


        private static CourseDefinition LoadOrCreateCourse()
        {
            CourseDefinition asset = AssetDatabase.LoadAssetAtPath<CourseDefinition>(CoursePath);
            if (asset != null) return asset;

            EnsureFolder(BaseFolder + "/Tuning");

            asset = ScriptableObject.CreateInstance<CourseDefinition>();
            asset.ResetToGddDefaults();

            AssetDatabase.CreateAsset(asset, CoursePath);
            AssetDatabase.SaveAssets();

            return asset;
        }


        private static ThrownSnowball EnsureProjectilePrefab()
        {
            EnsureFolder(PrefabFolder);

            string path = PrefabFolder + "/ThrownSnowball.prefab";

            ThrownSnowball existing = AssetDatabase.LoadAssetAtPath<ThrownSnowball>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            temp.name = "ThrownSnowball";
            temp.transform.localScale = Vector3.one * 0.25f;

            SetMaterial(temp, mats.ball);

            Rigidbody body = temp.AddComponent<Rigidbody>();
            body.mass = 0.4f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            temp.AddComponent<ThrownSnowball>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);

            return saved.GetComponent<ThrownSnowball>();
        }


        private static Materials EnsureMaterials()
        {
            EnsureFolder(MaterialFolder);

            Materials m = new Materials();

            m.snow = MakeMaterial("Snow", new Color(0.93f, 0.95f, 0.98f));
            m.apron = MakeMaterial("Apron", new Color(0.84f, 0.87f, 0.92f));
            m.ice = MakeMaterial("Ice", new Color(0.62f, 0.82f, 0.95f), 0.9f);
            m.rock = MakeMaterial("Rock", new Color(0.45f, 0.45f, 0.48f));
            m.tree = MakeMaterial("Tree", new Color(0.28f, 0.36f, 0.24f));
            m.rail = MakeMaterial("Rail", new Color(0.72f, 0.74f, 0.78f));
            m.ball = MakeMaterial("SnowballWhite", new Color(0.99f, 0.99f, 1f));
            m.player = MakeMaterial("Player", new Color(0.95f, 0.55f, 0.2f));
            m.dog = MakeMaterial("Dog", new Color(0.7f, 0.5f, 0.3f));
            m.kid = MakeMaterial("Kid", new Color(0.85f, 0.25f, 0.3f));
            m.house = MakeMaterial("House", new Color(0.55f, 0.35f, 0.3f));
            m.pond = MakeMaterial("Pond", new Color(0.2f, 0.45f, 0.75f));
            m.aim = MakeMaterial("AimLine", new Color(1f, 0.4f, 0.4f));
            m.wall = MakeMaterial("BoundaryWall", new Color(0.56f, 0.60f, 0.68f));
            m.peak = MakeMaterial("Peak", new Color(0.74f, 0.79f, 0.88f));
            m.ramp = MakeMaterial("Ramp", new Color(0.88f, 0.90f, 0.72f));

            return m;
        }


        private static Material MakeMaterial(string name, Color color, float smoothness = 0.2f)
        {
            string path = string.Format("{0}/{1}.mat", MaterialFolder, name);

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // URP 프로젝트라 Lit 을 먼저 찾고, 없으면 빌트인으로 떨어진다.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            Material material = new Material(shader);
            material.name = name;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

            AssetDatabase.CreateAsset(material, path);

            return material;
        }


        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }


        // ==========================================
        // 작은 도구들
        // ==========================================

        private static GameObject CreateBox(string name, Transform parent,
                                            Vector3 localPosition, Vector3 localScale,
                                            Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = localScale;

            SetMaterial(box, material);
            MarkStatic(box);

            return box;
        }


        private static void SetMaterial(GameObject target, Material material)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }


        /// <summary>움직이지 않는 지형은 Static 으로 표시해 배칭이 되게 한다.</summary>
        private static void MarkStatic(GameObject target)
        {
            if (target == null) return;
            if (target.GetComponent<Rigidbody>() != null) return;

            GameObjectUtility.SetStaticEditorFlags(
                target,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
            );
        }


        private static void AddSurfaceTag(GameObject target, SurfaceType surface)
        {
            SurfaceTag tag = target.GetComponent<SurfaceTag>();
            if (tag == null) tag = target.AddComponent<SurfaceTag>();

            tag.surface = surface;
        }
    }
}
