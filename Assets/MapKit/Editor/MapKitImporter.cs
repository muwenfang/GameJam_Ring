// 自动设置 MapKit 下所有图片的导入参数：Sprite、PPU（1 格 = 1 单位）、轴心、过滤方式
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class MapKitImporter : AssetPostprocessor
{
    struct Rule
    {
        public float ppu; public bool point; public Vector2 pivot;
        public Rule(float ppu, bool point, Vector2 pivot) { this.ppu = ppu; this.point = point; this.pivot = pivot; }
    }

    static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>
    {
            { "bamboo_lit_scene.jpg", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "bamboo_clump_ink.png", new Rule(100f, false, new Vector2(0.5000f, 0.0000f)) },
            { "ink_rain_drop.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "ink_rain_splash.png", new Rule(128f, false, new Vector2(0.5000f, 0.0000f)) },
            { "lightning_normal_ref.png", new Rule(128f, false, new Vector2(0.5000f, 1.0000f)) },
            { "lightning_damage_ref.png", new Rule(128f, false, new Vector2(0.5000f, 1.0000f)) },
            { "ink_ground_night.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_background.png", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_manhole.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_manhole_warn.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_puff_1.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_puff_2.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_puff_3.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "steam_wisp.png", new Rule(128f, false, new Vector2(0.5000f, 0.0000f)) },
            { "arena_colosseum.jpg", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "arena_sand_ground.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "elf_forest_pixel.jpg", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "gv_grab.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_mature_1.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_mature_2.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_pop.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_sprout_1.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_sprout_2.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_sprout_3.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "tangle_1.png", new Rule(16f, true, new Vector2(0.5000f, 0.0000f)) },
            { "tangle_2.png", new Rule(16f, true, new Vector2(0.5000f, 0.0000f)) },
            { "tangle_3.png", new Rule(16f, true, new Vector2(0.5000f, 0.0000f)) },
            { "tangle_break.png", new Rule(16f, true, new Vector2(0.5000f, 0.0000f)) },
            { "gv_wither_1.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_wither_2.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "gv_wither_3.png", new Rule(16f, true, new Vector2(0.5000f, 0.0270f)) },
            { "clock_background.png", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_face.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_hand_hour.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_hand_minute.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_gear.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_icon_rewind.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_icon_haste.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_icon_slow.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "clock_ground.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "cave_background.jpg", new Rule(64f, false, new Vector2(0.5000f, 0.5000f)) },
            { "cave_falling_rock.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "cave_stalactite_cluster.png", new Rule(128f, false, new Vector2(0.5000f, 1.0000f)) },
            { "cave_stalagmite_cluster.png", new Rule(128f, false, new Vector2(0.5000f, 0.0000f)) },
            { "cave_rock_shadow.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            { "cave_ground_wet.png", new Rule(128f, false, new Vector2(0.5000f, 0.5000f)) },
            // 2026-11-25 精灵森林：去叶底图 + 叶团图集（像素风，Point 过滤、不压缩）
            { "elf_forest_pixel_base.png", new Rule(64f, true, new Vector2(0.5000f, 0.5000f)) },
            { "elf_forest_leaf_atlas.png", new Rule(64f, true, new Vector2(0.5000f, 0.5000f)) },
    };

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("/MapKit/Maps/")) return;
        string file = System.IO.Path.GetFileName(assetPath);
        if (!Rules.TryGetValue(file, out Rule r)) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = r.point ? FilterMode.Point : FilterMode.Bilinear;
        ti.textureCompression = r.point ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        ti.maxTextureSize = 2048;
        ti.spritePixelsPerUnit = r.ppu;

        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spritePivot = r.pivot;
        ti.SetTextureSettings(s);
    }
}
