﻿using TMPro;
using UnityEngine;

namespace ArrowGame.Client {
	public class PlayerIdDisplay : MonoBehaviour {
		[SerializeField] private TextMeshPro _text;

		public void Init(int playerId) {
			_text.text = $"{playerId + 1}";
		}
	}
}
