using UnityEngine;

namespace ArrowGame.Client {
	public class PlayerController : MonoBehaviour {
		[Header("Movement")]
		[SerializeField] private float _moveSpeed;

		private Rigidbody2D _rigidbody;
		private IInputProvider _provider;

		private void Awake() {
			_provider = GetComponent<IInputProvider>();
			_rigidbody = GetComponent<Rigidbody2D>();
		}

		private void Update() {
			var state = _provider.GetState();
			_rigidbody.velocity = new Vector3(state.HorizontalMovement * _moveSpeed, 0, 0);
		}
	}
}
