using UnityEngine;

namespace Ring.Flow
{
    /// <summary>弹道的轻量视觉：一个按格坐标同步的白色圆点，不做物理与碰撞。</summary>
    [DisallowMultipleComponent]
    public sealed class ProjectileView : MonoBehaviour
    {
        private static Sprite sharedSprite;

        private SpriteRenderer spriteRenderer;
        private Color baseColor = Color.white;
        private float baseSize = 0.25f;

        private Sprite[] frames;
        private float frameInterval = 0.05f;
        private float frameTimer;
        private int frameIndex;

        public static ProjectileView Create(Transform parent, Color color, float size)
        {
            GameObject go = new GameObject("Projectile");
            go.transform.SetParent(parent, false);
            ProjectileView view = go.AddComponent<ProjectileView>();
            view.Apply(color, size);
            return view;
        }

        public void Apply(Color color, float size)
        {
            baseColor = color;
            baseSize = Mathf.Max(0.05f, size);
            EnsureRenderer();
            spriteRenderer.color = baseColor;
            transform.localScale = Vector3.one * baseSize;
        }

        public void SetReturning(bool returning)
        {
            EnsureRenderer();
            Color color = baseColor;
            color.a = returning ? 0.55f : 1f;
            spriteRenderer.color = color;
        }

        /// <summary>
        /// 换成序列帧视觉（精灵游侠的箭矢）。<paramref name="worldLength"/> 为箭在画面上的世界长度，
        /// 按首帧贴图宽度换算缩放，避免受 PPU 与 Create 传入的尺寸双重影响。
        /// </summary>
        public void ApplyFrames(Sprite[] sprites, float fps, float worldLength, bool flipX)
        {
            if (sprites == null || sprites.Length == 0)
            {
                return;
            }

            EnsureRenderer();
            frames = sprites;
            frameInterval = 1f / Mathf.Max(1f, fps);
            frameIndex = 0;
            frameTimer = 0f;

            spriteRenderer.sprite = frames[0];
            spriteRenderer.flipX = flipX;
            spriteRenderer.color = baseColor;

            float native = frames[0].bounds.size.x;
            float scale = native > 0.0001f ? Mathf.Max(0.01f, worldLength) / native : 1f;
            transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void Update()
        {
            if (frames == null || frames.Length <= 1 || spriteRenderer == null)
            {
                return;
            }

            frameTimer += Time.deltaTime;
            if (frameTimer < frameInterval)
            {
                return;
            }

            while (frameTimer >= frameInterval)
            {
                frameTimer -= frameInterval;
                frameIndex = (frameIndex + 1) % frames.Length;
            }

            spriteRenderer.sprite = frames[frameIndex];
        }

        public void SetWorldPosition(Vector3 position)
        {
            transform.position = position;
        }

        private void EnsureRenderer()
        {
            if (spriteRenderer != null)
            {
                return;
            }

            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            spriteRenderer.sprite = GetSharedSprite();
            spriteRenderer.sortingOrder = 20;
        }

        private static Sprite GetSharedSprite()
        {
            if (sharedSprite != null)
            {
                return sharedSprite;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            sharedSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            sharedSprite.hideFlags = HideFlags.HideAndDontSave;
            return sharedSprite;
        }
    }
}
