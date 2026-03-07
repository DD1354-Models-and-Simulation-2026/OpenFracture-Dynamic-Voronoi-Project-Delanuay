# Impact Radius Fracturing Implementation Guide

## Overview

This document outlines the step-by-step implementation plan for adding impact radius support to the OpenFracture Voronoi-Delaunay destruction system. The feature enables three distinct fracturing cases based on proximity to the impact point:

1. **Case 1 (Outside)**: Voronoi cells completely outside the impact radius remain part of the original sheet
2. **Case 2 (Partial)**: Cells with edges on the impact boundary become separate fragments with normal material
3. **Case 3 (Inside)**: Cells completely inside the radius become loose shards with exposed "inner material"

---

## Architecture Overview

### Current System Pipeline

```
Projectile Impact
    ↓
OnCollisionEnter (Impulse > MinImpactToBreak)
    ↓
Break(Vector2 position)
    ↓
Generate 50 Voronoi sites (normal distribution around impact)
    ↓
Delaunay Triangulation (Bowyer-Watson)
    ↓
Voronoi Diagram Calculation (circumcircle centers)
    ↓
Sutherland-Hodgman Clipping
    ↓
Fragment Instantiation (all clipped polygons)
    ↓
Destroy Original GameObject
```

### Key Classes & Files

| Class | File | Purpose |
|-------|------|---------|
| `BreakableSurface` | `Assets/Scripts/BreakableSurface.cs` | Impact detection, break orchestration |
| `VoronoiCalculator` | `Assets/Plugins/Delaunay/VoronoiCalculator.cs` | Voronoi diagram from sites |
| `VoronoiClipper` | `Assets/Plugins/Delaunay/VoronoiClipper.cs` | Polygon clipping to surface boundary |
| `DelaunayCalculator` | `Assets/Plugins/Delaunay/DelaunayCalculator.cs` | Delaunay triangulation |
| `Geom` | `Assets/Plugins/Delaunay/Geom.cs` | Geometry utilities (distance, intersection, etc.) |

---

## Implementation Plan: 9-Step Incremental Delivery

Each step is **independently testable** with clear verification criteria. Steps 1–7 are the core feature; 8–9 are optional enhancements.

---

### **Phase 1: Foundation (Steps 1–2)**

#### **Step 1: Add ImpactRadius Inspector Variable**

**Objective**: Expose the impact radius as an editable parameter

**File**: `Assets/Scripts/BreakableSurface.cs`

**Changes**:
```csharp
// In BreakableSurface class, after MinImpactToBreak property (line ~41)
public float ImpactRadius = 0.5f;  // Default radius in world units
```

**Location in code**:
```csharp
public float MinBreakArea = 0.01f;
public float MinImpactToBreak = 50.0f;
public float ImpactRadius = 0.5f;  // ← ADD HERE
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ Inspector shows "Impact Radius" field when BreakableSurface component selected
- ✅ Default value 0.5 is visible and editable
- ✅ Can change value to 0.1, 0.5, 2.0, etc. and value persists in Play mode

**Expected Console Output**: None (no functional changes yet)

---

#### **Step 2: Log ImpactRadius in Break Method**

**Objective**: Verify radius value is accessible during impact

**File**: `Assets/Scripts/BreakableSurface.cs` (around line 107)

**Changes**:
Add logging at the top of the `Break()` method:

```csharp
public void Break(Vector2 position) {
    var area = Area;
    if (area > MinBreakArea) {
        Debug.Log($"[Break] Impact at {position}, ImpactRadius={ImpactRadius}, Area={area:F3}");
        
        var calc = new VoronoiCalculator();
        var clip = new VoronoiClipper();
        // ... rest of method ...
    }
}
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ When projectile hits surface, console shows: `"[Break] Impact at (x, y), ImpactRadius=0.5, Area=..."`
- ✅ Changing ImpactRadius in Inspector changes logged value
- ✅ Fracturing still works normally (no visual regression)

