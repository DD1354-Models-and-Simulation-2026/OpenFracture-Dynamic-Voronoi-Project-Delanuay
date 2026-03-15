using System.Collections.Generic;
using UnityEngine;

namespace GK
{
    [RequireComponent(typeof(Collider))]
    public class BreakableSurfaceImpactPainter : MonoBehaviour
    {
        private enum SeedPattern
        {
            RingJittered,
            RadialScatter,
            StraussProcess
        }

        [Header("Impact")]
        [SerializeField] private float impactRadius = 0.4f;

        [Header("Impact Visuals")]
        [SerializeField] private Color impactDotColor = Color.red;
        [SerializeField] private Color impactCircleColor = Color.red;
        [SerializeField] private float impactDotRadius = 0.03f;
        [SerializeField] private float impactCircleLineWidth = 0.01f;
        [SerializeField] private int impactCircleSegments = 96;

        [Header("Seed Visuals")]
        [SerializeField] private bool drawSeeds = true;
        [SerializeField] private Color seedColor = Color.yellow;
        [SerializeField] private float seedRadius = 0.02f;

        [Header("Cell Visuals")]
        [SerializeField] private bool drawCells = true;
        [SerializeField] private Color cellColor = Color.cyan;
        [SerializeField] private float cellLineWidth = 0.0075f;

        [Header("Seed Pattern")]
        [SerializeField] private SeedPattern seedPattern = SeedPattern.RingJittered;

        [Header("Ring Jittered Settings")]
        [SerializeField] private float seedJitterFactor = 0.2f;

        [Header("Radial Scatter Settings")]
        [SerializeField] private float radialMean = 0.9f;
        [SerializeField] private float radialStdDev = 0.45f;
        [SerializeField] private float radialMinFactor = 0.15f;
        [SerializeField] private float radialMaxFactor = 2.25f;

        [Header("Strauss Process Settings")]
        [SerializeField] private int StraussTargetSeedCount = 100;
        [SerializeField, Range(0f, 1f)] public float StraussGamma = 0.2f;
		public float HardCoreDistance = 0.15f;
		public float ObservationRadius = 2f;
		public int StraussMcmcSweeps = 30;
		public bool StraussIncludeImpactCenter = false;

        [Header("Collision Filter")]
        [SerializeField] private float minImpactToPaint = 0f;
        [SerializeField] private int minAgeFrames = 2;

        [Header("Sheet")]
        [SerializeField] private float surfaceOffset = 0.002f;

        private Transform debugRoot;
        private int age;

        private void Awake()
        {
            CreateDebugRoot();
        }

        private void FixedUpdate()
        {
            age++;
        }

        private void OnCollisionEnter(Collision coll)
        {
            if (age < minAgeFrames)
                return;

            if (coll.impulse.magnitude < minImpactToPaint)
                return;

            if (coll.contactCount == 0)
                return;

            Vector3 worldPoint = coll.contacts[0].point;
            Vector2 sheetImpact = WorldToSheetPoint(worldPoint);

            PaintImpact(sheetImpact);
        }

        public void PaintImpact(Vector2 sheetImpact)
        {
            ClearDebug();

            List<Vector2> sheetPolygon = BuildDefaultSheetPolygon();
            Vector2[] sites = GeneratePaintSites(sheetImpact, impactRadius);

            CreateImpactDot(sheetImpact);
            CreateImpactCircle(sheetImpact, impactRadius);

            if (drawSeeds)
                DrawSeeds(sites);

            if (drawCells)
                DrawVoronoiCells(sheetPolygon, sites);
        }

        public void ClearDebug()
        {
            if (debugRoot == null)
                return;

            for (int i = debugRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(debugRoot.GetChild(i).gameObject);
            }
        }

        private void CreateDebugRoot()
        {
            if (debugRoot != null)
                return;

            GameObject root = new GameObject($"{name}_ImpactDebug");
            debugRoot = root.transform;
        }

