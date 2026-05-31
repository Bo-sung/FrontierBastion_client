using UnityEngine;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Base class for all runtime-spawned battle visual objects.
    /// Manages standard pool hooks, sprite renderer caching, sorting orders, and transforms.
    /// </summary>
    public abstract class BattleVisualView : MonoBehaviour
    {
        [SerializeField] protected SpriteRenderer spriteRenderer;

        protected virtual void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
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
