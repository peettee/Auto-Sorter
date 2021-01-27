using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.AutoSorter
{
    public class CUISorterConfigDialog : MonoBehaviour
    {
        private const int LOAD_ITEMS_PER_FRAME = 4;
        private const int MAX_DISPLAY_ITEMS = 50;

        private Transform mi_itemAnchor;
        private Transform mi_itemListAnchor;
        private Transform mi_searchAnchor;
        private Transform mi_upgradeOverlay;
        private Transform mi_initOverlay;

        private Button mi_downgradeButton;
        private Button mi_upgradeButton;

        private TextMeshProUGUI mi_sorterPriority;
        private Button mi_priorityIncrease;
        private Button mi_priorityDecrease;
        private TextMeshProUGUI mi_statusText;

        private TMP_InputField mi_inputField;

        private Toggle mi_optionAutoMode;
        private TextMeshProUGUI mi_upgradeOverlayText;

        private Dictionary<int, CUISorterConfigItem> mi_itemControls = new Dictionary<int, CUISorterConfigItem>();

        private CSceneStorage mi_currentStorage;

        private bool mi_isVisible;
        private bool mi_loaded;

        private Coroutine mi_itemCreateRoutine;
        private GameObject mi_itemAsset;

        private string mi_searchQuery = string.Empty;
        private MenuType mi_previousMenuType;

        private void Awake()
        {
            Transform contentRoot = transform.Find("Content");
            mi_itemListAnchor   = contentRoot.Find("ItemList");
            mi_itemAnchor       = mi_itemListAnchor.GetChild(0).GetChild(0); //from the scroll rect get viewport and then the content anchor to spawn item prefabs in

            mi_optionAutoMode   = contentRoot.Find("Options/AutomMode_Toggle").GetComponent<Toggle>();
            mi_downgradeButton  = contentRoot.Find("Options/Downgrade_Button").GetComponent<Button>();

            mi_sorterPriority   = contentRoot.Find("Priority/Text_Priority").GetComponent<TextMeshProUGUI>();
            mi_priorityDecrease = contentRoot.Find("Priority/Button_Decrease").GetComponent<Button>();
            mi_priorityIncrease = contentRoot.Find("Priority/Button_Increase").GetComponent<Button>();

            mi_searchAnchor     = contentRoot.Find("ItemSearch");
            mi_inputField       = mi_searchAnchor.Find("Input").GetComponent<TMP_InputField>();

            mi_upgradeOverlay   = contentRoot.Find("_Upgrade_Overlay");
            mi_initOverlay      = contentRoot.Find("_Initialize_Overlay");

            mi_statusText       = contentRoot.Find("Text_Status").GetComponent<TextMeshProUGUI>();

            mi_upgradeOverlayText = mi_upgradeOverlay.Find("Text_UpgradeMessage").GetComponent<TextMeshProUGUI>();
            mi_upgradeButton = mi_upgradeOverlay.Find("Button_Upgrade").GetComponent<Button>();

            mi_optionAutoMode.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnAutoModeToggled));
            mi_downgradeButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDowngradeButtonClicked));
            mi_upgradeButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnUpgradeButtonClicked));

            mi_priorityIncrease.onClick.AddListener(new UnityEngine.Events.UnityAction(OnPriorityIncreaseButtonClick));
            mi_priorityDecrease.onClick.AddListener(new UnityEngine.Events.UnityAction(OnPriorityDecreaseButtonClick));

            mi_inputField.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputValueChanged));
            mi_inputField.onSelect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputSelected));
            mi_inputField.onDeselect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputDeselected));

            mi_upgradeOverlayText.text = "Upgrading the storage to an autosorter will require:\n" + string.Join("\n", CAutoSorter.Config.UpgradeCosts.Select(_o => _o.Name + ": " + _o.Amount));
        }

        private void Start()
        {
            mi_initOverlay.gameObject.SetActive(true);
            mi_upgradeOverlay.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        public void Load(GameObject _itemPrefab)
        {
            mi_itemAsset = _itemPrefab;
        }

        public void Show(CSceneStorage _storage)
        {
            gameObject.SetActive(true);
            mi_isVisible = true;

            mi_currentStorage   = _storage;

            mi_searchQuery = "";
            mi_inputField.text = "";

            mi_searchAnchor.gameObject.SetActive(!(_storage.Data?.AutoMode ?? true));
            mi_itemListAnchor.gameObject.SetActive(!(_storage.Data?.AutoMode ?? true));
            mi_statusText.gameObject.SetActive(!(_storage.Data?.AutoMode ?? true));

            //if (!mi_loaded)
            //{
            //    if (mi_itemCreateRoutine != null)
            //    {
            //        StopCoroutine(mi_itemCreateRoutine);
            //    }
            //    mi_itemCreateRoutine = StartCoroutine(LoadItems());
            //} else 
            if (mi_loaded)
            {
                Reload();
            }

            if (!_storage.IsUpgraded)
            {
                mi_upgradeOverlay.gameObject.SetActive(true);
                return;
            }


            LoadWorkingData();

            mi_upgradeOverlay.gameObject.SetActive(false);
        }

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
            mi_optionAutoMode.SetIsOnWithoutNotify(mi_currentStorage.Data.AutoMode);
         
            UpdatePriorityLabel();
        }

        private void OnDestroy()
        {
            Hide();
        }

        public IEnumerator LoadItems()
        {
            CUtil.LogW("Loading items...");
            mi_initOverlay.gameObject.SetActive(true);

            int current = 0;
            var items = ItemManager.GetAllItems().ToArray();
            GameObject go;
            foreach (var item in items)
            {
                if (item.settings_Inventory == null) continue;
                if (mi_itemControls.ContainsKey(item.UniqueIndex)) continue;

                go = GameObject.Instantiate(mi_itemAsset, mi_itemAnchor, false);
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
            mi_initOverlay.gameObject.SetActive(false);
            CUtil.LogW("Done Loading items");
        }

        private IEnumerator ReloadItems()
        {
            mi_statusText.text = "Searching...";

            int current = 0;
            int visible = 0;
            bool vis;
            bool pre;
            foreach (var item in mi_itemControls)
            {
                vis = item.Value.Item.name.ToLower().Contains(mi_searchQuery);
                pre = item.Value.gameObject.activeSelf;
                item.Value.gameObject.SetActive(vis && visible < MAX_DISPLAY_ITEMS);
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
            else if(visible > MAX_DISPLAY_ITEMS)
            {
                mi_statusText.text = (visible - MAX_DISPLAY_ITEMS) + " match your query but are not displayed.\nNarrow down your search to display them.";
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

        private void OnUpgradeButtonClicked()
        {
            if (mi_currentStorage.AutoSorter.Upgrade())
            {
                mi_upgradeOverlay.gameObject.SetActive(false);
                LoadWorkingData();
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
                        mi_currentStorage.AutoSorter.Downgrade();
                    }
                });
        }

        private void OnAutoModeToggled(bool _isOn)
        {
            mi_currentStorage.Data.AutoMode = _isOn;

            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_searchAnchor.gameObject.SetActive(!_isOn);
            mi_itemListAnchor.gameObject.SetActive(!_isOn);
            mi_statusText.gameObject.SetActive(!_isOn);
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

        private void Reload()
        {
            if (mi_itemCreateRoutine != null)
            {
                StopCoroutine(mi_itemCreateRoutine);
            }
            mi_itemCreateRoutine = StartCoroutine(WaitAndReload());
        }

        private IEnumerator WaitAndReload()
        {
            yield return new WaitForSeconds(1f);
            yield return ReloadItems();
        }
    }
}