        private Vector2 WorldToSheetPoint(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 scale = transform.lossyScale;

            float sx = Mathf.Abs(scale.x) > Mathf.Epsilon ? Mathf.Abs(scale.x) : 1f;
            float sy = Mathf.Abs(scale.y) > Mathf.Epsilon ? Mathf.Abs(scale.y) : 1f;

            return new Vector2(local.x * sx, local.y * sy);
        }

        private Vector3 SheetPointToWorld(Vector2 sheetPoint, float zOffset)
        {
            Vector3 scale = transform.lossyScale;

            float sx = Mathf.Abs(scale.x) > Mathf.Epsilon ? Mathf.Abs(scale.x) : 1f;
            float sy = Mathf.Abs(scale.y) > Mathf.Epsilon ? Mathf.Abs(scale.y) : 1f;

            Vector3 local = new Vector3(sheetPoint.x / sx, sheetPoint.y / sy, zOffset);
            return transform.TransformPoint(local);
        }

        private List<Vector2> BuildDefaultSheetPolygon()
        {
            var meshFilter = GetComponent<MeshFilter>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return new List<Vector2>
                {
                    new Vector2(-0.5f, -0.5f),
                    new Vector2( 0.5f, -0.5f),
                    new Vector2( 0.5f,  0.5f),
                    new Vector2(-0.5f,  0.5f),
                };
            }

            Bounds bounds = meshFilter.sharedMesh.bounds;
            Vector3 scale = transform.lossyScale;

            float sx = Mathf.Abs(scale.x);
            float sy = Mathf.Abs(scale.y);

            float minX = bounds.min.x * sx;
            float maxX = bounds.max.x * sx;
            float minY = bounds.min.y * sy;
            float maxY = bounds.max.y * sy;

            return new List<Vector2>
            {
                new Vector2(minX, minY),
                new Vector2(maxX, minY),
                new Vector2(maxX, maxY),
                new Vector2(minX, maxY),
            };
        }

        private void CreateImpactDot(Vector2 sheetImpact)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "ImpactDot";
            dot.transform.SetParent(debugRoot, false);
            dot.transform.position = SheetPointToWorld(sheetImpact, surfaceOffset);
            dot.transform.localScale = Vector3.one * (impactDotRadius * 2f);

