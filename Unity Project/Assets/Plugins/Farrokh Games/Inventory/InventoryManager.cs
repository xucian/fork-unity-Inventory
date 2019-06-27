using System;
using System.Linq;
using UnityEngine;

namespace FarrokhGames.Inventory
{
    public class InventoryManager : IInventoryManager, IDisposable
    {
        private Vector2Int _size = Vector2Int.one;
        private IInventoryProvider _provider;
        private IInventoryItem[] _itemCache = null;
        private Rect _fullRect;

        public InventoryManager(IInventoryProvider provider, int width, int height)
        {
            _provider = provider;
            Rebuild();
            Resize(width, height);
        }

        public int Width { get { return _size.x; } }
        public int Height { get { return _size.y; } }

        public void Resize(int width, int height)
        {
            _size.x = width;
            _size.y = height;
            RebuildRect();
        }

        protected Rect Rect { get { return _fullRect; } }

        private void RebuildRect()
        {
            _fullRect = new Rect(0, 0, _size.x, _size.y);
            HandleSizeChanged();
            if (OnResized != null)OnResized();
        }

        protected void HandleSizeChanged()
        {
            // Drop all items that no longer fit the inventory
            for (int i = 0; i < AllItems.Length;)
            {
                var item = AllItems[i];
                var shouldBeDropped = false;
                var padding = Vector2.one * 0.01f;

                if (!Rect.Contains(item.GetMinPoint() + padding) || !Rect.Contains(item.GetMaxPoint() - padding))
                {
                    shouldBeDropped = true;
                }

                if (shouldBeDropped)
                {
                    TryDrop(item);
                }
                else
                {
                    i++;
                }
            }
        }

        public void Rebuild()
        {
            Rebuild(false);
        }

        private void Rebuild(bool silent)
        {
            _itemCache = new IInventoryItem[_provider.InventoryItemCount];
            for (var i = 0; i < _provider.InventoryItemCount; i++)
            {
                _itemCache[i] = _provider.GetInventoryItem(i);
            }
            if (!silent && OnRebuilt != null)OnRebuilt();
        }

        public void Dispose()
        {
            _provider = null;
            _itemCache = null;
        }

        /// <inheritdoc />
        public bool IsFull
        {
            get
            {
                if (_provider.IsInventoryFull)return true;

                for (var x = 0; x < Width; x++)
                {
                    for (var y = 0; y < Height; y++)
                    {
                        if (GetAtPoint(new Vector2Int(x, y)) == null) { return false; }
                    }
                }
                return true;
            }
        }

        public IInventoryItem[] AllItems { get { return _itemCache; } }

        public Action OnRebuilt { get; set; }
        public Action<IInventoryItem> OnItemDropped { get; set; }
        public Action<IInventoryItem> OnItemAdded { get; set; }
        public Action<IInventoryItem> OnItemRemoved { get; set; }
        public Action OnResized { get; set; }

        public IInventoryItem GetAtPoint(Vector2Int point)
        {
            // Single item override
            if (_provider.InventoryRenderMode == InventoryRenderMode.Single && _provider.IsInventoryFull && _itemCache.Length > 0)
            {
                return _itemCache[0];
            }

            foreach (var item in _itemCache)
            {
                if (item.Contains(point)) { return item; }
            }
            return null;
        }

        public IInventoryItem[] GetAtPoint(Vector2Int point, Vector2Int size)
        {
            var posibleItems = new IInventoryItem[size.x * size.y];
            var c = 0;
            for (var x = 0; x < size.x; x++)
            {
                for (var y = 0; y < size.y; y++)
                {
                    posibleItems[c] = GetAtPoint(point + new Vector2Int(x, y));
                    c++;
                }
            }
            return posibleItems.Distinct().Where(x => x != null).ToArray();
        }

        public bool TryRemove(IInventoryItem item)
        {
            if (CanRemove(item))
            {
                var success = _provider.RemoveInventoryItem(item);
                if (success)
                {
                    Rebuild(true);
                    if (OnItemRemoved != null)OnItemRemoved(item);
                }

                return success;
            }
            return false;
        }

