using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.AutoSorter
{
    public class CUISorterConfigItem : MonoBehaviour
    {
        public Item_Base Item => mi_representsItem;

        public Image ItemImage;
        public TextMeshProUGUI ItemText;
        public Toggle ItemToggle;
        public Toggle NoAmountControlToggle;
        public TMP_InputField MaxAmountInput;

        private Item_Base mi_representsItem;
        private CSceneStorage mi_storage;

        private MenuType mi_previousMenuType;

        public void Load(Item_Base _item)
        {   
            ItemImage               = transform.Find("Item_Image").GetChild(0).GetComponent<Image>();
            ItemText                = transform.Find("Item_Name").GetComponent<TextMeshProUGUI>();
            ItemToggle              = transform.Find("Item_Toggle").GetComponent<Toggle>();
            NoAmountControlToggle   = transform.Find("Toggle_Amount").GetComponent<Toggle>();
            MaxAmountInput          = transform.Find("Input_Amount").GetComponent<TMP_InputField>();

            mi_representsItem   = _item;
            ItemImage.sprite    = _item.settings_Inventory.Sprite;
            ItemText.text       = _item.settings_Inventory.DisplayName;

            ItemToggle.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnItemToggled));
            NoAmountControlToggle.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<bool>(OnAmountControlToggled));
            MaxAmountInput.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnMaxAmountValueChanged));

            MaxAmountInput.onSelect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputSelected));
            MaxAmountInput.onDeselect.AddListener(new UnityEngine.Events.UnityAction<string>(OnInputDeselected));
        }

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
                MaxAmountInput.SetTextWithoutNotify("0");
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

        public void UpdateLayout()
        {
            if (ItemToggle.isOn)
            {
                NoAmountControlToggle.gameObject.SetActive(true);
                NoAmountControlToggle.SetIsOnWithoutNotify(mi_storage.Data.Filters[mi_representsItem.UniqueIndex].NoAmountControl);
                if (NoAmountControlToggle.isOn)
                {
                    MaxAmountInput.gameObject.SetActive(false);
                }
                else
                {
                    MaxAmountInput.gameObject.SetActive(true);
                    MaxAmountInput.text = mi_storage.Data.Filters[mi_representsItem.UniqueIndex].MaxAmount.ToString();
                }
                return;
            }

            NoAmountControlToggle.gameObject.SetActive(false);
            MaxAmountInput.gameObject.SetActive(false);
        }
    }
}
