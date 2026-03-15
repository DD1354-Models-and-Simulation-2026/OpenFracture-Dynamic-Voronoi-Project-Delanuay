using System.Collections.Generic;
using UnityEngine;

namespace GK
{
    public static class ImpactSiteGenerator
    {
        private const float FinalSafetyDistance = 0.001f;
        private const double PoissonStep = 500.0;
        private static readonly double PoissonStepExp = System.Math.Exp(PoissonStep);

        public static Vector2[] GenerateStraussImpactSites(
            Vector2 center,
            int targetCount,
            float observationRadius,
            float hardCoreDistance,
            float gamma,
            int sweeps)
        {
            float clampedObservationRadius = Mathf.Max(observationRadius, 0.01f);
            int clampedTargetCount = Mathf.Max(1, targetCount);
            float clampedHardCoreDistance = Mathf.Max(0f, hardCoreDistance);
            float clampedGamma = Mathf.Clamp01(gamma);

            var hppSites = GenerateHppSites(center, clampedObservationRadius, clampedTargetCount);
            var mhcpSites = ApplyMatternHardCore(hppSites, clampedHardCoreDistance);

            RunStraussMcmc(
                mhcpSites,
                center,
                clampedObservationRadius,
                clampedHardCoreDistance,
                clampedGamma,
                sweeps
            );

            // Safety cleanup pass to enforce minimum final spacing.
            float finalDistance = Mathf.Max(clampedHardCoreDistance, FinalSafetyDistance);
            mhcpSites = ApplyMatternHardCore(mhcpSites, finalDistance);

            Debug.Log($"Strauss Seeds created: {mhcpSites.Count}");

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

        // "Junhao, based on Knuth. For double precision floating point format the threshold is near e700, so 500 should be a safe STEP."

        private static int SamplePoisson(float mean)
        {
            if (mean <= 0f)
            {
                return 0;
            }

            double lambdaLeft = mean;
            int k = 0;
            double product = 1.0;

            do
            {
                k++;
                product *= Random.value;

                while (product < 1.0 && lambdaLeft > 0.0)
                {
                    if (lambdaLeft > PoissonStep)
                    {
                        product *= PoissonStepExp;
                        lambdaLeft -= PoissonStep;
                    }
                    else
                    {
                        product *= System.Math.Exp(lambdaLeft);
                        lambdaLeft = 0.0;
                    }
                }
            } while (product > 1.0);

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
    }
}