        public bool TryDrop(IInventoryItem item)
        {
            if (CanDrop(item))
            {
                var success = _provider.DropInventoryItem(item);
                if (success)
                {
                    Rebuild(true);
                    if (OnItemDropped != null)OnItemDropped(item);
                }
                return success;
            }
            return false;
        }

        public bool CanAddAt(IInventoryItem item, Vector2Int point)
        {
            if (!_provider.CanAddInventoryItem(item) || _provider.IsInventoryFull)
            {
                return false;
            }
            else if (_provider.InventoryRenderMode == InventoryRenderMode.Single)
            {
                return true;
            }

            var previousPoint = item.Position;
            item.Position = point;
            var padding = Vector2.one * 0.01f;

            // Check if item is outside of inventory
            if (!Rect.Contains(item.GetMinPoint() + padding) || !Rect.Contains(item.GetMaxPoint() - padding))
            {
                item.Position = previousPoint;
                return false;
            }

            // Check if item overlaps another item already in the inventory
            foreach (var otherItem in AllItems)
            {
                if (item.Overlaps(otherItem))
                {
                    item.Position = previousPoint;
                    return false;
                }
            }

            return true; // Item can be added
        }

        public bool TryAddAt(IInventoryItem item, Vector2Int point)
        {
            if (CanAddAt(item, point))
            {
                var sucess = _provider.AddInventoryItem(item);
                if (sucess)
                {
                    switch (_provider.InventoryRenderMode)
                    {
                        case InventoryRenderMode.Single:
                            item.Position = GetCenterPosition(item);
                            break;
                        default:
                            item.Position = point;
                            break;
                    }
                    Rebuild(true);
                    if (OnItemAdded != null)OnItemAdded(item);
                }
                return sucess;
            }
            return false;
        }

        public bool CanAdd(IInventoryItem item)
        {
            Vector2Int point;
            if (!Contains(item) && GetFirstPointThatFitsItem(item, out point))
            {
                return CanAddAt(item, point);
            }
            return false;
        }

        public bool TryAdd(IInventoryItem item)
        {
            if (!CanAdd(item))return false;
            Vector2Int point;
            if (GetFirstPointThatFitsItem(item, out point))
            {
                return TryAddAt(item, point);
            }
            return false;
        }

        /// <inheritdoc />
        public bool CanSwap(IInventoryItem item)
        {
            return _provider.InventoryRenderMode == InventoryRenderMode.Single &&
                DoesItemFit(item) &&
                _provider.CanAddInventoryItem(item);
        }

        /// <inheritdoc />
        public void DropAll()
        {
            var itemsToDrop = AllItems.ToArray();
            foreach (var item in itemsToDrop)
            {
                TryDrop(item);
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            var itemsToRemove = AllItems.ToArray();
            foreach (var item in AllItems)
            {
                TryRemove(item);
            }
        }

        public bool Contains(IInventoryItem item)
        {
            return AllItems.Contains(item);
        }

        public bool CanRemove(IInventoryItem item)
        {
            return Contains(item) && _provider.CanRemoveInventoryItem(item);
        }

        public bool CanDrop(IInventoryItem item)
        {
            return Contains(item) && _provider.CanDropInventoryItem(item);
        }

        /*
         * Get first free point that will fit the given item
         */
        private bool GetFirstPointThatFitsItem(IInventoryItem item, out Vector2Int point)
        {
            if (DoesItemFit(item))
            {
                for (var x = 0; x < Width - (item.Width - 1); x++)
                {
                    for (var y = 0; y < Height - (item.Height - 1); y++)
                    {
                        point = new Vector2Int(x, y);
                        if (CanAddAt(item, point))return true;
                    }
                }
            }
            point = Vector2Int.zero;
            return false;
        }

        /* 
         * Returns true if given items physically fits within this inventory
         */
        private bool DoesItemFit(IInventoryItem item)
        {
            return item.Width <= Width && item.Height <= Height;
        }

        /*
         * Returns the center post position for a given item within this inventory
         */
        private Vector2Int GetCenterPosition(IInventoryItem item)
        {
            return new Vector2Int(
                (_size.x - item.Width) / 2,
                (_size.y - item.Height) / 2
            );
        }
    }
}