/**
 * Copyright 2019 Oskar Sigvardsson
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GK {
	public class BreakableSurface : MonoBehaviour {
		const float BoundaryEpsilon = 0.0001f;

		public MeshFilter Filter     { get; private set; }
		public MeshRenderer Renderer { get; private set; }
		public MeshCollider Collider { get; private set; }
		public Rigidbody Rigidbody   { get; private set; }

		public List<Vector2> Polygon;
		public float Thickness = 1.0f;
		public float MinBreakArea = 0.01f;
		public float MinImpactToBreak = 50.0f;

		[Header("Legacy / Default")]
		public float ImpactRadius = 0.5f;

		[Header("Impact Force Mapping")]
		public float MaxImpactForce = 300.0f;

		[Header("Dynamic Radius")]
		public float MinImpactRadius = 0.20f;
		public float MaxImpactRadius = 1.20f;

		[Header("Seed Count")]
		public int SeedCount = 100;

		[Header("Strauss Process")]
        [SerializeField, Range(0f, 1f)] public float StraussGamma = 0.2f;
		public float HardCoreDistance = 0.15f;
		public float ObservationRadius = 2f;
		public int StraussMcmcSweeps = 30;
		public bool StraussIncludeImpactCenter = false;

		[Header("Crack Style")]
		public bool TemperedGlassCrackAllEdges = false;

		float _Area = -1.0f;

		int age;

		private enum ShardCategory {
			Outside,
			Inside,
			Mixed
		}

		private struct SheetPiece {
			public List<Vector2> Polygon;
			public ShardCategory Category;

			public SheetPiece(List<Vector2> polygon, ShardCategory category) {
				Polygon = polygon;
				Category = category;
			}
		}

		private static ShardCategory CategorizeShard(Vector2 impactPos, float radius, IList<Vector2> polygon) {
			if (polygon == null || polygon.Count < 3 || radius <= 0f) {
				return ShardCategory.Outside;
			}

			int insideCount = 0;
			float radiusSq = radius * radius;

			for (int i = 0; i < polygon.Count; i++) {
				if ((polygon[i] - impactPos).sqrMagnitude <= radiusSq) {
					insideCount++;
				}
			}

			int n = polygon.Count;

			if (insideCount >= n - 1) {
				return ShardCategory.Inside;
			}

			if (insideCount > 0) {
				return ShardCategory.Mixed;
			}

			return ShardCategory.Outside;
		}

		static bool PointInPolygon(Vector2 p, IList<Vector2> polygon) {
			bool inside = false;

			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++) {
				Vector2 pi = polygon[i];
				Vector2 pj = polygon[j];

				bool intersect =
					((pi.y > p.y) != (pj.y > p.y)) &&
					(p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + Mathf.Epsilon) + pi.x);

				if (intersect) {
					inside = !inside;
				}
			}

			return inside;
		}

		static bool SegmentIntersectsCircle(Vector2 a, Vector2 b, Vector2 center, float radius) {
			Vector2 ab = b - a;
			Vector2 ac = center - a;

			float abLenSq = ab.sqrMagnitude;
			if (abLenSq <= Mathf.Epsilon) {
				return (a - center).sqrMagnitude <= radius * radius;
			}

			float t = Mathf.Clamp01(Vector2.Dot(ac, ab) / abLenSq);
			Vector2 closest = a + t * ab;

			return (closest - center).sqrMagnitude <= radius * radius;
		}

		public float Area {
			get {
				if (_Area < 0.0f) {
					_Area = Geom.Area(Polygon);
				}

				return _Area;
			}
		}

		void Start() {
			age = 0;
			Reload();
		}

		public void Reload() {
			var pos = transform.position;

			if (Filter == null) Filter = GetComponent<MeshFilter>();
			if (Renderer == null) Renderer = GetComponent<MeshRenderer>();
			if (Collider == null) Collider = GetComponent<MeshCollider>();
			if (Rigidbody == null) Rigidbody = GetComponent<Rigidbody>();

			if (Polygon.Count == 0) {
				// Assume it's a cube with localScale dimensions
				var scale = 0.5f * transform.localScale;

				Polygon.Add(new Vector2(-scale.x, -scale.y));
				Polygon.Add(new Vector2(scale.x, -scale.y));
				Polygon.Add(new Vector2(scale.x, scale.y));
				Polygon.Add(new Vector2(-scale.x, scale.y));

				Thickness = 2.0f * scale.z;

				transform.localScale = Vector3.one;
			}

			var mesh = MeshFromPolygon(Polygon, Thickness);

			Filter.sharedMesh = mesh;
			Collider.sharedMesh = mesh;
		}

		void FixedUpdate() {
			var pos = transform.position;

			age++;
			if (pos.magnitude > 1000.0f) {
				DestroyImmediate(gameObject);
			}
		}

		void OnCollisionEnter(Collision coll) {
			if (age > 5 && coll.impulse.magnitude > MinImpactToBreak) {
				if (coll.contactCount == 0) {
					return;
				}

				var pnt = coll.contacts[0].point;
				float impactForce = coll.impulse.magnitude;

				Break((Vector2)transform.InverseTransformPoint(pnt), impactForce);
			}
		}

		static float NormalizedRandom(float mean, float stddev) {
			var u1 = UnityEngine.Random.value;
			var u2 = UnityEngine.Random.value;

			var randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) *
				Mathf.Sin(2.0f * Mathf.PI * u2);

			return mean + stddev * randStdNormal;
		}

		Vector2[] GenerateImpactSites(Vector2 center) {
			return ImpactSiteGenerator.GenerateStraussImpactSites(
				center,
				SeedCount,
				ObservationRadius,
				HardCoreDistance,
				StraussGamma,
				StraussMcmcSweeps,
				StraussIncludeImpactCenter
			);
		}

		static void AddRing(List<Vector2> sites, Vector2 center, float radius, int count, float jitter) {
			if (count <= 0) {
				return;
			}

			float angleOffset = Random.value * Mathf.PI * 2f;

			for (int i = 0; i < count; i++) {
				float angle = angleOffset + (i / (float)count) * Mathf.PI * 2f;
				float r = radius + Random.Range(-jitter, jitter);

				sites.Add(center + new Vector2(
					Mathf.Cos(angle) * r,
					Mathf.Sin(angle) * r
				));
			}
		}

		public void Break(Vector2 position, float impactForce) {
			var area = Area;
			if (area > MinBreakArea) {
				float t = Mathf.InverseLerp(MinImpactToBreak, MaxImpactForce, impactForce);
				float dynamicImpactRadius = Mathf.Lerp(MinImpactRadius, MaxImpactRadius, t);

				Debug.Log($"[Break] Impact at {position}, Force={impactForce:F2}, Radius={dynamicImpactRadius:F2}, t={t:F2}, Area={area:F3}");

				var outerPolygon = new List<Vector2>(Polygon);

				var calc = new VoronoiCalculator();
				var clip = new VoronoiClipper();
				var sites = GenerateImpactSites(position);

				var diagram = calc.CalculateDiagram(sites);

				var clipped = new List<Vector2>();
				var remainingPieces = new List<SheetPiece>();
				float remainingArea = 0.0f;
				int emptyCellCount = 0;
				int tooSmallCellCount = 0;
				int processedCellCount = 0;

				int insideFragmentCount = 0;
				int outsideFragmentCount = 0;
				int mixedFragmentCount = 0;

				for (int i = 0; i < sites.Length; i++) {
					clip.ClipSite(diagram, Polygon, i, ref clipped);

					if (clipped.Count == 0) {
						emptyCellCount++;
						continue;
					}

					if (clipped.Count < 3) {
						emptyCellCount++;
						Debug.Log($"Degenerate cell {i}: verts={clipped.Count}");
						continue;
					}

					var childArea = Mathf.Abs(Geom.Area(clipped));
					if (childArea <= MinBreakArea) {
						tooSmallCellCount++;
						Debug.Log($"Too small cell {i}: area={childArea:F4}, verts={clipped.Count}");
						continue;
					}

					processedCellCount++;
					var category = CategorizeShard(position, dynamicImpactRadius, clipped);

					switch (category) {
						case ShardCategory.Inside:
							insideFragmentCount++;
							CreateShardFragment(clipped, area, childArea);
							break;

						case ShardCategory.Outside:
							outsideFragmentCount++;
							remainingArea += childArea;
							remainingPieces.Add(new SheetPiece(new List<Vector2>(clipped), ShardCategory.Outside));
							break;

						case ShardCategory.Mixed:
							mixedFragmentCount++;
							remainingArea += childArea;
							remainingPieces.Add(new SheetPiece(new List<Vector2>(clipped), ShardCategory.Mixed));
							break;
					}
				}

				if (remainingPieces.Count > 0) {
					Debug.Log($"CreateRemainingSheet input: outside={outsideFragmentCount}, mixed={mixedFragmentCount}");
					CreateRemainingSheet(remainingPieces, area, remainingArea, position, dynamicImpactRadius, outerPolygon);
				}

				Debug.Log($"[Step 6] Fragments by category: {insideFragmentCount} Inside, {outsideFragmentCount} Outside, {mixedFragmentCount} Mixed");
				Debug.Log($"[Step 8] Remaining sheet merged from {remainingPieces.Count} attached cells");
				Debug.Log($"Cells summary: processed={processedCellCount}, empty={emptyCellCount}, tooSmall={tooSmallCellCount}");

				Destroy(gameObject);
			}
		}

		void CreateShardFragment(List<Vector2> fragmentPolygon, float totalArea, float childArea) {
			var newGo = Instantiate(gameObject, transform.parent);
			newGo.transform.localPosition = transform.localPosition;
			newGo.transform.localRotation = transform.localRotation;

			var bs = newGo.GetComponent<BreakableSurface>();
			bs.Thickness = Thickness;
			bs.Polygon.Clear();
			bs.Polygon.AddRange(fragmentPolygon);

			var rb = bs.GetComponent<Rigidbody>();
			rb.mass = Rigidbody.mass * (childArea / totalArea);
		}

		void CreateRemainingSheet(List<SheetPiece> pieces, float totalArea, float remainingArea, Vector2 impactCenter, float impactRadius, List<Vector2> outerPolygon) {
			var newGo = Instantiate(gameObject, transform.parent);
			newGo.transform.localPosition = transform.localPosition;
			newGo.transform.localRotation = transform.localRotation;

			var bs = newGo.GetComponent<BreakableSurface>();
			bs.enabled = false;

			var filter = newGo.GetComponent<MeshFilter>();
			var collider = newGo.GetComponent<MeshCollider>();
			var rb = newGo.GetComponent<Rigidbody>();

			var combined = MeshFromPieces(pieces, Thickness, impactCenter, impactRadius, outerPolygon, TemperedGlassCrackAllEdges);
			filter.sharedMesh = combined;
			collider.sharedMesh = combined;
			collider.convex = false;
			rb.isKinematic = true;
			rb.useGravity = false;
			rb.mass = Rigidbody.mass * (remainingArea / totalArea);
		}

		static Mesh MeshFromPieces(List<SheetPiece> pieces, float thickness, Vector2 impactCenter, float impactRadius, IList<Vector2> outerPolygon, bool forceCrackedEdges) {
			var combined = new Mesh();
			if (pieces.Count == 0) {
				return combined;
			}

			var combine = new CombineInstance[pieces.Count];
			for (int i = 0; i < pieces.Count; i++) {
				var partMesh = MeshFromPolygon(
					pieces[i].Polygon,
					thickness,
					impactCenter,
					impactRadius,
					outerPolygon,
					pieces[i].Category,
					forceCrackedEdges
				);
				combine[i].mesh = partMesh;
				combine[i].transform = Matrix4x4.identity;
			}

			combined.CombineMeshes(combine, true, false);

			for (int i = 0; i < combine.Length; i++) {
				if (combine[i].mesh != null) {
					Destroy(combine[i].mesh);
				}
			}

			return combined;
		}

		static Mesh MeshFromPolygon(List<Vector2> polygon, float thickness) {
			return MeshFromPolygon(polygon, thickness, Vector2.zero, -1.0f, null, ShardCategory.Inside, false);
		}

		static Mesh MeshFromPolygon(List<Vector2> polygon, float thickness, Vector2 impactCenter, float impactRadius, IList<Vector2> outerPolygon, ShardCategory category, bool forceCrackedEdges) {
			var count = polygon.Count;
			var verts = new List<Vector3>(6 * count);
			var norms = new List<Vector3>(6 * count);
			var tris = new List<int>(3 * (4 * count - 4));
			// TODO: add UVs

			bool useImpactFiltering = impactRadius >= 0.0f && outerPolygon != null;

			var ext = 0.5f * thickness;

			// Top
			var topStart = verts.Count;
			for (int i = 0; i < count; i++) {
				verts.Add(new Vector3(polygon[i].x, polygon[i].y, ext));
				norms.Add(Vector3.forward);
			}

			// Bottom
			var bottomStart = verts.Count;
			for (int i = 0; i < count; i++) {
				verts.Add(new Vector3(polygon[i].x, polygon[i].y, -ext));
				norms.Add(Vector3.back);
			}

			for (int vert = 2; vert < count; vert++) {
				tris.Add(topStart);
				tris.Add(topStart + vert - 1);
				tris.Add(topStart + vert);
			}

			for (int vert = 2; vert < count; vert++) {
				tris.Add(bottomStart);
				tris.Add(bottomStart + vert);
				tris.Add(bottomStart + vert - 1);
			}

			// Sides (conditionally generated for reconstructed sheet)
			int generatedWallCount = 0;
			for (int vert = 0; vert < count; vert++) {
				var iNext = vert == count - 1 ? 0 : vert + 1;
				var a = polygon[vert];
				var b = polygon[iNext];

				bool generateWall = forceCrackedEdges;
				if (useImpactFiltering) {
					bool onOuterBoundary = EdgeOnOuterBoundary(a, b, outerPolygon, BoundaryEpsilon);
					bool aInside = VertexInsideImpact(a, impactCenter, impactRadius);
					bool bInside = VertexInsideImpact(b, impactCenter, impactRadius);

					if (!forceCrackedEdges) {
						switch (category) {
							case ShardCategory.Outside:
								generateWall = onOuterBoundary;
								break;

							case ShardCategory.Mixed:
								generateWall = onOuterBoundary || aInside || bInside;
								break;

							default:
								generateWall = true;
								break;
						}
					}
				}

				if (!generateWall) {
					continue;
				}

				var si = verts.Count;
				verts.Add(new Vector3(a.x, a.y, ext));
				verts.Add(new Vector3(a.x, a.y, -ext));
				verts.Add(new Vector3(b.x, b.y, -ext));
				verts.Add(new Vector3(b.x, b.y, ext));

				var norm = Vector3.Cross(b - a, Vector3.forward).normalized;
				norms.Add(norm);
				norms.Add(norm);
				norms.Add(norm);
				norms.Add(norm);

				tris.Add(si);
				tris.Add(si + 1);
				tris.Add(si + 2);
				tris.Add(si);
				tris.Add(si + 2);
				tris.Add(si + 3);
				generatedWallCount++;
			}

			var mesh = new Mesh();

			mesh.SetVertices(verts);
			mesh.SetTriangles(tris, 0);
			mesh.SetNormals(norms);

			return mesh;
		}

		static bool VertexInsideImpact(Vector2 vertex, Vector2 impactCenter, float impactRadius) {
			var dx = vertex.x - impactCenter.x;
			var dy = vertex.y - impactCenter.y;
			return dx * dx + dy * dy <= impactRadius * impactRadius;
		}

		static bool EdgeOnOuterBoundary(Vector2 a, Vector2 b, IList<Vector2> outerPolygon, float epsilon) {
			if (outerPolygon == null || outerPolygon.Count < 2) {
				return false;
			}

			for (int i = 0; i < outerPolygon.Count; i++) {
				int j = (i == outerPolygon.Count - 1) ? 0 : i + 1;
				Vector2 c = outerPolygon[i];
				Vector2 d = outerPolygon[j];

				if (PointOnSegment(a, c, d, epsilon) && PointOnSegment(b, c, d, epsilon)) {
					return true;
				}
			}

			return false;
		}

		static bool PointOnSegment(Vector2 p, Vector2 a, Vector2 b, float epsilon) {
			Vector2 ab = b - a;
			Vector2 ap = p - a;

			float cross = ab.x * ap.y - ab.y * ap.x;
			if (Mathf.Abs(cross) > epsilon) {
				return false;
			}

			float dot = Vector2.Dot(ap, ab);
			if (dot < -epsilon) {
				return false;
			}

			float abLenSq = ab.sqrMagnitude;
			if (dot > abLenSq + epsilon) {
				return false;
			}

			return true;
		}

		static bool ApproximatelyEqual(Vector2 a, Vector2 b, float epsilon) {
			return (a - b).sqrMagnitude <= epsilon * epsilon;
		}
	}
}