**Expected Console Output**:
```
[Break] Impact at (2.5, 1.3), ImpactRadius=0.5, Area=4.000
```

---

### **Phase 2: Site Categorization (Steps 3–4)**

#### **Step 3: Create SiteCategory Enum & Categorization Method**

**Objective**: Define data structure and implement site classification logic

**File**: `Assets/Scripts/BreakableSurface.cs`

**Changes**:
Add enum and helper method to the BreakableSurface class (after the age field):

```csharp
/// <summary>
/// Categorizes Voronoi sites based on their distance from impact point
/// </summary>
private enum SiteCategory {
    Outside,    // Case 1: completely outside radius (remains attached)
    Partial,    // Case 2: on boundary (separate fragment, normal material)
    Inside      // Case 3: completely inside radius (loose shard, exposed material)
}

/// <summary>
/// Categorize each Voronoi site by its distance from impact point
/// </summary>
private SiteCategory[] CategorizeSites(Vector2 impactPos, float radius, Vector2[] sites) {
    var categories = new SiteCategory[sites.Length];
    
    for (int i = 0; i < sites.Length; i++) {
        float distance = Vector2.Distance(impactPos, sites[i]);
        
        if (distance <= radius) {
            categories[i] = SiteCategory.Inside;
        } else {
            categories[i] = SiteCategory.Outside;
        }
    }
    
    return categories;
}
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ No runtime errors (method not called yet)
- ✅ Method signature correct: `Vector2[] sites` input → `SiteCategory[]` output
- ✅ Logic is simple and obviously correct

**Expected Console Output**: None yet

---

#### **Step 4: Invoke Categorization & Log Results**

**Objective**: Call the categorization method and display statistics

**File**: `Assets/Scripts/BreakableSurface.cs` (around line 115-135)

**Changes**:
In `Break()` method, after the sites array is filled (after the `for` loop at ~line 131):

```csharp
// After: for (int i = 0; i < sites.Length; i++) { ... }

// Categorize sites by distance from impact
var categories = CategorizeSites(position, ImpactRadius, sites);

// Count statistics
int countInside = 0, countOutside = 0;
for (int i = 0; i < categories.Length; i++) {
    if (categories[i] == SiteCategory.Inside) countInside++;
    else countOutside++;
}

Debug.Log($"[Step 4] Site categorization: {countInside} Inside, {countOutside} Outside (radius={ImpactRadius})");

// Continue with diagram calculation...
var diagram = calc.CalculateDiagram(sites);
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ Console shows categorization summary for each impact: `"[Step 4] Site categorization: X Inside, Y Outside"`
- ✅ With ImpactRadius=0.5: ~5–15 Inside, ~35–45 Outside
- ✅ With ImpactRadius=2.0: ~40–49 Inside, ~1–10 Outside
- ✅ With ImpactRadius=0.1: ~0–3 Inside, ~47–50 Outside
- ✅ Fracturing still works normally (all fragments still created)

**Expected Console Output**:
```
[Break] Impact at (2.5, 1.3), ImpactRadius=0.5, Area=4.000
[Step 4] Site categorization: 12 Inside, 38 Outside (radius=0.5)
```

---

### **Phase 3: Visual Debugging (Step 5)**

#### **Step 5: Draw Gizmos for Impact Radius & Sites**

**Objective**: Visualize the impact circle and categorized sites in Scene view during gameplay

**File**: `Assets/Scripts/BreakableSurface.cs`

**Changes**:
Add instance fields to store last break data:

```csharp
// Add after 'int age;' field (line ~34)
private Vector2 lastBreakPos;
private float lastBreakRadius;
private Vector2[] lastSites;
private SiteCategory[] lastCategories;
```

In `Break()` method, after categorization (around line ~130), save data for gizmo drawing:

```csharp
// After: var categories = CategorizeSites(...);
lastBreakPos = position;
lastBreakRadius = ImpactRadius;
lastSites = sites;
lastCategories = categories;
```

