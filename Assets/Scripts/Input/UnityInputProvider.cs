using ArrowGame.Common;
using UnityEngine;

namespace ArrowGame.Client {
	public class UnityInputProvider : MonoBehaviour, IInputProvider {
		public InputState GetState() {
			var horizontal = Input.GetAxisRaw("Horizontal");
			return new InputState {
				HorizontalMovement = horizontal
			};
		}
	}
}
