using Newtonsoft.Json;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Mod configuration class which represents user configuration for the mod and takes care of handling Extra Settings API mod compatibility.
    /// </summary>
    [System.Serializable]
    public class CModConfig
    {
        /// <summary>
        /// Interval in seconds in which storages check if items need to be transferred.
        /// </summary>
        public int CheckIntervalSeconds;
        /// <summary>
        /// If set to true, will display additional information in the console.
        /// </summary>
        public bool Debug;
        /// <summary>
        /// Set by the mod, whenever the initial help text on auto-sorters has been shown.
        /// </summary>
        public bool InitialHelpShown;
        /// <summary>
        /// Costs to upgrade a regular storage to an auto-sorter.
        /// </summary>
        public UpgradeCost[] UpgradeCosts;
        /// <summary>
        /// Multiplier applied when returning items to the player on downgrade.
        /// </summary>
        public float ReturnItemsOnDowngradeMultiplier;
        /// <summary>
        /// Maximum number of item search results in the auto-sorter configuration UI.
        /// </summary>
        public int MaxSearchResultItems;
        /// <summary>
        /// If true changes the storages color to red on upgrade. False will leave the color untouched.
        /// </summary>
        public bool ChangeStorageColorOnUpgrade;

        public CModConfig()
        {
            CheckIntervalSeconds                = 2;
            MaxSearchResultItems                = 25;
            Debug                               = false;
            ChangeStorageColorOnUpgrade         = true;
            ReturnItemsOnDowngradeMultiplier    = 0.5f;
            UpgradeCosts                        = new[]
            {
                new UpgradeCost("Plastic", 20),
                new UpgradeCost("Scrap", 10),
                new UpgradeCost("CircuitBoard", 6),
                new UpgradeCost("Battery", 1)
            };
        }

        #region EXTRA_SETTINGS_API_CALLBACKS
        public static bool ExtraSettingsAPI_GetCheckboxState(string _settingName) => true;
        public static float ExtraSettingsAPI_GetSliderValue(string _settingName) => 0f;

        public static bool ExtraSettingsAPI_Loaded = false;

        public void ExtraSettingsAPI_Load()
        {
            ReloadSettings();
        }

        public void ExtraSettingsAPI_SettingsClose() => ReloadSettings();
        #endregion

        private void ReloadSettings()
        {
            CheckIntervalSeconds                = (int)ExtraSettingsAPI_GetSliderValue(nameof(CheckIntervalSeconds));
            MaxSearchResultItems                = (int)ExtraSettingsAPI_GetSliderValue(nameof(MaxSearchResultItems));
            Debug                               = ExtraSettingsAPI_GetCheckboxState(nameof(Debug));
            ReturnItemsOnDowngradeMultiplier    = ExtraSettingsAPI_GetSliderValue(nameof(ReturnItemsOnDowngradeMultiplier));
            ChangeStorageColorOnUpgrade         = ExtraSettingsAPI_GetCheckboxState(nameof(ChangeStorageColorOnUpgrade));
            CUtil.LogD("Settings reload!\n" + this);
        }

        public override string ToString()
        {
            return $@"## Config ##
Check interval: {CheckIntervalSeconds}
Debug: {Debug}
InitialHelpShown: {InitialHelpShown}
ReturnItems: {ReturnItemsOnDowngradeMultiplier}
MaxSearchResults: {MaxSearchResultItems}
ChangeColor: {ChangeStorageColorOnUpgrade}";
        }
    }

    /// <summary>
    /// Upgrade cost class, used to represent the upgrade costs for auto-sorters in the user config file.
    /// </summary>
    [System.Serializable]
    public class UpgradeCost
    {
        public string Name;
        public int Amount;

        [JsonIgnore]
        public Item_Base Item;

        public UpgradeCost() { }
        public UpgradeCost(string _name, int _amount)
        {
            Name    = _name;
            Amount  = _amount;
        }

        public Cost ToCost()
        {
            return new Cost(ItemManager.GetItemByName(Name), Amount);
        }

        public void Load()
        {
            Item = ItemManager.GetItemByName(Name);
            if (!Item)
            {
                CUtil.LogW("Specified item \"" + Name + "\" in the config upgrade costs could not be found. Please check your config file. The item will be ignored.");
            }
            if(Amount <= 0)
            {
                CUtil.LogW("Item amount on item \"" + Name + "\" in the config upgrade costs is invalid. Please check your config file.");
            }
        }

    }
}