Add gizmo drawing method at the end of the class:

```csharp
void OnDrawGizmos() {
    if (lastSites == null || lastSites.Length == 0) return;
    
    // Draw impact radius circle (yellow)
    Gizmos.color = Color.yellow;
    DrawCircleGizmo(transform.TransformPoint(new Vector3(lastBreakPos.x, lastBreakPos.y, 0)), 
                     lastBreakRadius, 32);
    
    // Draw categorized sites
    for (int i = 0; i < lastSites.Length; i++) {
        Gizmos.color = (lastCategories[i] == SiteCategory.Inside) ? Color.red : Color.green;
        
        var worldPos = transform.TransformPoint(new Vector3(lastSites[i].x, lastSites[i].y, 0));
        Gizmos.DrawSphere(worldPos, 0.05f);
    }
}

/// <summary>
/// Helper to draw a circle as line segments in Gizmos view
/// </summary>
void DrawCircleGizmo(Vector3 center, float radius, int segments) {
    segments = Mathf.Max(segments, 3);
    float angleStep = 360f / segments;
    
    Vector3 prevPoint = center + new Vector3(radius, 0, 0);
    
    for (int i = 1; i <= segments; i++) {
        float angle = i * angleStep;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 newPoint = center + new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0);
        Gizmos.DrawLine(prevPoint, newPoint);
        prevPoint = newPoint;
    }
}
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ Play scene, fire projectile at surface
- ✅ Yellow circle appears showing ImpactRadius around impact point
- ✅ Red dots (Inside) cluster near impact center
- ✅ Green dots (Outside) appear around edges of yellow circle
- ✅ Gizmos disappear naturally after break completes
- ✅ Adjusting ImpactRadius in Inspector changes circle size before next shot (matching logged radius)
- ✅ Circle and dots position matches logged impact position

**Expected Visual**:
- Yellow circle with 0.5 radius around impact point
- ~12 red dots clustered in center region
- ~38 green dots scattered around outer edges

---

### **Phase 4: Fragment Categorization (Steps 6–7)**

#### **Step 6: Track Fragment Categories During Clipping**

**Objective**: Assign each clipped polygon to its originating site's category

**File**: `Assets/Scripts/BreakableSurface.cs` (around line 139-165)

**Changes**:
In the clipping loop, track which category each fragment belongs to:

```csharp
var clipped = new List<Vector2>();
int insideFragmentCount = 0;
int outsideFragmentCount = 0;

for (int i = 0; i < sites.Length; i++) {
    clip.ClipSite(diagram, Polygon, i, ref clipped);

    if (clipped.Count > 0) {
        var fragCategory = categories[i];  // ← Get site's category
        
        // Count fragments by category
        if (fragCategory == SiteCategory.Inside) {
            insideFragmentCount++;
        } else if (fragCategory == SiteCategory.Outside) {
            outsideFragmentCount++;
        }
        
        // Still instantiate all fragments (will filter in next step)
        var newGo = Instantiate(gameObject, transform.parent);
        // ... rest of instantiation code ...
    }
}

// Log summary
Debug.Log($"[Step 6] Fragments by category: {insideFragmentCount} Inside, {outsideFragmentCount} Outside");
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ Console shows: `"[Step 6] Fragments by category: X Inside, Y Outside"`
- ✅ Fragment counts roughly match site counts from Step 4 (within ±5%)
- ✅ Fracturing behavior unchanged (all fragments still instantiated)
- ✅ Changing ImpactRadius changes category balance

**Expected Console Output**:
```
[Break] Impact at (2.5, 1.3), ImpactRadius=0.5, Area=4.000
[Step 4] Site categorization: 12 Inside, 38 Outside (radius=0.5)
[Step 6] Fragments by category: 8 Inside, 22 Outside
```

---

#### **Step 7: Apply Different Materials by Category**

**Objective**: Visually distinguish Inside (exposed material) from Outside fragments

