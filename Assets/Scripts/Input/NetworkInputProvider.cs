using ArrowGame.Common;
using UnityEngine;

namespace ArrowGame.Client {
	public class NetworkInputProvider : MonoBehaviour, IInputProvider {
		[SerializeField] private int _playerID;

		public InputState GetState() {
			throw new System.NotImplementedException();
		}
	}
}
