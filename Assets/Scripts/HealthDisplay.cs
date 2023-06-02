using System;
using UnityEngine;
using UnityEngine.UI;

namespace ArrowGame.Client {
	public class HealthDisplay : MonoBehaviour {
		[SerializeField] private float _maxHealth = 100f;
		[SerializeField] private Image _barImage;

		public void SetHealth(float health) {
			var fillAmount = health / _maxHealth;
			_barImage.fillAmount = Math.Clamp(fillAmount, 0f, 1f);
		}
	}
}
