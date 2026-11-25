// 菜单：Tools/MapKit/森林：启用树叶分层摇摆 / 森林：恢复整图扭曲（旧版）
// 在当前打开的场景里找到用 elf_forest_pixel 的背景，换成去叶底图 + 默认材质，并挂上 ForestLeafSway。支持 Ctrl+Z 撤销。
using MapKitTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MapKitForestLeafSetup
{
    const string Dir = "Assets/MapKit/Maps/04_ElfForest/";
    const string OrigPath = Dir + "Backgrounds/elf_forest_pixel.jpg";
    const string BasePath = Dir + "Backgrounds/elf_forest_pixel_base.png";
    const string AtlasPath = Dir + "Sprites/Leaves/elf_forest_leaf_atlas.png";
    const string JsonPath = Dir + "Sprites/Leaves/elf_forest_leaf_clumps.json";
    const string OldMatPath = "Assets/MapKit/Materials/ForestWindSway.mat";

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            a = AssetDatabase.LoadAssetAtPath<T>(path);
        }
        if (a == null) Debug.LogError("[MapKit] 找不到素材：" + path);
        return a;
    }

    [MenuItem("Tools/MapKit/森林：启用树叶摇摆 + 动态光点")]
    public static void Enable()
    {
        var orig = Load<Sprite>(OrigPath);
        var baseSp = Load<Sprite>(BasePath);
        var atlas = Load<Texture2D>(AtlasPath);
        var json = Load<TextAsset>(JsonPath);
        if (baseSp == null || atlas == null || json == null) return;
        var defMat = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        int n = 0;
        foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            if (sr.sprite != orig && sr.sprite != baseSp) continue;
            Undo.RecordObject(sr, "启用树叶分层摇摆");
            sr.sprite = baseSp;
            sr.sharedMaterial = defMat;        // 不再用整图扭曲的着色器
            EditorUtility.SetDirty(sr);

            var ls = sr.GetComponent<ForestLeafSway>();
            if (ls == null) ls = Undo.AddComponent<ForestLeafSway>(sr.gameObject);
            Undo.RecordObject(ls, "启用树叶分层摇摆");
            ls.leafAtlas = atlas;
            ls.clumpData = json;
            EditorUtility.SetDirty(ls);
            ls.enabled = false; ls.enabled = true;   // 立即重建叶团

            // 动态萤火虫（背景里画死的光点已从去叶底图里去掉）
            var ff = sr.GetComponent<ForestFireflies>();
            if (ff == null) ff = Undo.AddComponent<ForestFireflies>(sr.gameObject);
            ff.enabled = false; ff.enabled = true;
            n++;
        }
        EditorSceneManager.MarkAllScenesDirty();
        if (n == 0) Debug.LogWarning("[MapKit] 当前场景里没找到使用 elf_forest_pixel 的背景");
        else Debug.Log("[MapKit] 已为 " + n + " 个森林背景启用树叶分层摇摆和动态萤火虫，记得保存场景（Ctrl+S）");
    }

    [MenuItem("Tools/MapKit/森林：恢复整图扭曲（旧版）")]
    public static void Revert()
    {
        var orig = Load<Sprite>(OrigPath);
        var baseSp = Load<Sprite>(BasePath);
        var oldMat = AssetDatabase.LoadAssetAtPath<Material>(OldMatPath);
        int n = 0;
        foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            if (sr.sprite != baseSp) continue;
            var ls = sr.GetComponent<ForestLeafSway>();
            if (ls != null) Undo.DestroyObjectImmediate(ls);
            var ff = sr.GetComponent<ForestFireflies>();
            if (ff != null) Undo.DestroyObjectImmediate(ff);
            Undo.RecordObject(sr, "恢复整图扭曲");
            sr.sprite = orig;
            if (oldMat != null) sr.sharedMaterial = oldMat;
            EditorUtility.SetDirty(sr);
            n++;
        }
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[MapKit] 已恢复 " + n + " 个森林背景为旧版");
    }
}
