using System;
using ArrowGame.Common;
using TMPro;
using UnityEngine;

namespace ArrowGame.Client.UI {
	public class RoomStatusDisplay : MonoBehaviour {
		[SerializeField] private TextMeshProUGUI _infoText;

		private void Update() {
			var state = NetworkManager.Instance.RoomState;

			_infoText.text = state == RoomState.Waiting ? "유저 대기 중..." : "";
		}
	}
}
