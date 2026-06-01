using UnityEngine;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Base class for all runtime-spawned battle visual objects.
    /// Manages standard pool hooks, sprite renderer caching, sorting orders, and transforms.
    /// </summary>
    public abstract class Entity_BaseView : MonoBehaviour
    {
        [SerializeField] protected SpriteRenderer spriteRenderer;

        protected virtual void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            EnsureSprite(spriteRenderer);
        }

        // Shared white placeholder so views stay visible when a prefab's
        // SpriteRenderer has no sprite assigned yet (artists fill these in later).
        private static Sprite _placeholder;
        protected static Sprite PlaceholderSprite
        {
            get
            {
                if (_placeholder == null)
                {
                    var tex = new Texture2D(4, 4);
                    var cols = new Color[16];
                    for (int i = 0; i < 16; i++) cols[i] = Color.white;
                    tex.SetPixels(cols);
                    tex.Apply();
                    _placeholder = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                }
                return _placeholder;
            }
        }

        /// <summary>Assigns the white placeholder if the renderer has no sprite.</summary>
        protected static void EnsureSprite(SpriteRenderer sr)
        {
            if (sr != null && sr.sprite == null) sr.sprite = PlaceholderSprite;
        }

        public virtual void OnSpawn()
        {
            SetActiveVisual(true);
        }

        public virtual void OnDespawn()
        {
            SetActiveVisual(false);
        }

        public void SetSortingOrder(int order)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.sortingOrder = order;
            }
        }

        public void SetWorldPosition(Vector3 position)
        {
            transform.position = position;
        }

        public void SetActiveVisual(bool active)
        {
            gameObject.SetActive(active);
        }

        public SpriteRenderer GetSpriteRenderer() => spriteRenderer;

        public void SetSprite(Sprite sprite)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = sprite;
            }
        }
    }
}
