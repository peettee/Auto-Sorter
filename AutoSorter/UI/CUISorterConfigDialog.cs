using pp.RaftMods.AutoSorter.Protocol;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Class representing the auto-sorter UI which is shown to the user whenever a storage is opened.
    /// </summary>
    public class CUISorterConfigDialog : MonoBehaviour
    {
        private const int LOAD_ITEMS_PER_FRAME = 4;

        private Transform mi_itemEntryAnchor;
        private Transform mi_itemAnchor;
        private Transform mi_content;
        private Transform mi_upgradeOverlay;
        private Transform mi_initOverlay;
        private Transform mi_helpOverlay;
          
        private Button mi_downgradeButton;
        private Button mi_upgradeButton;

        private TextMeshProUGUI mi_sorterPriority;
        private Button mi_priorityIncrease;
        private Button mi_priorityDecrease;
        private TextMeshProUGUI mi_statusText;
        private TextMeshProUGUI mi_helpText;

        private TMP_InputField mi_inputField;

        private Toggle mi_optionAutoMode;
        private TextMeshProUGUI mi_upgradeOverlayText;

        private Button mi_selectAllButton;
        private Button mi_deselectAllButton;
        private Button mi_selectFilteredButton;
        private Button mi_deselectFilteredButton;
        private Button mi_closeHelpButton;
        private Button mi_openHelpButton;

        private Button mi_showButton;
        private Button mi_hideButton;

        private Button mi_ignoreButton;
        private Button mi_includeButton;

        private Dictionary<int, CUISorterConfigItem> mi_itemControls = new Dictionary<int, CUISorterConfigItem>();

        private CSceneStorage mi_currentStorage;

        private bool mi_isVisible;
        private bool mi_loaded;

        private Coroutine mi_itemCreateRoutine;
        private GameObject mi_itemAsset;

        private string mi_searchQuery = string.Empty;
        private MenuType mi_previousMenuType;
        private bool mi_isHidden;

        private EAdditionalItemFilterType mi_additionalItemFilter;

        private void Awake()
        {
            mi_content          = transform.Find("Content");
            mi_upgradeOverlay   = transform.Find("_Upgrade_Overlay");
            mi_initOverlay      = transform.Find("_Initialize_Overlay");
            mi_helpOverlay      = transform.Find("_Help_Overlay");
            mi_showButton       = transform.Find("Button_Show").GetComponent<Button>();
            mi_hideButton       = transform.Find("Button_Hide").GetComponent<Button>();
            mi_ignoreButton     = transform.Find("Button_Ignore").GetComponent<Button>();
            mi_includeButton    = transform.Find("Button_Include").GetComponent<Button>();

            mi_optionAutoMode   = mi_content.Find("Options/AutomMode_Toggle").GetComponent<Toggle>();
            mi_downgradeButton  = mi_content.Find("Options/Downgrade_Button").GetComponent<Button>();
            mi_openHelpButton   = mi_content.Find("Options/Help_Button").GetComponent<Button>();

            mi_sorterPriority   = mi_content.Find("Priority/Text_Priority").GetComponent<TextMeshProUGUI>();
            mi_priorityDecrease = mi_content.Find("Priority/Button_Decrease").GetComponent<Button>();
            mi_priorityIncrease = mi_content.Find("Priority/Button_Increase").GetComponent<Button>();

            mi_itemAnchor       = mi_content.Find("Items");

            var itemList        = mi_itemAnchor.Find("ItemList");
            mi_itemEntryAnchor  = itemList.GetChild(0).GetChild(0); //from the scroll rect get viewport and then the content anchor to spawn item prefabs in

            var selectionAnchor         = mi_itemAnchor.Find("Selection");
            mi_selectAllButton          = selectionAnchor.Find("Button_Select_All").GetComponent<Button>();
            mi_deselectAllButton        = selectionAnchor.Find("Button_Deselect_All").GetComponent<Button>();
            mi_selectFilteredButton     = selectionAnchor.Find("Button_Select").GetComponent<Button>();
            mi_deselectFilteredButton   = selectionAnchor.Find("Button_Deselect").GetComponent<Button>();

            var searchAnchor        = mi_itemAnchor.Find("ItemSearch");
            mi_inputField           = searchAnchor.Find("Input").GetComponent<TMP_InputField>();

            mi_statusText           = mi_itemAnchor.Find("Text_Status").GetComponent<TextMeshProUGUI>();

            mi_upgradeOverlayText   = mi_upgradeOverlay.Find("Text_UpgradeMessage").GetComponent<TextMeshProUGUI>();
            mi_upgradeButton        = mi_upgradeOverlay.Find("Button_Upgrade").GetComponent<Button>();

            mi_helpText             = mi_helpOverlay
                                        .GetComponentInChildren<ScrollRect>()
                                        .GetComponentInChildren<TextMeshProUGUI>();
            mi_closeHelpButton      = mi_helpOverlay.GetComponentInChildren<Button>();

            mi_optionAutoMode.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnAutoModeToggled));
            mi_downgradeButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDowngradeButtonClicked));
            mi_upgradeButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnUpgradeButtonClicked));

            mi_priorityIncrease.onClick.AddListener(new UnityEngine.Events.UnityAction(OnPriorityIncreaseButtonClick));
            mi_priorityDecrease.onClick.AddListener(new UnityEngine.Events.UnityAction(OnPriorityDecreaseButtonClick));

            mi_inputField.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputValueChanged));
            mi_inputField.onSelect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputSelected));
            mi_inputField.onDeselect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputDeselected));

            mi_selectAllButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnSelectAllClicked));
            mi_deselectAllButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDeselectAllClicked));
            mi_selectFilteredButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnSelectFilteredClicked));
            mi_deselectFilteredButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDeselectFilteredClicked));

            mi_closeHelpButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnCloseHelpButtonClicked));
            mi_openHelpButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnOpenHelpButtonClicked));

            mi_showButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnShowButtonClicked));
            mi_hideButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnHideButtonClicked));

            mi_includeButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnIncludeButtonClick));
            mi_ignoreButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnIgnoreButtonClick));

            mi_upgradeOverlayText.text = "Upgrading the storage to an auto-sorter will require:\n" + string.Join("\n", CAutoSorter.Config.UpgradeCosts.Select(_o => _o.Name + ": " + _o.Amount));
        }

        private void Start()
        {
            mi_helpOverlay.gameObject.SetActive(false);
            mi_initOverlay.gameObject.SetActive(true);
            mi_showButton.gameObject.SetActive(false);
            mi_hideButton.gameObject.SetActive(true);
            mi_upgradeOverlay.gameObject.SetActive(false);

            gameObject.SetActive(false);
            mi_helpText.text = CAutoSorter.HelpText;
        }

        /// <summary>
        /// Sets the item prefab reference-
        /// </summary>
        /// <param name="_itemPrefab"></param>
        public void Load(GameObject _itemPrefab)
        {
            mi_itemAsset = _itemPrefab;
        }

        /// <summary>
        /// Shows the storage configuration dialog for the given storage to the user.
        /// </summary>
        /// <param name="_storage">The storage to display a the configuration UI for.</param>
        public void Show(CSceneStorage _storage)
        {
            gameObject.SetActive(true);
            mi_isVisible = true;

            mi_currentStorage = _storage;

            mi_searchQuery = "";
            mi_inputField.text = "";

            mi_itemAnchor.gameObject.SetActive(_storage.IsUpgraded && mi_loaded && !_storage.Data.AutoMode);

            if (!CAutoSorter.Config.InitialHelpShown)
            {
                mi_hideButton.gameObject.SetActive(false);
                mi_helpOverlay.gameObject.SetActive(true);
                CAutoSorter.Config.InitialHelpShown = true;
                CAutoSorter.Get.SaveConfig();
            }

            if (mi_loaded)
            {
                Reload();
            }

            mi_includeButton.gameObject.SetActive(mi_currentStorage.AdditionalData != null && mi_currentStorage.AdditionalData.Ignore);
            mi_ignoreButton.gameObject.SetActive(mi_currentStorage.AdditionalData == null || !mi_currentStorage.AdditionalData.Ignore);

            if (!_storage.IsUpgraded)
            {
                if (!mi_isHidden)
                {
                    mi_upgradeOverlay.gameObject.SetActive(true);
                }
                return;
            }

            if (mi_loaded)
            {
                LoadWorkingData();
            } 

            mi_upgradeOverlay.gameObject.SetActive(false);
        }

        /// <summary>
        /// Hides the storage configuration dialog.
        /// </summary>
        public void Hide()
        {
            mi_isVisible = false;
            gameObject.SetActive(false);
            if (mi_itemCreateRoutine != null)
            {
                StopCoroutine(mi_itemCreateRoutine);
            }
        }

        private void LoadWorkingData()
        {
            if(mi_optionAutoMode.isOn == mi_currentStorage.Data.AutoMode)
            {
                mi_itemAnchor.gameObject.SetActive(!mi_optionAutoMode.isOn); //make sure we set the UI state correctly as the callback is not called when the auto mode has not been changed.
            }
            mi_optionAutoMode.isOn = mi_currentStorage.Data.AutoMode; //trigger notify on purpose
            UpdatePriorityLabel();
        }

        private void OnDestroy()
        {
            Hide();
        }

        public IEnumerator LoadItems()
        {
            mi_initOverlay.gameObject.SetActive(true);

            int current = 0;
            var items = ItemManager.GetAllItems().ToArray();
            CUtil.LogD($"Loading UI elements for {items.Length} items...");
            GameObject go;
            foreach (var item in items)
            {
                if (item.settings_Inventory == null || 
                    item.settings_Inventory.DisplayName == "An item") continue; //ignore "An item" undefined items
                if (mi_itemControls.ContainsKey(item.UniqueIndex)) continue;
                go = GameObject.Instantiate(mi_itemAsset, mi_itemEntryAnchor, false);
                var cfgItem = go.AddComponent<CUISorterConfigItem>();
                if (!cfgItem)
                {
                    CUtil.LogE("Prefab error on sorter config item. Invalid prefab setup.");
                    yield break;
                }
                cfgItem.Load(item);
                mi_itemControls.Add(item.UniqueIndex, cfgItem);
                go.SetActive(false);
                ++current;

                if (current >= LOAD_ITEMS_PER_FRAME)
                {
                    current = 0;
                    yield return new WaitForEndOfFrame();
                }
            }

            if (mi_isVisible)
            {
                yield return ReloadItems();
            }

            mi_loaded = true;
            if (mi_isVisible && mi_currentStorage.IsUpgraded)
            {
                mi_itemAnchor.gameObject.SetActive(!mi_currentStorage.Data.AutoMode);
                LoadWorkingData();
            }
            mi_initOverlay.gameObject.SetActive(false);
            CUtil.LogD("Done loading items.");
        }

        private IEnumerator ReloadItems()
        {
            mi_statusText.text = "Searching...";

            var postProcessQuery = mi_searchQuery;
            mi_additionalItemFilter = EAdditionalItemFilterType.NONE;
            if (!string.IsNullOrEmpty(postProcessQuery))
            {
                if (postProcessQuery.ToLower().Contains("#active"))
                {
                    mi_additionalItemFilter |= EAdditionalItemFilterType.ACTIVE;
                    postProcessQuery = postProcessQuery.ToLower().Replace("#active", "").Trim();
                }
                if (postProcessQuery.ToLower().Contains("#inactive"))
                {
                    mi_additionalItemFilter |= EAdditionalItemFilterType.INACTIVE;
                    postProcessQuery = postProcessQuery.ToLower().Replace("#inactive", "").Trim();
                }
                if (postProcessQuery.ToLower().Contains("#amount"))
                {
                    mi_additionalItemFilter |= EAdditionalItemFilterType.AMOUNT_CONTROL;
                    postProcessQuery = postProcessQuery.ToLower().Replace("#amount", "").Trim();
                }
            }

            var exactMatch = postProcessQuery.StartsWith("\"");
            if(exactMatch)
            {
                postProcessQuery = postProcessQuery.Replace("\"", "");
            }

            int current = 0;
            int visible = 0;
            bool vis;
            bool pre;

            foreach (var item in mi_itemControls)
            {
                vis =   string.IsNullOrEmpty(mi_searchQuery)
                        ||
                        (
                            !string.IsNullOrEmpty(postProcessQuery)
                            && 
                            (
                                (
                                    !exactMatch
                                    &&
                                    (
                                        item.Value.Item.name.ToLower().Contains(postProcessQuery) ||
                                        item.Value.Item.settings_Inventory.DisplayName.ToLower().Contains(postProcessQuery)
                                    )
                                )
                                ||
                                (
                                    item.Value.Item.name.ToLower() == postProcessQuery ||
                                    item.Value.Item.settings_Inventory.DisplayName.ToLower() == postProcessQuery
                                )
                            )
                        )
                        || AdditionalFilterApplies(item.Value);
                pre = item.Value.gameObject.activeSelf;
                item.Value.gameObject.SetActive(vis && visible < CAutoSorter.Config.MaxSearchResultItems);
                item.Value.LoadStorage(mi_currentStorage);
                visible += vis ? 1 : 0;
                ++current;
                if (current >= LOAD_ITEMS_PER_FRAME)
                {
                    current = 0;
                    yield return new WaitForEndOfFrame();
                }
            }

            if(visible == 0)
            {
                mi_statusText.text = "No items match your current search query.";
            }
            else if(visible > CAutoSorter.Config.MaxSearchResultItems)
            {
                mi_statusText.text = (visible - CAutoSorter.Config.MaxSearchResultItems) + " match your query but are not displayed.\nNarrow down your search to display them.";
            }
            else if(mi_itemControls.Count > visible)
            {
                mi_statusText.text = (mi_itemControls.Count - visible) + " items do not match your query.";
            }
            else
            {
                mi_statusText.text = string.Empty;
            }
        }  

        private void UpdatePriorityLabel()
        {
            mi_sorterPriority.text = "Priority: " + mi_currentStorage.Data.Priority;
        }
        
        private void Reload()
        {
            if (mi_itemCreateRoutine != null)
            {
                StopCoroutine(mi_itemCreateRoutine);
            }
            mi_itemCreateRoutine = StartCoroutine(WaitAndReload());
        }

        #region UI_CALLBACKS
        private void OnUpgradeButtonClicked()
        {
            if (mi_currentStorage.AutoSorter.Upgrade())
            {
                CAutoSorter.Get.Sounds?.PlayUI_Click();
                mi_upgradeOverlay.gameObject.SetActive(false);
                LoadWorkingData();
            }
            else
            {
                CAutoSorter.Get.Sounds?.PlayUI_Click_Fail();
            }
        }

        private void OnDowngradeButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            CAutoSorter.Get.Dialog.ShowPrompt(
                "Are you sure you want to downgrade to a regular storage?",
                _result =>
                {
                    if (_result)
                    {
                        mi_upgradeOverlay.gameObject.SetActive(true);
                        mi_itemAnchor.gameObject.SetActive(false);
                        mi_currentStorage.AutoSorter.Downgrade();
                    }
                });
        }

        private void OnAutoModeToggled(bool _isOn)
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_currentStorage.Data.AutoMode = _isOn;

            mi_itemAnchor.gameObject.SetActive(!_isOn);
        }

        private void OnPriorityIncreaseButtonClick()
        {
            ++mi_currentStorage.Data.Priority;
            UpdatePriorityLabel();

            CAutoSorter.Get.Sounds?.PlayUI_Click();
        }

        private void OnPriorityDecreaseButtonClick()
        {
            --mi_currentStorage.Data.Priority;
            UpdatePriorityLabel();

            CAutoSorter.Get.Sounds?.PlayUI_Click();
        }
    
        private void OnInputValueChanged(string _input)
        {
            var trimmedInput = _input.Trim().ToLower();
            if (mi_searchQuery == trimmedInput)
            {
                return;
            }
            mi_searchQuery = trimmedInput;
            Reload();
        }

        private void OnInputDeselected(string _value)
        {
            CanvasHelper.ActiveMenu = mi_previousMenuType;
        }

        private void OnInputSelected(string _value)
        {
            mi_previousMenuType = CanvasHelper.ActiveMenu;
            CanvasHelper.ActiveMenu = MenuType.PauseMenu; //locks all input while the input field is focused
        }

        private void OnSelectAllClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            CAutoSorter.Get.Dialog.ShowPrompt("This will wipe your current selection.\nContinue?", 
            _result =>
            {
                if (!_result) return;
                foreach (var toggle in mi_itemControls)
                {
                    toggle.Value.ItemToggle.isOn = true;
                }
            });
        }

        private void OnDeselectAllClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            CAutoSorter.Get.Dialog.ShowPrompt("This will wipe your current selection.\nContinue?",
            _result =>
            {
                if (!_result) return;
                foreach (var toggle in mi_itemControls)
                {
                    toggle.Value.ItemToggle.isOn = false;
                }
            });
        }

        private void OnSelectFilteredClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            foreach (var toggle in mi_itemControls)
            {
                if (!toggle.Value.gameObject.activeSelf) continue;
                toggle.Value.ItemToggle.isOn = true;
            }
        }

        private void OnDeselectFilteredClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            foreach (var toggle in mi_itemControls)
            {
                if (!toggle.Value.gameObject.activeSelf) continue;
                toggle.Value.ItemToggle.isOn = false;
            }
        }
        
        private void OnCloseHelpButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_hideButton.gameObject.SetActive(true);
            mi_helpOverlay.gameObject.SetActive(false);
        }

        private void OnOpenHelpButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_hideButton.gameObject.SetActive(false);
            mi_helpOverlay.gameObject.SetActive(true);
        }

        private void OnHideButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_hideButton.gameObject.SetActive(false);
            mi_showButton.gameObject.SetActive(true);

            mi_includeButton.gameObject.SetActive(false);
            mi_ignoreButton.gameObject.SetActive(false);

            mi_helpOverlay.gameObject.SetActive(false);
            mi_initOverlay.gameObject.SetActive(false);
            mi_upgradeOverlay.gameObject.SetActive(false);

            mi_content.gameObject.SetActive(false);

            mi_isHidden = true;
        }

        private void OnShowButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_hideButton.gameObject.SetActive(true);
            mi_showButton.gameObject.SetActive(false);

            mi_includeButton.gameObject.SetActive(mi_currentStorage.AdditionalData != null && mi_currentStorage.AdditionalData.Ignore);
            mi_ignoreButton.gameObject.SetActive(mi_currentStorage.AdditionalData == null || !mi_currentStorage.AdditionalData.Ignore);

            if (!mi_loaded)
            {
                mi_initOverlay.gameObject.SetActive(true);
            }
            else if (!mi_currentStorage.IsUpgraded)
            {
                mi_upgradeOverlay.gameObject.SetActive(true);
            }

            mi_content.gameObject.SetActive(true);
            
            mi_isHidden = false;

        }
        private void OnIgnoreButtonClick()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_currentStorage.AdditionalData = new CGeneralStorageData(mi_currentStorage.AutoSorter.ObjectIndex, true);
            CAutoSorter.Get.Broadcast(new CDTO(EStorageRequestType.STORAGE_IGNORE_UPDATE, mi_currentStorage.AutoSorter.ObjectIndex) { AdditionalInfo = mi_currentStorage.AdditionalData });

            mi_includeButton.gameObject.SetActive(true);
            mi_ignoreButton.gameObject.SetActive(false);
        }

        private void OnIncludeButtonClick()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_currentStorage.AdditionalData = null;

            CAutoSorter.Get.Broadcast(new CDTO(EStorageRequestType.STORAGE_IGNORE_UPDATE, mi_currentStorage.AutoSorter.ObjectIndex) { AdditionalInfo = null });

            mi_includeButton.gameObject.SetActive(false);
            mi_ignoreButton.gameObject.SetActive(true);
        }
        #endregion

        private IEnumerator WaitAndReload()
        {
            yield return new WaitForSeconds(1f);
            yield return ReloadItems();
        }
    
        private bool AdditionalFilterApplies(CUISorterConfigItem _item)
        {
            return  mi_additionalItemFilter != EAdditionalItemFilterType.NONE
                    &&
                    (
                        ((mi_additionalItemFilter & EAdditionalItemFilterType.ACTIVE) != 0 && _item.ItemToggle.isOn) ||
                        ((mi_additionalItemFilter & EAdditionalItemFilterType.INACTIVE) != 0 && !_item.ItemToggle.isOn) ||
                        ((mi_additionalItemFilter & EAdditionalItemFilterType.AMOUNT_CONTROL) != 0 && !_item.AmountControlToggle.isOn)
                    );
        }

        [System.Flags]
        private enum EAdditionalItemFilterType
        {
            NONE            = 0,
            ACTIVE          = 1,
            INACTIVE        = 1 << 1,
            AMOUNT_CONTROL  = 1 << 2
        }
    }
}