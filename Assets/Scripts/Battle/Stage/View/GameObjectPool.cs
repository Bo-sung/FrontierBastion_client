using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrontierBastion.Client.Stage.View
{
    /// <summary>
    /// Generic object pool for BattleVisualView components to recycle GameObjects and avoid GC allocations.
    /// Provides Instantiate, caching, active tracking, prewarming, and fallback support.
    /// </summary>
    public sealed class GameObjectPool<T> where T : BattleVisualView
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly List<T> _pool = new List<T>();
        private readonly Func<T> _fallbackCreator;

        public GameObjectPool(T prefab, Transform parent, int initialSize, Func<T> fallbackCreator = null)
        {
            _prefab = prefab;
            _parent = parent;
            _fallbackCreator = fallbackCreator;

            if (initialSize > 0)
            {
                Prewarm(initialSize);
            }
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                T obj = CreateNew();
                obj.SetActiveVisual(false);
                _pool.Add(obj);
            }
        }

        public T Get()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && !_pool[i].gameObject.activeSelf)
                {
                    T obj = _pool[i];
                    obj.OnSpawn();
                    return obj;
                }
            }

            T newObj = CreateNew();
            _pool.Add(newObj);
            newObj.OnSpawn();
            return newObj;
        }

        public void Return(T obj)
        {
            if (obj == null) return;
            obj.OnDespawn();
        }

        public void ReturnAll()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null && _pool[i].gameObject.activeSelf)
                {
                    _pool[i].OnDespawn();
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                {
                    UnityEngine.Object.Destroy(_pool[i].gameObject);
                }
            }
            _pool.Clear();
        }

        private T CreateNew()
        {
            T obj = null;
            if (_prefab != null)
            {
                obj = UnityEngine.Object.Instantiate(_prefab, _parent, false);
            }
            else if (_fallbackCreator != null)
            {
                obj = _fallbackCreator();
            }
            else
            {
                GameObject go = new GameObject(typeof(T).Name);
                go.transform.SetParent(_parent, false);
                obj = go.AddComponent<T>();
            }

            return obj;
        }
    }
}
