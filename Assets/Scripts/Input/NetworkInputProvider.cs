using ArrowGame.Common;
using UnityEngine;

namespace ArrowGame.Client {
	public class NetworkInputProvider : MonoBehaviour, IInputProvider {
		public InputState LastReceivedState;

		public InputState GetState() {
			return LastReceivedState;
		}
	}
}
