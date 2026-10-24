using System.Linq;
using UnityEngine;

public static partial class AsylumHubBuilder
{
    /// <summary>
    /// Stairs, measured in Play (F92 Phase A/B): the pack flights have 0.25 m risers at a slope under 45 degrees and the player's
    /// CharacterController (stepOffset 0.25) climbs them - but each flight tops out 0.24-0.28 m below the floor it arrives on,
    /// a hair more than the step offset, so the player stalled on the last tread. An invisible half-height step is added at the
    /// top of each flight, on the last tread against the landing (Building/StairFix/&lt;flight&gt;_TopStep, a plain BoxCollider).
    /// </summary>
    private static void FixStairs(Transform building)
    {
        LastDiagnostics = "";
        Transform fix = Group(building, "StairFix");
        // name, direction of the top end of the flight
        (string, Vector3)[] flights =
        {
            ("Basement_Stairs", Vector3.forward),
            ("Stairs_Left", Vector3.left),
            ("Stairs_Right", Vector3.right),
        };

        foreach ((string name, Vector3 up) in flights)
        {
            Transform stairs = building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            Renderer renderer = stairs != null ? stairs.GetComponent<Renderer>() : null;
            if (renderer == null) { LastDiagnostics += $"stairs {name}: not found; "; continue; }

            Bounds b = renderer.bounds;
            bool alongX = Mathf.Abs(up.x) > 0.5f;
            float extent = alongX ? b.extents.x : b.extents.z;
            Vector3 line = b.center;
            if (alongX) line.z = b.max.z - 1.5f; // the part of the flight that has a landing beyond it
            Vector3 Along(float d) => new Vector3(line.x, 0f, line.z) + up * d;

            if (!FloorAt(Along(extent + 0.5f), b.min.y + 4.6f, out float ground)) { LastDiagnostics += $"stairs {name}: no landing; "; continue; }

            // Walk from the landing back down the flight: the first point lower than the landing is the landing edge.
            float edge = float.NaN, tread = float.NegativeInfinity;
            for (float d = extent + 0.5f; d > extent - 3f; d -= 0.05f)
            {
                if (!Physics.Raycast(new Vector3(Along(d).x, ground + 0.05f, Along(d).z), Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (float.IsNaN(edge))
                {
                    if (hit.point.y < ground - 0.12f) { edge = d + 0.05f; tread = hit.point.y; }
                }
                else if (d > edge - 0.5f) tread = Mathf.Max(tread, hit.point.y);
            }
            if (float.IsNaN(edge)) { LastDiagnostics += $"stairs {name}: no landing edge; "; continue; }

            float rise = ground - tread;
            if (rise <= 0.2f) { LastDiagnostics += $"stairs {name}: rise {rise:F2} ok; "; continue; }

            // A ramp along the line through the last two tread noses, carried on up to the landing lip: it removes the last, too tall
            // riser without changing how the flight looks (the slope stays that of the stairs, under the 45 degree limit).
            float yBack = float.NegativeInfinity;
            for (float d = edge - 0.62f; d <= edge - 0.5f; d += 0.03f)
            {
                if (Physics.Raycast(new Vector3(Along(d).x, ground + 0.05f, Along(d).z), Vector3.down, out RaycastHit h2, 3f, ~0, QueryTriggerInteraction.Ignore)) yBack = Mathf.Max(yBack, h2.point.y);
            }
            if (float.IsNegativeInfinity(yBack)) yBack = tread - 0.25f;
            float runBack = 0.56f;
            Vector3 pBack = Along(edge - runBack); pBack.y = yBack;
            Vector3 pLip = Along(edge); pLip.y = ground;
            Vector3 slope = (pLip - pBack).normalized;
            Vector3 across = alongX ? Vector3.forward : Vector3.right;
            Vector3 normal = Vector3.Cross(slope, across);
            if (normal.y < 0f) normal = -normal;
            float length = Vector3.Distance(pBack, pLip);
            float width = (alongX ? b.size.z : b.size.x) - 0.3f;

            GameObject ramp = new GameObject(name + "_TopRamp");
            ramp.transform.SetParent(fix, false);
            ramp.transform.position = (pBack + pLip) * 0.5f - normal * 0.15f + (alongX ? new Vector3(0f, 0f, b.center.z - line.z) : Vector3.zero);
            ramp.transform.rotation = Quaternion.LookRotation(slope, normal);
            BoxCollider box = ramp.AddComponent<BoxCollider>();
            box.size = alongX ? new Vector3(width, 0.3f, length) : new Vector3(width, 0.3f, length);
            float angle = Mathf.Atan2(ground - yBack, runBack) * Mathf.Rad2Deg;
            LastDiagnostics += $"stairs {name}: tread {tread:F2}, back {yBack:F2}, landing {ground:F2}, edge at {edge:F2}, ramp {angle:F0} deg; ";
        }
    }

}
