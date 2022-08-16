using System.Linq;
using UnityEngine;

namespace pp.RaftMods.AutoSorter
{
    public static class CUtil //util
    {
        public static void Log(object _message)
        {
            Debug.Log($"[{CAutoSorter.MOD_NAME}] {_message}");
        }

        public static void LogW(object _message)
        {
            Debug.LogWarning($"[{CAutoSorter.MOD_NAME}] {_message}");
        }

        public static void LogE(object _message)
        {
            Debug.LogError($"[{CAutoSorter.MOD_NAME}] {_message}");
        }

        public static void LogD(object _message)
        {
            if (!(CAutoSorter.Config?.Debug ?? false)) return;
            Debug.Log($"[{CAutoSorter.MOD_NAME}][DEBUG] {_message}");
        }

        /// <summary>
        /// Adds items to the specified inventory and takes care of properly creating item stacks while doing so.
        /// </summary>
        /// <param name="_inventory">The inventory to add items to.</param>
        /// <param name="_itemName">The name of the item to add to the inventory.</param>
        /// <param name="_amount">The amount of items to add.</param>
        public static int StackedAddInventory(Inventory _inventory, string _itemName, int _amount)
        {
            if (_amount <= 0) return 0;

            if (!_inventory) throw new System.ArgumentNullException("_inventory");
            if (string.IsNullOrEmpty(_itemName)) throw new System.ArgumentNullException("_itemName");

            Item_Base item = ItemManager.GetItemByName(_itemName);
            if (item == null) throw new System.InvalidOperationException("_itemName not found");

            int spaceLeft = SpaceLeftForItem(_inventory, item);
            if (spaceLeft == 0) return 0;

            int actuallyAdded = _amount;
            if(_amount > spaceLeft)
            {
                actuallyAdded = spaceLeft;
            }

            int stackSize = item.settings_Inventory.StackSize;
            for (int i = 0; i < actuallyAdded / stackSize; ++i) //need to do the stacking ourself
            {
                _inventory.AddItem(_itemName, stackSize);
            }
            int rest = actuallyAdded % stackSize;
            if (rest > 0)
            {
                _inventory.AddItem(_itemName, rest);
            }

            return actuallyAdded;
        }

        public static int SpaceLeftForItem(Inventory _inventory, Item_Base _item)
        {
            int spaceLeft = 0;
            foreach(var slot in _inventory.allSlots)
            {
                if (!slot.active || slot.locked || slot.StackIsFull() || (!slot.IsEmpty && slot.itemInstance.UniqueIndex != _item.UniqueIndex)) continue;
                spaceLeft += _item.settings_Inventory.StackSize - (slot.itemInstance?.Amount ?? 0);
            }
            return spaceLeft;
        }

        public static bool HasSpaceLeftForItem(Inventory _inventory, string _itemName)
        {
            return _inventory?.allSlots.Any(_o => _o.active && !_o.locked && !_o.StackIsFull() && (_o.IsEmpty || _o.itemInstance.UniqueName == _itemName)) ?? false;
        }

        public static void ReimburseConstructionCosts(Network_Player _player, bool _applyDowngradeMultiplier)
        {
            int toAdd;
            foreach (var cost in CAutoSorter.Config.UpgradeCosts)
            {
                Item_Base item = ItemManager.GetItemByName(cost.Name);
                if(item == null)
                {
                    CUtil.LogW("Configured reimbursement item \"" + cost.Name + "\" was not found. Please check your \"UpgradeCosts\" setting in the config file and make sure the items in there exist in your raft.");
                    continue;
                }
                if(cost.Amount < 0)
                {
                    CUtil.LogW("Invalid amount configured for item \"" + cost.Name + "\" in the \"UpgradeCosts\" of your config file. Make sure you set the amount to a value >0.");
                    continue;
                }
                toAdd = (int)(cost.Amount * (_applyDowngradeMultiplier ? CAutoSorter.Config.ReturnItemsOnDowngradeMultiplier : 1f));
                if (toAdd > 0)
                {
                    int added = CUtil.StackedAddInventory(_player.Inventory, cost.Name, toAdd);
                    if(added < toAdd)
                    {
                        _player.Inventory.DropItem(item, toAdd - added);
                    }
                }
            }
        }

        /// <summary>
        /// Reformats the given texture, copying its context to a new texture with the provided format.
        /// </summary>
        /// <param name="_texture">The texture to reformat.</param>
        /// <param name="_format">The new format of the texture.</param>
        /// <param name="_useMipMaps">Are mip-maps enabled for the newly created texture.</param>
        /// <returns>A new texture object reformatted with the provided settings.</returns>
        public static Texture2D MakeReadable(Texture2D _texture, TextureFormat _format = TextureFormat.RGB24, bool _useMipMaps = false)
        {
            RenderTexture r = RenderTexture.GetTemporary(_texture.width, _texture.height);
            Graphics.Blit(_texture, r);
            RenderTexture.active = r;
            var tex = new Texture2D(_texture.width, _texture.height, _format, _useMipMaps);
            tex.ReadPixels(new Rect(0, 0, r.width, r.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            r.Release();
            return tex;
        }
    }
}
