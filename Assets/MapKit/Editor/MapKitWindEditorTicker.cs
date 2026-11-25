// 让风摇着色器在"不点 Play"时也能动：编辑器里每帧写入全局 _MapKitWindTime 并刷新视图。
// 进入 Play 后把它清零，着色器自动改用 _Time.y。菜单 Tools/MapKit/编辑模式预览风摇 可开关。
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MapKitWindEditorTicker
{
    const string PrefKey = "MapKit.WindPreviewInEditMode";
    const string MenuPath = "Tools/MapKit/编辑模式预览风摇";
    static readonly int WindTimeId = Shader.PropertyToID("_MapKitWindTime");
    static double lastRepaint;

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static MapKitWindEditorTicker()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode || s == PlayModeStateChange.ExitingEditMode)
                Shader.SetGlobalFloat(WindTimeId, 0f);
        };
    }

    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Enabled) { Shader.SetGlobalFloat(WindTimeId, 0f); return; }

        double now = EditorApplication.timeSinceStartup;
        Shader.SetGlobalFloat(WindTimeId, (float)(now % 10000.0) + 0.001f);
        if (now - lastRepaint > 1.0 / 30.0)   // 约 30 帧/秒刷新 Scene / Game 窗口
        {
            lastRepaint = now;
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
    }

    [MenuItem(MenuPath)]
    static void Toggle() { Enabled = !Enabled; }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }
}