**Setup** (one-time):
1. In Unity Editor, create a new Material:
   - Right-click in Assets → Create → Material
   - Name it `ExposedInnerMaterial`
   - Set color to dark gray/black or use a different shader
2. Assign to BreakableSurface component:
   - Select the BreakableSurface prefab/object in scene
   - In Inspector, add public field `public Material ExposedInnerMaterial;`
   - Drag the newly created material into this field

**File Changes**: `Assets/Scripts/BreakableSurface.cs`

Add field (after ImpactRadius):
```csharp
public Material ExposedInnerMaterial;  // Assigned in Inspector
```

In clipping loop, apply material at instantiation:

```csharp
if (clipped.Count > 0) {
    var newGo = Instantiate(gameObject, transform.parent);
    
    newGo.transform.localPosition = transform.localPosition;
    newGo.transform.localRotation = transform.localRotation;
    
    var bs = newGo.GetComponent<BreakableSurface>();
    
    bs.Thickness = Thickness;
    bs.Polygon.Clear();
    bs.Polygon.AddRange(clipped);
    
    var childArea = bs.Area;
    var rb = bs.GetComponent<Rigidbody>();
    rb.mass = Rigidbody.mass * (childArea / area);
    
    // ← NEW: Apply material based on category
    var renderer = newGo.GetComponent<MeshRenderer>();
    if (categories[i] == SiteCategory.Inside && ExposedInnerMaterial != null) {
        renderer.material = ExposedInnerMaterial;
        Debug.Log($"[Step 7] Fragment {i} is INSIDE → applied ExposedInnerMaterial");
    } else {
        renderer.material = Renderer.material;  // Preserve original
        Debug.Log($"[Step 7] Fragment {i} is OUTSIDE → kept original material");
    }
}
```

**Verification Checklist**:
- ✅ Compile without errors
- ✅ ExposedInnerMaterial field appears in Inspector
- ✅ Fire projectile at surface
- ✅ Fragments with Inside category appear **dark/different color**
- ✅ Fragments with Outside category appear **original color** (white/default)
- ✅ Console logs each fragment's material assignment
- ✅ Adjust ImpactRadius: smaller radius → fewer dark fragments; larger radius → more dark fragments
- ✅ Visual distinction is clear (easy to see 2 colors)

**Expected Visual Behavior**:
- ImpactRadius=0.3: 3-5 dark shards in center, many light fragments
- ImpactRadius=1.0: 15-20 dark shards in impact zone, light ring around
- ImpactRadius=2.0: Most fragments dark, few light on far edges

**Expected Console Output**:
```
[Break] Impact at (2.5, 1.3), ImpactRadius=0.5, Area=4.000
[Step 4] Site categorization: 12 Inside, 38 Outside (radius=0.5)
[Step 6] Fragments by category: 8 Inside, 22 Outside
[Step 7] Fragment 0 is INSIDE → applied ExposedInnerMaterial
[Step 7] Fragment 1 is OUTSIDE → kept original material
[Step 7] Fragment 2 is INSIDE → applied ExposedInnerMaterial
...
```

---

## Decision Checkpoints

After each major phase, evaluate whether to proceed to the next step:

### **After Step 5: Visual Debugging**
**Question**: Do the gizmos clearly show expected categorization?
- **Yes**: Proceed to Step 6
- **No**: Debug gizmo drawing; verify circle radius/site positions match logged values

### **After Step 7: Material Assignment**
**Question**: Is the visual distinction between Inside/Outside fragments clear and correct?
- **Yes**: Feature is complete! Consider Steps 8–9 for polish
- **No**: Verify ExposedInnerMaterial is assigned; check renderer.material assignment

---

## Advanced Features (Optional, Deferred)

### **Step 8: Merge Outside Fragments into Remaining Sheet**

**Objective** (deferred): Combine Case 1 (Outside) cells back into single "remaining sheet" polygon

**Why deferred**: Requires polygon union algorithm; current behavior (separate fragments) is functional

