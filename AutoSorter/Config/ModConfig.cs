namespace pp.RaftMods.AutoSorter
{
    [System.Serializable]
    public class CModConfig
    {
        public int CheckIntervalSeconds;
        public bool Debug;
        public bool InitialHelpShown;
        public UpgradeCost[] UpgradeCosts;
        public bool TransferFromAutosorters;
        public float ReturnItemsOnDowngradeMultiplier;
        public int MaxSearchResultItems;
        public bool ChangeStorageColorOnUpgrade;

        public CModConfig()
        {
            CheckIntervalSeconds                = 2;
            MaxSearchResultItems                = 25;
            Debug                               = false;
            TransferFromAutosorters             = false;
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

        public static bool ExtraSettingsAPI_GetCheckboxState(string _settingName) => true;
        public static float ExtraSettingsAPI_GetSliderValue(string _settingName) => 0f;

        public static bool ExtraSettingsAPI_Loaded = false;

        public void ExtraSettingsAPI_Load()
        {
            ReloadSettings();
        }

        public void ExtraSettingsAPI_SettingsClose() => ReloadSettings();

        private void ReloadSettings()
        {
            CheckIntervalSeconds                = (int)ExtraSettingsAPI_GetSliderValue(nameof(CheckIntervalSeconds));
            MaxSearchResultItems                = (int)ExtraSettingsAPI_GetSliderValue(nameof(MaxSearchResultItems));
            Debug                               = ExtraSettingsAPI_GetCheckboxState(nameof(Debug));
            TransferFromAutosorters             = ExtraSettingsAPI_GetCheckboxState(nameof(TransferFromAutosorters));
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
TransferFromAS: {TransferFromAutosorters}
ReturnItems: {ReturnItemsOnDowngradeMultiplier}
MaxSearchResults: {MaxSearchResultItems}
ChangeColor: {ChangeStorageColorOnUpgrade}";
        }
    }

    [System.Serializable]
    public class UpgradeCost
    {
        public string Name;
        public int Amount;

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
    }
}