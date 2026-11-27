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
