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

        public static void StackedAddInventory(Inventory _inventory, string _itemName, int _amount)
        {
            if (_amount <= 0) return;

            if (!_inventory) throw new System.ArgumentNullException("_inventory");
            if (string.IsNullOrEmpty(_itemName)) throw new System.ArgumentNullException("_itemName");

            Item_Base item = ItemManager.GetItemByName(_itemName);
            if (item == null) throw new System.InvalidOperationException("_itemName not found");

            int stackSize = item.settings_Inventory.StackSize;
            for (int i = 0; i < _amount / stackSize; ++i) //need to do the stacking ourself
            {
                _inventory.AddItem(_itemName, stackSize);
            }
            int rest = _amount % stackSize;
            if (rest > 0)
            {
                _inventory.AddItem(_itemName, rest);
            }
        }

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

        public static void ClearChildren(Transform _transform)
        {
            for (int i = 0; i < _transform.childCount; ++i)
            {
                GameObject.Destroy(_transform.GetChild(i).gameObject);
            }
        }
    }
}
