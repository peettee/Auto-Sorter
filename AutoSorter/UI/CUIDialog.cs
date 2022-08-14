using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pp.RaftMods.AutoSorter
{
    /// <summary>
    /// Class representing a prompt for a player which is shown before performing certain actions.
    /// </summary>
    public class CUIDialog : MonoBehaviour
    {
        private TextMeshProUGUI mi_dialogText;
        private Button mi_okButton;
        private Button mi_cancelButton;

        private System.Action mi_currentInfoCallback;
        private System.Action<bool> mi_currentPromptCallback;
        private bool mi_dialogVisible;

        private void Awake()
        {
            mi_dialogText       = transform.Find("Content/Text_Dialog").GetComponent<TextMeshProUGUI>(); //from the scroll rect get viewport and then the content anchor to spawn item prefabs in

            mi_okButton         = transform.Find("Content/Controls/Button_Ok").GetComponent<Button>();
            mi_cancelButton     = transform.Find("Content/Controls/Button_Cancel").GetComponent<Button>();

            mi_okButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnOkButtonClicked));
            mi_cancelButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnCancelButtonClicked));
        }

        #region ENGINE_CALLBACKS
        private void Start()
        {
            gameObject.SetActive(false);
        }
        #endregion

        /// <summary>
        /// Hide the user dialog.
        /// </summary>
        public void Hide()
        {
            mi_dialogVisible = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows a dialog the user displaying the given message with an OK button only.
        /// </summary>
        /// <param name="_message">The message shown to the user.</param>
        /// <param name="_onConfirm">Callback which is invoked whenever the user presses the OK button.</param>
        public void ShowInfo(string _message, System.Action _onConfirm)
        {
            if (mi_dialogVisible)
            {
                CUtil.LogW("Dialog already visible. No queue implemented yet. Cannot show dialog: " + _message);
                return;
            }

            ShowDialog(_message);

            mi_cancelButton.gameObject.SetActive(false);
            mi_currentInfoCallback = _onConfirm;
        }

        /// <summary>
        /// Shows a dialog to the user requiring a Yes or No answer.
        /// </summary>
        /// <param name="_message">The message shown to the user.</param>
        /// <param name="_onResult">Callback invoked when the user confirms or declines the dialog providing a boolean indicating the users decision.</param>
        public void ShowPrompt(string _message, System.Action<bool> _onResult)
        {
            if (mi_dialogVisible)
            {
                CUtil.LogW("Dialog already visible. No queue implemented yet. Cannot show dialog: " + _message);
                return;
            }

            ShowDialog(_message);

            mi_cancelButton.gameObject.SetActive(true);
            mi_currentPromptCallback = _onResult;
        }

        private void ShowDialog(string _message)
        {
            mi_dialogVisible = true;
            gameObject.SetActive(true);

            CAutoSorter.Get.Sounds?.PlayUI_OpenMenu();

            mi_okButton.gameObject.SetActive(true);
            mi_dialogText.text = _message;
        }

        private void OnDestroy()
        {
            Hide();
        }

        private void OnOkButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_currentInfoCallback?.Invoke();
            mi_currentInfoCallback = null;

            mi_currentPromptCallback?.Invoke(true);
            mi_currentPromptCallback = null;

            Hide();
        }

        private void OnCancelButtonClicked()
        {
            CAutoSorter.Get.Sounds?.PlayUI_Click();

            mi_currentPromptCallback?.Invoke(false);
            mi_currentPromptCallback = null;

            Hide();
        }
    }
}