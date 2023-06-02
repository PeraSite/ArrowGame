using UnityEngine;

namespace ArrowGame.Client {
	public class Arrow : MonoBehaviour {
		[SerializeField] private float _destroyY;

		private float _fallingSpeed;

		public void Init(float fallingSpeed) {
			_fallingSpeed = fallingSpeed;
		}

		private void Update() {
			transform.Translate(0, -_fallingSpeed * Time.deltaTime, 0);

			if (transform.position.y <= _destroyY) {
				Destroy(gameObject);
			}

		}

		private void OnTriggerEnter2D(Collider2D other) {
			if (other.CompareTag("Player")) {
				Destroy(gameObject);
				// TODO: 플레이어에게 데미지를 입히는 로직을 구현
			}
		}
	}
}
