#if UNITY_EDITOR
// 菜单：钟声 / 地图 / ...
// 一次性在「当前打开的场景」里搭好六张地图根节点（背景 + 碰撞地面）并自动连线。
//
// 设计约束（与项目既有约定一致）：
// - 只在编辑器里、由用户点菜单触发；生成的是持久化场景物体，用户可在 Inspector 里随便改。
// - 运行时代码零 new GameObject / 零 Instantiate / 零 Resources.Load：贴图在编辑器里赋值并序列化。
// - 不碰角色、输入、钟声、技能、UI 层级；只创建地图物体与三条 Inspector 连线。
using System;
using System.IO;
using System.Linq;
using Ring.Maps;
using Ring.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MapSceneSetupTools
{
    private const string MapRoot = "Assets/MapKit/Maps/";
    private const string ContainerName = "BattleMaps";
    private const string PresenterObjectName = "BattleMapPresenter";
    private const string BackgroundChildName = "BG_背景";
    private const string GroundChildName = "Ground_地面25格";

    // ---- 地面几何（按当前 SampleScene 定标）----
    // 相机：正交、position(0,0,-10)、orthographicSize=5 → 可见 y ∈ [-5, 5]，16:9 下可见宽 ≈ 17.8。
    // 角色：MatchSpawner 六边形站位半径 3.5，CharacterInstance 的 transform 原点就是脚底
    //      （见 SwordsmanSpriteAnimation.ApplyFrame 的脚底锚定）。
    // 因此把地面「顶面」放在 y = -3.5：最下方那名角色脚底刚好贴住地面，其余角色分布在上方。
    private const float GroundSurfaceY = -3.5f;
    private const float GroundWidth = 18f;
    private const float GroundThickness = 1f;

    // 背景铺满相机全高（2 * orthographicSize）。
    private const float BackgroundHeight = 10f;
    private const float BackgroundWidth = 18f;

    private const int BackgroundSortingOrder = -20;
    private const int GroundSortingOrder = -10;

    private const string MenuRoot = "钟声/地图/";

    // ---------------------------------------------------------------- 生成

    [MenuItem(MenuRoot + "生成六张地图与碰撞地面", false, 1)]
    public static void Generate()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[地图工具] 没有已打开且可编辑的场景，请先在 Unity 里打开 SampleScene。");
            return;
        }

        if (string.IsNullOrEmpty(scene.path))
        {
            Debug.LogError("[地图工具] 当前场景还没保存过（没有路径）。请先 Ctrl+S 保存场景再生成，"
                           + "否则生成的物体无法持久化。");
            return;
        }

        GameObject container = FindRootOrChildNamed(null, ContainerName);
        if (container != null && !EditorUtility.DisplayDialog(
                "已存在 BattleMaps",
                "场景里已经有生成的地图容器。\n\n「重新生成」会删除现有六个地图根节点（你在这些物体上的手工调整会丢失）；"
                + "「只补连线」保留现有物体，仅重新连接展示器与界面引用。",
                "重新生成", "只补连线"))
        {
            RewireOnly(scene);
            return;
        }

        if (container != null)
        {
            Undo.DestroyObjectImmediate(container);
        }

        container = new GameObject(ContainerName);
        Undo.RegisterCreatedObjectUndo(container, "生成六张地图与碰撞地面");
        // 容器必须保持激活：否则六个子根节点即使被 Show() 激活，也会因为父物体隐藏而仍然不显示。
        container.SetActive(true);

        var roots = new GameObject[BattleMaps.Count];
        for (int i = 0; i < BattleMaps.Count; i++)
        {
            BattleMapInfo info = BattleMaps.Info(BattleMaps.FromIndex(i));
            roots[i] = BuildMapRoot(container.transform, info, i);
        }

        BattleMapPresenter presenter = EnsurePresenter(scene);
        WritePresenterScenes(presenter, roots);
        WireUiController(scene, presenter);
        EnsureMapBackgroundVisible(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[地图工具] 已生成六张地图根节点（背景 + 碰撞地面）、" + PresenterObjectName
                  + "，并保存场景 " + scene.path + "。\n"
                  + "地面顶面 Y=" + GroundSurfaceY.ToString("F2") + "，宽 " + GroundWidth
                  + "，厚 " + GroundThickness + "；六个根节点已存成隐藏，Show() 只会激活抽中的那张。\n"
                  + "注意：当前角色没有 Rigidbody2D，碰撞地面只作为地图实体/碰撞层存在，"
                  + "不会让角色物理站立（详见 Docs/08）。");
    }

    /// <summary>
    /// battleCanvas 下的 bg1 是 Screen Space Overlay 的纯色不透明 Image（无贴图），
    /// 会整块盖住世界空间里的地图背景 SpriteRenderer —— 不处理的话点完菜单仍看不到地图。
    /// 判定条件与 GameUiController.hideEmptyBackdrops 一致（无 sprite + 不透明），
    /// 只改一个 enabled 勾选、用 Undo 记录，用户随时能在 Inspector 里改回。
    /// </summary>
    private static void EnsureMapBackgroundVisible(Scene scene)
    {
        foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (image.gameObject.scene != scene || image.gameObject.name != "bg1")
            {
                continue;
            }

            if (!image.enabled || image.sprite != null || image.color.a < 0.99f)
            {
                continue;
            }

            Undo.RecordObject(image, "生成地图时隐藏 bg1 纯色背景");
            image.enabled = false;
            EditorUtility.SetDirty(image);
            Debug.Log("[地图工具] 已关闭 battleCanvas 下的 bg1 纯色 Image：它是 Screen Space Overlay、"
                      + "不透明且无贴图，会盖住世界空间的地图背景。地图背景改由各图 BG_背景 SpriteRenderer 提供；"
                      + "想恢复就在 Inspector 重新勾选该 Image（或 Ctrl+Z）。");
        }
    }

    /// <summary>保留现有物体，只重连三条 Inspector 引用（不删除、不覆盖手工调整）。</summary>
    private static void RewireOnly(Scene scene)
    {
        GameObject container = FindRootOrChildNamed(null, ContainerName);
        BattleMapPresenter presenter = EnsurePresenter(scene);
        var roots = new GameObject[BattleMaps.Count];

        if (container != null)
        {
            for (int i = 0; i < BattleMaps.Count; i++)
            {
                string zoneName = BattleMaps.Info(BattleMaps.FromIndex(i)).ZoneName;
                roots[i] = FindChildNamed(container.transform, zoneName);
                MapScene mapScene = roots[i] != null ? roots[i].GetComponent<MapScene>() : null;
                if (mapScene != null)
                {
                    AutoFillGroundReferences(mapScene);
                    // 只补连线：用户可能手工改过编号，只有非法值才纠正。
                    EnsureMapId(mapScene, i, false);
                }
            }
        }

        WritePresenterScenes(presenter, roots);
        WireUiController(scene, presenter);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[地图工具] 已保留现有物体并重新连线，详见「钟声 / 地图 / 校验地图接线」。");
    }

    // ---------------------------------------------------------------- 校验

    [MenuItem(MenuRoot + "校验地图接线", false, 2)]
    public static void Validate()
    {
        BattleMapPresenter presenter = UnityEngine.Object.FindObjectOfType<BattleMapPresenter>(true);
        GameUiController ui = UnityEngine.Object.FindObjectOfType<GameUiController>(true);

        if (presenter == null)
        {
            Debug.LogWarning("[地图工具] 场景里没有 BattleMapPresenter，请先执行「生成六张地图与碰撞地面」。");
            return;
        }

        var so = new SerializedObject(presenter);
        SerializedProperty scenes = so.FindProperty("scenes");
        if (scenes == null || !scenes.isArray)
        {
            Debug.LogError("[地图工具] BattleMapPresenter 上找不到 scenes 数组，接线状态无法读取。");
            return;
        }

        int ok = 0;
        for (int i = 0; i < BattleMaps.Count; i++)
        {
            BattleMapInfo info = BattleMaps.Info(BattleMaps.FromIndex(i));
            if (i >= scenes.arraySize)
            {
                Debug.LogWarning("[地图工具] 槽位 " + i + "（" + info.DisplayName + "）越界：scenes 长度只有 " + scenes.arraySize + "。");
                continue;
            }

            SerializedProperty slot = scenes.GetArrayElementAtIndex(i);
            MapScene mapScene = slot.objectReferenceValue as MapScene;
            if (mapScene == null)
            {
                Debug.LogWarning("[地图工具] 槽位 " + i + "（" + info.DisplayName + "）为空。");
                continue;
            }

            bool idOk = mapScene.MapId == info.Id;
            bool groundOk = mapScene.HasGround;
            if (idOk && groundOk)
            {
                ok++;
            }

            Debug.Log("[地图工具] 槽位 " + i + " " + info.DisplayName
                      + " → " + mapScene.name
                      + " | MapId=" + mapScene.MapId + (idOk ? " ✓" : " ✗ 应为 " + info.Id)
                      + " | 地面=" + (groundOk ? "顶面 Y=" + mapScene.GroundSurfaceY.ToString("F2") : "未接")
                      + " | 初始激活=" + mapScene.gameObject.activeSelf
                      + (mapScene.gameObject.activeSelf ? "（建议存成隐藏）" : ""));
        }

        bool uiWired = false;
        if (ui != null)
        {
            var uiSo = new SerializedObject(ui);
            uiWired = uiSo.FindProperty("battleMapPresenter").objectReferenceValue == presenter;
        }

        // 遮挡检查：bg1 若仍是不透明纯色 Image，地图背景会被整块盖住。
        foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (image.gameObject.scene != SceneManager.GetActiveScene()
                || image.gameObject.name != "bg1"
                || !image.enabled || image.sprite != null || image.color.a < 0.99f)
            {
                continue;
            }

            Debug.LogWarning("[地图工具] battleCanvas 下的 bg1 仍是启用状态的不透明纯色 Image，"
                             + "会盖住世界空间的地图背景。请在 Inspector 里取消勾选它的 Image 组件，"
                             + "或重新执行一次「生成六张地图与碰撞地面」。");
        }

        Debug.Log("[地图工具] 校验完成：" + ok + "/" + BattleMaps.Count + " 张地图接线完整；"
                  + "GameUiController.Battle Map Presenter " + (uiWired ? "已连" : "未连（会走自动查找，通常也能工作）") + "。");
    }

    // ---------------------------------------------------------------- 清理

    [MenuItem(MenuRoot + "移除生成的地图与展示器", false, 3)]
    public static void RemoveGenerated()
    {
        if (!EditorUtility.DisplayDialog(
                "移除地图物体",
                "将删除场景里的 BattleMaps 容器（六张地图根节点）与 BattleMapPresenter 物体，"
                + "并清空 GameUiController 上的展示器引用。此操作可用 Ctrl+Z 撤销。",
                "确认移除", "取消"))
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameUiController ui = UnityEngine.Object.FindObjectOfType<GameUiController>(true);
        if (ui != null)
        {
            var uiSo = new SerializedObject(ui);
            uiSo.FindProperty("battleMapPresenter").objectReferenceValue = null;
            uiSo.ApplyModifiedProperties();
        }

        GameObject presenterGo = FindRootOrChildNamed(null, PresenterObjectName);
        if (presenterGo != null)
        {
            Undo.DestroyObjectImmediate(presenterGo);
        }

        GameObject container = FindRootOrChildNamed(null, ContainerName);
        if (container != null)
        {
            Undo.DestroyObjectImmediate(container);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[地图工具] 已移除生成的地图物体与展示器。");
    }

    // ---------------------------------------------------------------- 内部：搭一张地图

    private static GameObject BuildMapRoot(Transform parent, BattleMapInfo info, int index)
    {
        var root = new GameObject(info.ZoneName);
        Undo.RegisterCreatedObjectUndo(root, "生成地图 " + info.DisplayName);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;

        MapScene mapScene = root.AddComponent<MapScene>();
        // 新建对象没有手工值需要尊重，必须强制写入编号，否则六个 Map Id 会全是默认的 Map01。
        EnsureMapId(mapScene, index, true);

        // 背景：SpriteRenderer 缩放铺满相机视野。贴图在编辑器里 Load 后序列化进场景，运行时零加载。
        Sprite bgSprite = LoadFirstSprite(MapRoot + info.ZoneFolder + "/Backgrounds");
        GameObject bg = CreateChild(root.transform, BackgroundChildName, Vector3.zero);
        var bgRenderer = bg.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = bgSprite;
        bgRenderer.sortingOrder = BackgroundSortingOrder;
        if (bgSprite != null)
        {
            bg.transform.localScale = ScaleToSize(bgSprite, BackgroundWidth, BackgroundHeight);
        }
        else
        {
            Debug.LogWarning("[地图工具] " + info.DisplayName + " 没找到背景 Sprite（" + info.ZoneFolder
                             + "/Backgrounds），已留空，请在 Inspector 里手工指定。");
        }

        // 碰撞地面：子节点带 SpriteRenderer + BoxCollider2D。
        // 碰撞体顶面 = groundRoot.localPosition.y + GroundThickness/2 = GroundSurfaceY。
        Sprite groundSprite = LoadGroundSprite(info);
        var groundGo = CreateChild(
            root.transform, GroundChildName,
            new Vector3(0f, GroundSurfaceY - GroundThickness * 0.5f, 0f));

        var groundRenderer = groundGo.AddComponent<SpriteRenderer>();
        groundRenderer.sprite = groundSprite;
        groundRenderer.sortingOrder = GroundSortingOrder;

        var box = groundGo.AddComponent<BoxCollider2D>();
        if (groundSprite != null)
        {
            // collider.size 用 sprite 的本地尺寸，配合 localScale 缩放后世界尺寸正好是 Width × Thickness。
            box.size = groundSprite.bounds.size;
            groundGo.transform.localScale = ScaleToSize(groundSprite, GroundWidth, GroundThickness);
        }
        else
        {
            box.size = new Vector2(GroundWidth, GroundThickness);
        }

        box.offset = Vector2.zero;
        box.isTrigger = false;

        var mapSo = new SerializedObject(mapScene);
        mapSo.FindProperty("groundRoot").objectReferenceValue = groundGo.transform;
        mapSo.FindProperty("groundCollider").objectReferenceValue = box;
        mapSo.ApplyModifiedPropertiesWithoutUndo();

        // 六张根节点存成隐藏：Show() 只激活抽中的那张，初始全激活会叠在一起。
        root.SetActive(false);
        return root;
    }

    /// <summary>地面贴图：优先 Ground/ 目录；04_ElfForest 没有 Ground 目录，退化到背景里的 base 图。</summary>
    private static Sprite LoadGroundSprite(BattleMapInfo info)
    {
        string groundDir = MapRoot + info.ZoneFolder + "/Ground";
        if (Directory.Exists(groundDir))
        {
            Sprite s = LoadFirstSprite(groundDir, "warn");
            if (s != null)
            {
                return s;
            }
        }

        string bgDir = MapRoot + info.ZoneFolder + "/Backgrounds";
        string[] files = ImageFiles(bgDir);
        string baseFile = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
            .IndexOf("base", StringComparison.OrdinalIgnoreCase) >= 0);
        return LoadSprite(baseFile ?? files.FirstOrDefault());
    }

    // ---------------------------------------------------------------- 内部：连线

    private static BattleMapPresenter EnsurePresenter(Scene scene)
    {
        GameObject go = FindRootOrChildNamed(null, PresenterObjectName);
        if (go == null)
        {
            go = new GameObject(PresenterObjectName);
            Undo.RegisterCreatedObjectUndo(go, "创建 BattleMapPresenter");
            go.SetActive(true);
        }

        BattleMapPresenter presenter = go.GetComponent<BattleMapPresenter>();
        if (presenter == null)
        {
            presenter = go.AddComponent<BattleMapPresenter>();
        }

        return presenter;
    }

    private static void WritePresenterScenes(BattleMapPresenter presenter, GameObject[] roots)
    {
        var so = new SerializedObject(presenter);
        SerializedProperty scenes = so.FindProperty("scenes");
        if (scenes.arraySize != BattleMaps.Count)
        {
            scenes.arraySize = BattleMaps.Count;
        }

        for (int i = 0; i < BattleMaps.Count; i++)
        {
            MapScene mapScene = roots[i] != null ? roots[i].GetComponent<MapScene>() : null;
            if (mapScene == null)
            {
                // 只补连线时找不到对应根节点：保留槽位原值，绝不把用户手工连的线清空。
                Debug.LogWarning("[地图工具] 槽位 " + i + " 没找到对应根节点，已保留原有引用。");
                continue;
            }

            scenes.GetArrayElementAtIndex(i).objectReferenceValue = mapScene;
        }

        so.ApplyModifiedProperties();
    }

    private static void WireUiController(Scene scene, BattleMapPresenter presenter)
    {
        GameUiController[] all = Resources.FindObjectsOfTypeAll<GameUiController>();
        GameUiController ui = all.FirstOrDefault(c => c.gameObject.scene == scene);
        if (ui == null)
        {
            Debug.LogWarning("[地图工具] 场景里没有 GameUiController：运行时会自动查找 BattleMapPresenter，"
                             + "也可以稍后手工把它拖进 Battle Map Presenter 槽位。");
            return;
        }

        var so = new SerializedObject(ui);
        so.FindProperty("battleMapPresenter").objectReferenceValue = presenter;
        so.ApplyModifiedProperties();
    }

        /// <summary>
        /// force=true：新建对象，强制把 Map Id 写成槽位对应的编号（默认是 Map01，不写会六张全 0）。
        /// force=false：只补连线路径，用户手工设过的合法编号一律保留，仅纠正越界值。
        /// enum 字段用 enumValueIndex + enumNames 定位，不假设「索引 == 枚举值」。
        /// </summary>
        private static void EnsureMapId(MapScene mapScene, int index, bool force)
        {
            var so = new SerializedObject(mapScene);
            SerializedProperty prop = so.FindProperty("mapId");
            BattleMapId expected = BattleMaps.FromIndex(index);

            bool needWrite = force;
            if (!force)
            {
                int current = prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumNames.Length
                    ? SafeParseEnum(prop.enumNames[prop.enumValueIndex])
                    : -1;
                needWrite = current < 0 || current >= BattleMaps.Count;
            }

            if (!needWrite)
            {
                return;
            }

            int target = Array.FindIndex(prop.enumNames, n => n == expected.ToString());
            prop.enumValueIndex = target >= 0 ? target : 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int SafeParseEnum(string name)
        {
            return Enum.TryParse(name, out BattleMapId id) ? (int)id : -1;
        }

    /// <summary>手工接线的场景：只拖了 Ground Root 时，把碰撞体引用补上（不创建对象）。</summary>
    private static void AutoFillGroundReferences(MapScene mapScene)
    {
        var so = new SerializedObject(mapScene);
        SerializedProperty rootProp = so.FindProperty("groundRoot");
        SerializedProperty colProp = so.FindProperty("groundCollider");
        bool changed = false;

        if (colProp.objectReferenceValue == null && rootProp.objectReferenceValue is Transform t)
        {
            colProp.objectReferenceValue = t.GetComponent<Collider2D>();
            changed = colProp.objectReferenceValue != null;
        }

        if (changed)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---------------------------------------------------------------- 内部：资源与查找

    private static Sprite LoadFirstSprite(string dir, string excludeKeyword = null)
    {
        string[] files = ImageFiles(dir);
        foreach (string file in files)
        {
            if (!string.IsNullOrEmpty(excludeKeyword)
                && file.IndexOf(excludeKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            Sprite s = LoadSprite(file);
            if (s != null)
            {
                return s;
            }
        }

        return LoadSprite(files.FirstOrDefault());
    }

    private static Sprite LoadSprite(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath.Replace('\\', '/'));
    }

    private static string[] ImageFiles(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(dir)
            .Where(f =>
            {
                string ext = Path.GetExtension(f);
                return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.Replace('\\', '/'))
            .ToArray();
    }

    /// <summary>按 sprite 的本地 bounds.size 计算缩放，使显示尺寸等于 (width, height) 世界单位。</summary>
    private static Vector3 ScaleToSize(Sprite sprite, float width, float height)
    {
        Vector2 size = sprite.bounds.size;
        float sx = size.x > 0.0001f ? width / size.x : 1f;
        float sy = size.y > 0.0001f ? height / size.y : 1f;
        return new Vector3(sx, sy, 1f);
    }

    private static GameObject CreateChild(Transform parent, string childName, Vector3 localPosition)
    {
        var go = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(go, "生成 " + childName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go;
    }

    /// <summary>在场景里按名字找根物体；parent 传 null 时只查场景根级，避免名字撞车。</summary>
    private static GameObject FindRootOrChildNamed(Transform parent, string objectName)
    {
        if (parent != null)
        {
            return FindChildNamed(parent, objectName);
        }

        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
            {
                return root;
            }
        }

        return null;
    }

    private static GameObject FindChildNamed(Transform parent, string objectName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == objectName)
            {
                return child.gameObject;
            }
        }

        return null;
    }
}
#endif
