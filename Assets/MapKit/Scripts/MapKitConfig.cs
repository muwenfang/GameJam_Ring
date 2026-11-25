// 六张地图的机制参数。用法：var cfg = MapKitConfig.Load(); cfg.cave.damage ...
using System;
using UnityEngine;

[Serializable] public class RangeF { public float min, max; public float Random() => UnityEngine.Random.Range(min, max); }
[Serializable] public class RangeI { public int min, max; public int Random() => UnityEngine.Random.Range(min, max + 1); }

[Serializable] public class GridConfig { public int cols, rows; public float tileUnits; public int actorWidth, actorHeight; }

[Serializable] public class BambooConfig
{
    public float darknessAlpha, flashSeconds, extraCheckEvery, extraChance, damageChance, stunSeconds;
    public RangeF intervalSeconds, normalBoltEndHeight;
    public int damage; public bool normalHitsGround;
    public string normalColor, damageColor;
}
[Serializable] public class SteamConfig { public RangeF batchInterval; public float ventRatio, warningSeconds; public int maxConsecutive, damage; }
[Serializable] public class ArenaConfig { public float intervalSeconds, atkBonusPerStack; public bool stackable, onlyInsideArea; }
[Serializable] public class ForestConfig
{
    public RangeF batchInterval; public float ratioOfEmpty, growSeconds, matureSeconds, witherSeconds, rootSeconds;
    public int maxConsecutive, damage; public bool tangleOncePerPlant;
}
[Serializable] public class ClockConfig { public float intervalSeconds, rewindSeconds, hasteMultiplier, slowMultiplier, speedDuration; }
[Serializable] public class CaveConfig { public float intervalSeconds, warningSeconds; public RangeI rockCount; public int damage, rockSizeTiles; }

[Serializable] public class MapKitConfig
{
    public GridConfig grid;
    public BambooConfig bamboo; public SteamConfig steam; public ArenaConfig arena;
    public ForestConfig forest; public ClockConfig clock; public CaveConfig cave;

    public static MapKitConfig Load()
    {
        var t = Resources.Load<TextAsset>("MapKit/map_config");
        if (t == null) { Debug.LogError("[MapKit] 找不到 Resources/MapKit/map_config.json"); return null; }
        return JsonUtility.FromJson<MapKitConfig>(t.text);
    }
}
