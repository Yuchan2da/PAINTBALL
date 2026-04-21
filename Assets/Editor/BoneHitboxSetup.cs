
using UnityEngine;
using UnityEditor;

/// <summary>
/// 에디터 전용 유틸리티: Player 프리팹의 기존 T-포즈 히트박스를 제거하고
/// 골격(뼈) 트랜스폼에 콜라이더를 직접 부착한다.
/// 
/// [실행 방법] Unity 메뉴 → Tools → Setup Bone Hitboxes
/// 
/// [왜 뼈에 콜라이더?]
/// 기존 히트박스(Head, Body, ArmR 등)는 Player 루트의 직접 자식이라
/// 애니메이션을 따라가지 않고 T-포즈로 고정됨.
/// 뼈에 직접 콜라이더를 붙이면 애니메이션에 100% 연동되어
/// 정확한 피격 판정 + 정확한 페인트 위치가 가능.
/// </summary>
public class BoneHitboxSetup : EditorWindow
{
    [MenuItem("Tools/Setup Bone Hitboxes")]
    static void SetupBoneHitboxes()
    {
        // Player 프리팹 로드
        string prefabPath = "Assets/Resources/Player.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[BoneHitboxSetup] 프리팹을 찾을 수 없습니다: {prefabPath}");
            return;
        }

        // 프리팹 편집 모드 진입
        string assetPath = AssetDatabase.GetAssetPath(prefab);
        GameObject instance = PrefabUtility.LoadPrefabContents(assetPath);

        int hitboxLayer = LayerMask.NameToLayer("Hitbox");
        if (hitboxLayer < 0)
        {
            Debug.LogError("[BoneHitboxSetup] 'Hitbox' 레이어가 존재하지 않습니다!");
            PrefabUtility.UnloadPrefabContents(instance);
            return;
        }

        // ─── 1단계: 기존 T-포즈 히트박스 삭제 ───
        string[] oldHitboxNames = { "Head", "Body", "ArmR", "ArmL", "LegR", "LegL" };
        foreach (string name in oldHitboxNames)
        {
            Transform old = instance.transform.Find(name);
            if (old != null)
            {
                Debug.Log($"[BoneHitboxSetup] 기존 히트박스 삭제: {name}");
                Object.DestroyImmediate(old.gameObject);
            }
        }

        // ─── 2단계: 뼈 경로 매핑 ───
        Transform meshRoot = instance.transform.Find("MonkeyMesh/root");
        if (meshRoot == null)
        {
            Debug.LogError("[BoneHitboxSetup] MonkeyMesh/root를 찾을 수 없습니다!");
            PrefabUtility.UnloadPrefabContents(instance);
            return;
        }

        // 뼈 콜라이더 정의: (뼈 경로, 태그, 콜라이더 타입, 파라미터)
        // CapsuleCollider direction: 0=X, 1=Y, 2=Z
        var boneConfigs = new BoneConfig[]
        {
            // ── 머리 (SphereCollider, 헤드샷 판정) ──
            new BoneConfig("pelvis/spine_01/spine_02/spine_03/neck_01/head",
                "Head", ColliderType.Sphere, radius: 0.2f),

            // ── 몸통 (spine_02: 상체 중심) ──
            new BoneConfig("pelvis/spine_01/spine_02",
                "Body", ColliderType.Capsule, radius: 0.15f, height: 0.4f, direction: 1),

            // ── 골반 ──
            new BoneConfig("pelvis",
                "Body", ColliderType.Capsule, radius: 0.14f, height: 0.25f, direction: 1),

            // ── 왼팔 상 ──
            new BoneConfig("pelvis/spine_01/spine_02/spine_03/clavicle_l/upperarm_l",
                "Body", ColliderType.Capsule, radius: 0.05f, height: 0.25f, direction: 0),

            // ── 왼팔 하 ──
            new BoneConfig("pelvis/spine_01/spine_02/spine_03/clavicle_l/upperarm_l/lowerarm_l",
                "Body", ColliderType.Capsule, radius: 0.04f, height: 0.22f, direction: 0),

            // ── 오른팔 상 ──
            new BoneConfig("pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r",
                "Body", ColliderType.Capsule, radius: 0.05f, height: 0.25f, direction: 0),

            // ── 오른팔 하 ──
            new BoneConfig("pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r/lowerarm_r",
                "Body", ColliderType.Capsule, radius: 0.04f, height: 0.22f, direction: 0),

            // ── 왼다리 상 ──
            new BoneConfig("pelvis/thigh_l",
                "Body", ColliderType.Capsule, radius: 0.06f, height: 0.3f, direction: 1),

            // ── 왼다리 하 ──
            new BoneConfig("pelvis/thigh_l/calf_l",
                "Body", ColliderType.Capsule, radius: 0.05f, height: 0.28f, direction: 1),

            // ── 오른다리 상 ──
            new BoneConfig("pelvis/thigh_r",
                "Body", ColliderType.Capsule, radius: 0.06f, height: 0.3f, direction: 1),

            // ── 오른다리 하 ──
            new BoneConfig("pelvis/thigh_r/calf_r",
                "Body", ColliderType.Capsule, radius: 0.05f, height: 0.28f, direction: 1),
        };

        int addedCount = 0;
        foreach (var config in boneConfigs)
        {
            Transform bone = meshRoot.Find(config.bonePath);
            if (bone == null)
            {
                Debug.LogWarning($"[BoneHitboxSetup] 뼈를 찾을 수 없음: {config.bonePath}");
                continue;
            }

            // 레이어 + 태그 설정
            bone.gameObject.layer = hitboxLayer;
            bone.gameObject.tag = config.tag;

            // 기존 콜라이더가 있으면 제거
            var existing = bone.gameObject.GetComponent<Collider>();
            if (existing != null)
                Object.DestroyImmediate(existing);

            // 콜라이더 추가
            if (config.type == ColliderType.Sphere)
            {
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.radius = config.radius;
                sphere.center = Vector3.zero;
            }
            else
            {
                var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                capsule.radius = config.radius;
                capsule.height = config.height;
                capsule.direction = config.direction;
                capsule.center = Vector3.zero;
            }

            addedCount++;
            Debug.Log($"[BoneHitboxSetup] ✅ {bone.name} ({config.tag}) — {config.type} 추가 완료");
        }

        // ─── 3단계: 프리팹 저장 ───
        PrefabUtility.SaveAsPrefabAsset(instance, assetPath);
        PrefabUtility.UnloadPrefabContents(instance);

        Debug.Log($"[BoneHitboxSetup] 완료! 기존 히트박스 {oldHitboxNames.Length}개 삭제, 뼈 콜라이더 {addedCount}개 추가.");
        EditorUtility.DisplayDialog("Bone Hitbox Setup",
            $"완료!\n\n삭제: {oldHitboxNames.Length}개 기존 히트박스\n추가: {addedCount}개 뼈 콜라이더\n\n프리팹이 저장되었습니다.",
            "확인");
    }

    enum ColliderType { Sphere, Capsule }

    struct BoneConfig
    {
        public string bonePath;
        public string tag;
        public ColliderType type;
        public float radius;
        public float height;
        public int direction;

        public BoneConfig(string bonePath, string tag, ColliderType type,
            float radius = 0.1f, float height = 0.3f, int direction = 1)
        {
            this.bonePath = bonePath;
            this.tag = tag;
            this.type = type;
            this.radius = radius;
            this.height = height;
            this.direction = direction;
        }
    }
}
