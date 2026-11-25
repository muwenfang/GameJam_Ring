// 区域机制基类：提供配置读取与纯色 Sprite 生成（不含角色/战斗耦合）
using UnityEngine;

namespace MapKitTest
{
    public abstract class ZoneMechanicBase : MonoBehaviour
    {
        protected MapKitConfig cfg;
        public string zoneLabel = "";

        protected virtual void Awake()
        {
            cfg = MapKitConfig.Load();
            if (cfg == null) Debug.LogError("[机制] " + zoneLabel + " 读取 map_config.json 失败");
        }

        // 生成一张纯色 Sprite（用于遮罩、光斑、阴影），轴心可调
        protected static Sprite MakeSolidSprite(string name, Color color, float ppu, Vector2 pivot)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { color, color, color, color });
            tex.Apply();
            tex.name = name;
            return Sprite.Create(tex, new Rect(0, 0, 2, 2), pivot, ppu);
        }

        protected float Now { get { return Time.time; } }

        // 供 HUD 显示的机制运行状态摘要，各机制可覆写
        public virtual string StatusText() { return "运行中"; }

        protected static Color Hex(string html)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(html, out c) ? c : Color.white;
        }
    }
}
