using UnityEngine;

namespace GK {
	public class Shoot : MonoBehaviour {

		public GameObject Projectile; 
		public float MinDelay = 0.25f;
		public float InitialSpeed = 10.0f;
		public Transform SpawnLocation;

		float lastShot = -1000.0f;

		void Update() {
			if (Input.GetButton("Fire1")) {

				if (Time.time - lastShot >= MinDelay) {
					lastShot = Time.time;

					var go = Instantiate(
						Projectile,
						SpawnLocation.position,
						SpawnLocation.rotation
					);

					var rb = go.GetComponent<Rigidbody>();
					rb.linearVelocity = SpawnLocation.forward * InitialSpeed;
				}
			}
		}
	}
}