using UnityEngine;

namespace ArrowGame.Client {
	public class PlayerController : MonoBehaviour {
		[Header("Movement")]
		[SerializeField] private float _moveSpeed;

		private IInputProvider _provider;

		private void Awake() {
			_provider = GetComponent<IInputProvider>();
		}

		private void Update() {
			var state = _provider.GetState();
			transform.position += new Vector3(state.HorizontalMovement * _moveSpeed * Time.deltaTime, 0, 0);
		}
	}
}