**Implementation approach** (sketch):
1. Collect all Outside-category clipped polygons
2. Implement polygon union algorithm (or use third-party library)
3. Create single merged fragment from union
4. Apply original material to merged sheet

**Verification**:
- Remaining unaffected sheet is single rigid body
- Collision mesh is contiguous
- Visual test: large radius → most fragments loose; small radius → large attached sheet

---

### **Step 9: Add Boundary Case (Partial Category)**

**Objective** (deferred): Handle cells whose vertices straddle the impact radius

**Why deferred**: Current 2-category system (Inside/Outside) works well; partial edges may not be visually important

**Implementation approach** (sketch):
1. In CategorizeSites(), check if site is Inside but has clipped polygon with area loss
2. Mark as Partial
3. Apply intermediate material (e.g., slightly darker than original)

**Verification**:
- Boundary cells visible with distinct color
- Material distinct from both Inside and Outside

---

## Testing Checklist: Visual Verification Scenarios

| Scenario | Setup | Expected Result |
|----------|-------|-----------------|
| **Small radius** | ImpactRadius=0.2, fire at center | 2-4 dark shards, mostly light fragments |
| **Medium radius** | ImpactRadius=0.8, fire at center | 15-25 dark shards, balanced light/dark |
| **Large radius** | ImpactRadius=2.0, fire at center | 40+ dark shards, few light on edges |
| **Edge impact** | ImpactRadius=0.5, fire at edge | Mostly light fragments (outside radius) |
| **Gizmo verification** | Any impact, Gizmos enabled | Yellow circle + red/green dots match logged counts |
| **Material persistence** | Fire twice at same spot | Materials remain consistent with radius |

---

## Debugging Tips

### Console Output Not Showing?
- Check Edit → Preferences → General → Log Entry
- Ensure Debug.Log calls are in active code path
- Verify Break() is actually called (check OnCollisionEnter conditions)

### Gizmos Not Visible?
- Enable Gizmos in Scene view (top right toggle)
- Ensure OnDrawGizmos() is not commented out
- Verify lastSites/lastCategories are not null

### Material Not Applied?
- Verify ExposedInnerMaterial is assigned in Inspector (not null)
- Check that renderer component exists (GetComponent<MeshRenderer>())
- Confirm material assignment line is reached (add Debug.Log before assignment)

### Fragment Counts Don't Match Sites?
- Not all sites may produce clipped polygons (area < MinBreakArea)
- Some polygon clipping may fail in edge cases
- This is expected; counts should be roughly proportional

---

## Code Summary: Changes Required

| Step | File | Lines | Type |
|------|------|-------|------|
| 1 | BreakableSurface.cs | ~41 | Add field |
| 2 | BreakableSurface.cs | ~108 | Add Debug.Log |
| 3 | BreakableSurface.cs | ~34–60 | Add enum + method |
| 4 | BreakableSurface.cs | ~128–135 | Add calls + logging |
| 5 | BreakableSurface.cs | ~34–45, ~170–200 | Add fields + methods |
| 6 | BreakableSurface.cs | ~142–158 | Add tracking |
| 7 | BreakableSurface.cs | ~41, ~152–162 | Add field + material logic |

---

## References

### Project Structure
- Main script: `Assets/Scripts/BreakableSurface.cs`
- Voronoi calculation: `Assets/Plugins/Delaunay/VoronoiCalculator.cs`
- Clipping: `Assets/Plugins/Delaunay/VoronoiClipper.cs`
- Geometry utilities: `Assets/Plugins/Delaunay/Geom.cs`

### Algorithm References
- **Voronoi Diagram**: Circumcircle centers of Delaunay triangles
- **Clipping**: Sutherland-Hodgman half-plane algorithm
- **Distance Check**: Euclidean distance (`Vector2.Distance`)

---

## Version History

| Date | Version | Changes |
|------|---------|---------|
| 2026-03-04 | 1.0 | Initial plan with 7 core steps |

---

**Status**: Ready for implementation of Phase 1 (Steps 1–2)
