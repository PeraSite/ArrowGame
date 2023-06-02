using TMPro;
using UnityEngine;

namespace ArrowGame.Client.UI {
	public class EndingPanel : MonoBehaviour {
		[SerializeField] private GameObject _endingPanel;
		[SerializeField] private TextMeshProUGUI _winnerText;

		private void Awake() {
			_endingPanel.SetActive(false);
		}

		public void ShowPanel(string text) {
			_endingPanel.SetActive(true);
			_winnerText.text = text;
		}

		public void QuitGame() {
#if UNITY_EDITOR
			UnityEditor.EditorApplication.isPlaying = false;
#else
  			Application.Quit();
#endif
		}
	}
}
