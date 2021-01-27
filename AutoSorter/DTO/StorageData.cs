using System.Collections.Generic;
using System.Linq;

namespace pp.RaftMods.AutoSorter
{
    [System.Serializable]
    public class CStorageData
    {
        public string SaveName;
        public ulong ObjectID;
        public int Priority;
        public bool AutoMode = true;
        //public int[] ItemStates = new int[0];

        [System.NonSerialized]
        public readonly Dictionary<int, CItemFilter> Filters = new Dictionary<int, CItemFilter>();

        private CItemFilter[] mi_filters = new CItemFilter[0];

        public CStorageData() { }
        public CStorageData(ulong _objectID)
            : this(_objectID, 0, true, new CItemFilter[0]) { }
        public CStorageData(ulong _objectID, int _priority, bool _autoMode, CItemFilter[] _itemStates)
        {
            SaveName        = SaveAndLoad.CurrentGameFileName;
            ObjectID        = _objectID;
            Priority        = _priority;
            AutoMode        = _autoMode;
            mi_filters      = _itemStates;
            Filters         = mi_filters.ToDictionary(_o => _o.Index, _o => _o);
        }

        public CStorageData Copy()
        {
            return new CStorageData(ObjectID, Priority, AutoMode, mi_filters);
        }

        public void OnBeforeSerialize()
        {
            mi_filters = Filters.Select(_o => _o.Value).ToArray();
        }
    }

    [System.Serializable]
    public class CItemFilter
    {
        public int Index;
        public bool NoAmountControl = true;
        public int MaxAmount;

        public CItemFilter(int _index)
        {
            Index = _index;
        }
    }
}
