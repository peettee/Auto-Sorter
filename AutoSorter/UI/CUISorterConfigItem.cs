using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// UI element for a single item in the auto-sorters configuration UI item list.
    /// </summary>
    public class CUISorterConfigItem : MonoBehaviour
    {
        /// <summary>
        /// The Raft item object this UI element represents.
        /// </summary>
        public Item_Base Item => mi_representsItem;

        public Toggle ItemToggle;
        private Image mi_itemImage;
        private TextMeshProUGUI mi_itemText;
        private Toggle mi_noAmountControlToggle;
        private TMP_InputField mi_maxAmountInput;

        private Item_Base mi_representsItem;
        private CSceneStorage mi_storage;

        private MenuType mi_previousMenuType;

        /// <summary>
        /// Load the UI element for the given item.
        /// </summary>
        /// <param name="_item">The raft item object this UI element will represent.</param>
        public void Load(Item_Base _item)
        {   
            mi_itemImage               = transform.Find("Item_Image").GetChild(0).GetComponent<Image>();
            mi_itemText                = transform.Find("Item_Name").GetComponent<TextMeshProUGUI>();
            ItemToggle                 = transform.Find("Item_Toggle").GetComponent<Toggle>();
            mi_noAmountControlToggle   = transform.Find("Toggle_Amount").GetComponent<Toggle>();
            mi_maxAmountInput          = transform.Find("Input_Amount").GetComponent<TMP_InputField>();

            mi_representsItem           = _item;
            mi_itemImage.sprite         = _item.settings_Inventory.Sprite;
            mi_itemText.text            = _item.settings_Inventory.DisplayName;

            ItemToggle.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnItemToggled));
            mi_noAmountControlToggle.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnAmountControlToggled));
            mi_maxAmountInput.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnMaxAmountValueChanged));

            mi_maxAmountInput.onSelect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputSelected));
            mi_maxAmountInput.onDeselect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputDeselected));
        }

        /// <summary>
        /// Load the storage configuration for this UI element. Setting the elements states to the configuration stored for the storage.
        /// </summary>
        /// <param name="_storage">The storage to load the item configuration state from.</param>
        public void LoadStorage(CSceneStorage _storage)
        {
            mi_storage      = _storage;

            if (_storage.Data?.Filters.ContainsKey(mi_representsItem.UniqueIndex) ?? false)
            {
                ItemToggle.SetIsOnWithoutNotify(true);
            }
            else
            {
                ItemToggle.SetIsOnWithoutNotify(false);
            }

            UpdateLayout();
        }

        private void OnItemToggled(bool _item)
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            if (!_item)
            {
                mi_storage.Data.Filters.Remove(mi_representsItem.UniqueIndex);
            }
            else if (!mi_storage.Data.Filters.ContainsKey(mi_representsItem.UniqueIndex))
            {
                mi_storage.Data.Filters.Add(mi_representsItem.UniqueIndex, new CItemFilter(mi_representsItem.UniqueIndex, mi_representsItem.UniqueName));
            }

            UpdateLayout();
        }

        private void OnAmountControlToggled(bool _controlAmount)
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            if (!mi_storage.Data.Filters.ContainsKey(mi_representsItem.UniqueIndex)) return;

            mi_storage.Data.Filters[mi_representsItem.UniqueIndex].NoAmountControl = _controlAmount;

            UpdateLayout();
        }

        private void OnMaxAmountValueChanged(string _value)
        {
            if (!int.TryParse(_value, 
                out int result)) return;

            if(result < 0)
            {
                mi_maxAmountInput.SetTextWithoutNotify("0");
                return;
            }

            if (!mi_storage.Data.Filters.ContainsKey(mi_representsItem.UniqueIndex)) return;
            mi_storage.Data.Filters[mi_representsItem.UniqueIndex].MaxAmount = result;
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

        private void UpdateLayout()
        {
            if (ItemToggle.isOn)
            {
                mi_noAmountControlToggle.gameObject.SetActive(true);
                mi_noAmountControlToggle.SetIsOnWithoutNotify(mi_storage.Data.Filters[mi_representsItem.UniqueIndex].NoAmountControl);
                if (mi_noAmountControlToggle.isOn)
                {
                    mi_maxAmountInput.gameObject.SetActive(false);
                }
                else
                {
                    mi_maxAmountInput.gameObject.SetActive(true);
                    mi_maxAmountInput.text = mi_storage.Data.Filters[mi_representsItem.UniqueIndex].MaxAmount.ToString();
                }
                return;
            }

            mi_noAmountControlToggle.gameObject.SetActive(false);
            mi_maxAmountInput.gameObject.SetActive(false);
        }
    }
}
