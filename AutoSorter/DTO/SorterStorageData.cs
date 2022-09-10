using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Class used to save auto-sorter configurations to disk whenever the world is saved.
    /// This data is loaded again whenever the player loads into the world and applied to storages.
    /// </summary>
    [System.Serializable]
    public class CSorterStorageData
    {
        /// <summary>
        /// Name of the world this storage was saved in.
        /// </summary>
        public string SaveName;
        /// <summary>
        /// The object ID of the storage used to reapply settings on load.
        /// </summary>
        public ulong ObjectID;
        /// <summary>
        /// Priority of the auto sorter as set by the player in the auto-sorter UI.
        /// </summary>
        public int Priority;
        /// <summary>
        /// Auto mode enabled state as set by the player in the auto-sorter UI.
        /// </summary>
        public bool AutoMode;
        /// <summary>
        /// Saved search query for this sorter.
        /// </summary>
        [DefaultValue("")]
        public string SearchQuery = "";
        /// <summary>
        /// Filters applied to the auto-sorter during runtime. Is not saved to the json data but loaded from the json data and used to easily and quickly access storage data during runtime.
        /// </summary>
        [JsonIgnore]
        public Dictionary<int, CItemFilter> Filters = new Dictionary<int, CItemFilter>();

        [JsonProperty(PropertyName = "Filters")]
        private CItemFilter[] mi_filters = new CItemFilter[0];

        public CSorterStorageData() { }
        public CSorterStorageData(ulong _objectID)
            : this(_objectID, 0, false, new CItemFilter[0]) { }
        public CSorterStorageData(ulong _objectID, int _priority, bool _autoMode, CItemFilter[] _itemStates)
        {
            SaveName        = SaveAndLoad.CurrentGameFileName;
            ObjectID        = _objectID;
            Priority        = _priority;
            AutoMode        = _autoMode;
            mi_filters      = _itemStates;
        }

        public CSorterStorageData Copy()
        {
            return new CSorterStorageData(ObjectID, Priority, AutoMode, mi_filters);
        }

        public void OnBeforeSerialize()
        {
            mi_filters = Filters.Select(_o => _o.Value).ToArray();
        }

        public void OnAfterDeserialize()
        {
            if (mi_filters != null)
            {
                Filters = mi_filters.ToDictionary(_o => _o.Index, _o => _o);
            }
        }
    }

    /// <summary>
    /// Item filter class representing the filter settings for an item set by the player in the auto-sorter UI.
    /// </summary>
    [System.Serializable]
    public class CItemFilter
    {
        /// <summary>
        /// Index of the item this filter is for.
        /// </summary>
        public int Index;
        /// <summary>
        /// Name of the item a filter has been set for in the auto-sorter UI.
        /// </summary>
        public string UniqueName;
        /// <summary>
        /// Determines whether specific amount control is enabled for the item.
        /// </summary>
        [DefaultValue(true)]
        public bool NoAmountControl = true;
        /// <summary>
        /// If specific amount control is enabled, specifies the maximum amount of items transferred by the auto-sorter.
        /// </summary>
        public int MaxAmount;

        public CItemFilter(int _index, string _name)
        {
            Index       = _index;
            UniqueName  = _name;
        }
    }
}
