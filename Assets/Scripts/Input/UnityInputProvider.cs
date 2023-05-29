using ArrowGame.Common;
using UnityEngine;

namespace ArrowGame.Client {
	public class UnityInputProvider : MonoBehaviour, IInputProvider {
		[SerializeField] private NetworkManager _networkManager;

		public InputState GetState() {
			if (_networkManager.RoomState != RoomState.Playing)
				return new InputState {
					HorizontalMovement = 0f
				};

			var horizontal = Input.GetAxisRaw("Horizontal");
			return new InputState {
				HorizontalMovement = horizontal
			};
		}
	}
}
