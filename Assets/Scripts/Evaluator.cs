using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

public class Evaluator : MonoBehaviour
{
    private Stopwatch stopwatch;

    public void OnFractureStart(Collider col, GameObject obj, Vector3 point)
    {
        UnityEngine.Debug.Log($"Evaluator START called on {name}");
        stopwatch = Stopwatch.StartNew();
    }

    public void OnFractureComplete()
    {
        UnityEngine.Debug.Log($"Evaluator COMPLETE called on {name}");

        if (stopwatch == null)
        {
            UnityEngine.Debug.LogWarning($"Evaluator stopwatch is null on {name}");
            return;
        }

        stopwatch.Stop();

        int shardCount = 0;

        string rootName = gameObject.name + "Fragments";
        GameObject root = GameObject.Find(rootName);

        if (root != null)
        {
            shardCount = root.transform.childCount;
        }
        else
        {
            Transform parent = transform.parent;
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child != transform && child.name.StartsWith(gameObject.name))
                    {
                        shardCount++;
                    }
                }
            }
        }

        UnityEngine.Debug.Log($"Time: {stopwatch.Elapsed.TotalMilliseconds} ms | Shards: {shardCount}");
    }
}