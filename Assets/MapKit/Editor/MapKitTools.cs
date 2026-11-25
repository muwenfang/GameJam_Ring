// 菜单：Tools/MapKit/...  生成抓握藤动画、地面 Tile
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MapKitTools
{
    const string Root = "Assets/MapKit/Maps/";
    const string VineDir = Root + "04_ElfForest/Sprites/GraspingVine/";
    const string FxDir = Root + "04_ElfForest/Sprites/TangleFX/";
    const string AnimDir = Root + "04_ElfForest/Animations/";

    static Sprite S(string path)
    {
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sp == null) Debug.LogError("[MapKit] 找不到 Sprite: " + path);
        return sp;
    }

    // frames: (sprite 路径, 持续秒数)
    static void MakeClip(string name, bool loop, params (string path, float dur)[] frames)
    {
        var clip = new AnimationClip { frameRate = 60 };
        var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
        var keys = new ObjectReferenceKeyframe[frames.Length + 1];
        float t = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe { time = t, value = S(frames[i].path) };
            t += frames[i].dur;
        }
        keys[frames.Length] = new ObjectReferenceKeyframe { time = t, value = S(frames[frames.Length - 1].path) };
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        var st = AnimationUtility.GetAnimationClipSettings(clip);
        st.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, st);
        Directory.CreateDirectory(AnimDir);
        string p = AnimDir + name + ".anim";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(clip, p);
    }

    [MenuItem("Tools/MapKit/生成抓握藤动画")]
    public static void CreateVineAnimations()
    {
        string v(string n) => VineDir + "gv_" + n + ".png";
        string f(string n) => FxDir + n + ".png";
        // 冒芽到长高 0.7 秒
        MakeClip("GV_Grow", false, (v("sprout_1"), .175f), (v("sprout_2"), .175f), (v("sprout_3"), .175f), (v("pop"), .175f));
        // 成熟 2 秒内循环摆动
        MakeClip("GV_Mature", true, (v("mature_1"), .45f), (v("mature_2"), .45f));
        // 缠住人的瞬间
        MakeClip("GV_Grab", false, (v("grab"), .25f));
        // 凋亡 0.3 秒后销毁
        MakeClip("GV_Wither", false, (v("wither_1"), .1f), (v("wither_2"), .1f), (v("wither_3"), .1f));
        // 缠绕特效（叠在角色身上）：缠上 0.3 秒，保持到定身结束，然后断开
        MakeClip("Tangle_In", false, (f("tangle_1"), .15f), (f("tangle_2"), .15f));
        MakeClip("Tangle_Hold", true, (f("tangle_3"), .5f));
        MakeClip("Tangle_Break", false, (f("tangle_break"), .2f));
        AssetDatabase.SaveAssets();
        Debug.Log("[MapKit] 动画已生成到 " + AnimDir);
    }

    [MenuItem("Tools/MapKit/生成地面 Tile")]
    public static void CreateGroundTiles()
    {
        foreach (var dir in Directory.GetDirectories(Root))
        {
            string g = dir.Replace('\\', '/') + "/Ground";
            if (!Directory.Exists(g)) continue;
            foreach (var file in Directory.GetFiles(g, "*.png"))
            {
                string path = file.Replace('\\', '/');
                var sp = S(path);
                if (sp == null) continue;
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sp;
                string tp = Path.ChangeExtension(path, null) + "_Tile.asset";
                AssetDatabase.DeleteAsset(tp);
                AssetDatabase.CreateAsset(tile, tp);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[MapKit] 地面 Tile 已生成（各地图 Ground 文件夹内 *_Tile.asset）");
    }

    [MenuItem("Tools/MapKit/重新导入全部素材")]
    public static void ReimportAll()
    {
        AssetDatabase.ImportAsset("Assets/MapKit/Maps", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
    }
}