            Collider col = dot.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            MeshRenderer mr = dot.GetComponent<MeshRenderer>();
            mr.sharedMaterial = CreateDebugMaterial(impactDotColor);
        }

        private void CreateImpactCircle(Vector2 center, float radius)
        {
            GameObject circle = new GameObject("ImpactCircle");
            circle.transform.SetParent(debugRoot, false);

            LineRenderer lr = circle.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = Mathf.Max(16, impactCircleSegments);
            lr.startWidth = impactCircleLineWidth;
            lr.endWidth = impactCircleLineWidth;
            lr.startColor = impactCircleColor;
            lr.endColor = impactCircleColor;
            lr.material = CreateDebugMaterial(impactCircleColor);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;

            int count = lr.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f;
                Vector2 p = center + new Vector2(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius
                );

                lr.SetPosition(i, SheetPointToWorld(p, surfaceOffset));
            }
        }

        private void DrawSeeds(Vector2[] sites)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                seed.name = $"Seed_{i}";
                seed.transform.SetParent(debugRoot, false);
                seed.transform.position = SheetPointToWorld(sites[i], surfaceOffset * 1.5f);
                seed.transform.localScale = Vector3.one * (seedRadius * 2f);

                Collider col = seed.GetComponent<Collider>();
                if (col != null)
                    Destroy(col);

                MeshRenderer mr = seed.GetComponent<MeshRenderer>();
                mr.sharedMaterial = CreateDebugMaterial(seedColor);
            }
        }

        private void DrawVoronoiCells(List<Vector2> polygon, Vector2[] sites)
        {
            var calc = new VoronoiCalculator();
            var clip = new VoronoiClipper();
            var diagram = calc.CalculateDiagram(sites);
            var clipped = new List<Vector2>();

            for (int i = 0; i < sites.Length; i++)
            {
                clipped.Clear();
                clip.ClipSite(diagram, polygon, i, ref clipped);

                if (clipped.Count < 2)
                    continue;

                DrawCellOutline(clipped, i);
            }
        }

        private void DrawCellOutline(List<Vector2> cell, int index)
        {
            GameObject go = new GameObject($"Cell_{index}");
            go.transform.SetParent(debugRoot, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = cell.Count;
            lr.startWidth = cellLineWidth;
            lr.endWidth = cellLineWidth;
            lr.startColor = cellColor;
            lr.endColor = cellColor;
            lr.material = CreateDebugMaterial(cellColor);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;

            for (int i = 0; i < cell.Count; i++)
            {
                lr.SetPosition(i, SheetPointToWorld(cell[i], surfaceOffset * 0.7f));
            }
        }

        private Vector2[] GeneratePaintSites(Vector2 position, float radius)
        {
            switch (seedPattern)
            {
                case SeedPattern.StraussProcess:
                    return GenerateStraussProcessSites(position, radius);

                case SeedPattern.RadialScatter:
                    return GenerateRadialScatterSites(position, radius);

                case SeedPattern.RingJittered:
                default:
                    return GenerateRingJitteredSites(position, radius);
            }
        }

        private Vector2[] GenerateRingJitteredSites(Vector2 position, float radius)
        {
            var sites = GenerateImpactSites(position, radius);
            float jitterAmount = radius * seedJitterFactor;

            for (int i = 0; i < sites.Length; i++)
            {
                Vector2 jitter = Random.insideUnitCircle * jitterAmount;
                sites[i] += jitter;
            }

            return sites;
        }

        private Vector2[] GenerateRadialScatterSites(Vector2 position, float radius)
        {
            var sites = GenerateImpactSites(position, radius);

            for (int i = 0; i < sites.Length; i++)
            {
                float dist = Mathf.Abs(NormalizedRandom(radialMean, radialStdDev)) * radius;
                dist = Mathf.Clamp(dist, radius * radialMinFactor, radius * radialMaxFactor);

                float angle = 2.0f * Mathf.PI * Random.value;

                sites[i] = position + new Vector2(
                    dist * Mathf.Cos(angle),
                    dist * Mathf.Sin(angle)
                );
            }

            return sites;
        }

        private Vector2[] GenerateStraussProcessSites(Vector2 center, float impactRadius)
        {
			float observationRadius = Mathf.Max(ObservationRadius, 0.01f);
			int targetCount = Mathf.Max(1, StraussTargetSeedCount);

			var hppSites = GenerateHppSites(center, observationRadius, targetCount);

			float hardCoreDistance = Mathf.Max(0f, HardCoreDistance);
			var mhcpSites = ApplyMatternHardCore(hppSites, hardCoreDistance);

			float gamma = Mathf.Clamp01(StraussGamma);
			RunStraussMcmc(mhcpSites, center, observationRadius, hardCoreDistance, gamma, StraussMcmcSweeps);

			if (StraussIncludeImpactCenter) {
				mhcpSites.Add(center);
			}

			Debug.Log($"[StraussProcess] Seeds created: {mhcpSites.Count}");
			return mhcpSites.ToArray();
        }

        private static List<Vector2> GenerateHppSites(Vector2 center, float radius, int targetCount)
        {
            float area = Mathf.PI * radius * radius;
            float lambda = targetCount / area;
            int hppCount = SamplePoisson(lambda * area);

            var hppSites = new List<Vector2>(Mathf.Max(1, hppCount));
            for (int i = 0; i < hppCount; i++)
            {
                hppSites.Add(RandomPointInDisk(center, radius));
            }

            return hppSites;
        }

        private static int SamplePoisson(float mean)
        {
            if (mean <= 0f)
            {
                return 0;
            }

            float l = Mathf.Exp(-mean);
            int k = 0;
            float p = 1f;

            do
            {
                k++;
                p *= Random.value;
            } while (p > l);

            return k - 1;
        }

        private static Vector2 RandomPointInDisk(Vector2 center, float radius)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float radial = radius * Mathf.Sqrt(Random.value);

            return center + new Vector2(
                Mathf.Cos(angle) * radial,
                Mathf.Sin(angle) * radial
            );
        }

        private static List<Vector2> ApplyMatternHardCore(List<Vector2> points, float hardCoreDistance)
        {
            if (points == null || points.Count == 0 || hardCoreDistance <= 0f)
            {
                return points == null ? new List<Vector2>() : new List<Vector2>(points);
            }

            var filtered = new List<Vector2>(points);
            float hardCoreSq = hardCoreDistance * hardCoreDistance;

            int maxRemovals = filtered.Count;
            for (int removal = 0; removal < maxRemovals; removal++)
            {
                if (!TryFindConflictPair(filtered, hardCoreSq, out int i, out int j))
                {
                    break;
                }

                int removeIndex = (Random.value < 0.5f) ? i : j;
                filtered.RemoveAt(removeIndex);
            }

            return filtered;
        }

        private static bool TryFindConflictPair(List<Vector2> points, float distanceSq, out int first, out int second)
        {
            first = -1;
            second = -1;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    if ((points[i] - points[j]).sqrMagnitude < distanceSq)
                    {
                        first = i;
                        second = j;
                        return true;
                    }
                }
            }

            return false;
        }

        private static void RunStraussMcmc(List<Vector2> points, Vector2 center, float radius, float hardCoreDistance, float gamma, int sweeps)
        {
            if (points == null || points.Count <= 1 || sweeps <= 0)
            {
                return;
            }

            float hardCoreSq = hardCoreDistance * hardCoreDistance;
            gamma = Mathf.Clamp01(gamma);
            int moves = points.Count * sweeps;

            for (int step = 0; step < moves; step++)
            {
                int idx = Random.Range(0, points.Count);
                Vector2 oldPoint = points[idx];
                Vector2 proposal = RandomPointInDisk(center, radius);

                int oldNeighbors = CountNeighborsWithin(points, idx, oldPoint, hardCoreSq);
                int newNeighbors = CountNeighborsWithin(points, idx, proposal, hardCoreSq);

                bool accept;
                if (gamma <= 0f)
                {
                    accept = newNeighbors == 0;
                }
                else if (gamma >= 1f)
                {
                    accept = true;
                }
                else
                {
                    float ratio = Mathf.Pow(gamma, newNeighbors - oldNeighbors);
                    accept = ratio >= 1f || Random.value < ratio;
                }

                if (accept)
                {
                    points[idx] = proposal;
                }
            }
        }

        private static int CountNeighborsWithin(List<Vector2> points, int skipIndex, Vector2 p, float distanceSq)
        {
            int count = 0;

            for (int i = 0; i < points.Count; i++)
            {
                if (i == skipIndex)
                {
                    continue;
                }

                if ((points[i] - p).sqrMagnitude <= distanceSq)
                {
                    count++;
                }
            }

            return count;
        }

        private static float NormalizedRandom(float mean, float stddev)
        {
            float u1 = Random.value;
            float u2 = Random.value;

            float randStdNormal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) *
                                  Mathf.Sin(2.0f * Mathf.PI * u2);

            return mean + stddev * randStdNormal;
        }

        private static Vector2[] GenerateImpactSites(Vector2 center, float impactRadius)
        {
            var sites = new List<Vector2>();

            AddRing(sites, center, impactRadius * 0.25f, 4, impactRadius * 0.03f);
            AddRing(sites, center, impactRadius * 0.70f, 14, impactRadius * 0.05f);
            AddRing(sites, center, impactRadius * 1.20f, 10, impactRadius * 0.06f);
            AddRing(sites, center, impactRadius * 1.75f, 8, impactRadius * 0.08f);

            return sites.ToArray();
        }

        private static void AddRing(List<Vector2> sites, Vector2 center, float radius, int count, float jitter)
        {
            float angleOffset = Random.value * Mathf.PI * 2f;

            for (int i = 0; i < count; i++)
            {
                float angle = angleOffset + (i / (float)count) * Mathf.PI * 2f;
                float r = radius + Random.Range(-jitter, jitter);

                sites.Add(center + new Vector2(
                    Mathf.Cos(angle) * r,
                    Mathf.Sin(angle) * r
                ));
            }
        }

        private static Material CreateDebugMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}