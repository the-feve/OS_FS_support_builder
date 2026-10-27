FeatureScript 3083;
import(path : "onshape/std/geometry.fs", version : "3083.0");

// Print Support Wall
// Builds thin breakaway support walls for tall, thin 3D-printed parts. Each wall lies on a
// user-picked plane, stands on the print bed plane, and touches the part through a row of
// small teeth ("attach points") that follow the part's edge however it curves.
// Pick one part to brace it from one side, or two parts to build a wall bridging them.
// Works on solids and on imported meshes (STL/OBJ/3MF).
//
// How the edge is found: rays are cast across the support plane at many heights, toward the
// part. The closest hit at each height traces the part's edge in that plane. Rays are cast at
// the wall's centre and both faces, so the gap holds across the whole wall thickness. Misses
// and jumps (rays slipping through holes in the part) are filtered out.
// All profile math is plain 2D (s = across the plane, h = height above the bed).

export enum AttachMode
{
    annotation { "Name" : "Count" }
    COUNT,
    annotation { "Name" : "Spacing" }
    SPACING
}

const WALL_BOUNDS      = { (millimeter) : [0.2, 1.2, 20] } as LengthBoundSpec;
const GAP_BOUNDS       = { (millimeter) : [0, 1.0, 20] } as LengthBoundSpec;
const OVERLAP_BOUNDS   = { (millimeter) : [0, 0.1, 2] } as LengthBoundSpec;
const TIP_BOUNDS       = { (millimeter) : [0.1, 1.0, 10] } as LengthBoundSpec;
const BASE_W_BOUNDS    = { (millimeter) : [0.2, 2.5, 50] } as LengthBoundSpec;
const SPACING_BOUNDS   = { (millimeter) : [0.5, 8, 500] } as LengthBoundSpec;
const HEIGHT_BOUNDS    = { (millimeter) : [1, 80, 2000] } as LengthBoundSpec;
const FOOT_BOUNDS      = { (millimeter) : [0, 20, 1000] } as LengthBoundSpec;
const TOP_W_BOUNDS     = { (millimeter) : [0.5, 3, 100] } as LengthBoundSpec;
const PAD_W_BOUNDS     = { (millimeter) : [1, 10, 200] } as LengthBoundSpec;
const PAD_T_BOUNDS     = { (millimeter) : [0.1, 0.6, 10] } as LengthBoundSpec;
const COUNT_BOUNDS     = { (unitless) : [1, 12, 500] } as IntegerBoundSpec;

const SAMPLE_STEP = 1 * millimeter;   // edge-trace resolution
const MAX_SAMPLES = 300;              // cap on trace heights per side
const MAX_TRACE_GAP = 8 * millimeter; // a longer run of misses ends the trace
const OUTER_EVERY = 4;                // outer edge uses every Nth trace sample
const TOL = 1e-6 * meter;

annotation { "Feature Type Name" : "Print Support Wall",
             "Feature Type Description" : "Thin breakaway support walls for tall, thin 3D-printed parts.<br>" ~
                 "Pick one part (or two to bridge them), one or more support planes, and the print bed " ~
                 "plane. Each wall follows the part's edge in its plane and touches it through small teeth. " ~
                 "Works on solids and imported meshes." }
export const printSupportWall = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Parts to support (pick 2 to bridge them)",
                     "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET) && AllowMeshGeometry.YES,
                     "MaxNumberOfPicks" : 2 }
        definition.part is Query;

        annotation { "Name" : "Support planes", "Filter" : GeometryType.PLANE }
        definition.supportPlanes is Query;

        annotation { "Name" : "Flip support side (one part)", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipSide is boolean;

        annotation { "Name" : "Print bed plane", "Filter" : GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.bedPlane is Query;

        annotation { "Name" : "Max support height" }
        isLength(definition.supportHeight, HEIGHT_BOUNDS);

        annotation { "Name" : "Wall thickness" }
        isLength(definition.thickness, WALL_BOUNDS);

        annotation { "Name" : "Attach points by", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.attachMode is AttachMode;

        if (definition.attachMode == AttachMode.COUNT)
        {
            annotation { "Name" : "Attach point count (per side)" }
            isInteger(definition.toothCount, COUNT_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Attach point spacing" }
            isLength(definition.toothSpacing, SPACING_BOUNDS);
        }

        annotation { "Name" : "Gap to part" }
        isLength(definition.gap, GAP_BOUNDS);

        annotation { "Name" : "Tooth contact width" }
        isLength(definition.toothTip, TIP_BOUNDS);

        annotation { "Name" : "Tooth base width" }
        isLength(definition.toothBase, BASE_W_BOUNDS);

        annotation { "Name" : "Contact overlap" }
        isLength(definition.overlap, OVERLAP_BOUNDS);

        annotation { "Name" : "Foot reach on bed (one part)" }
        isLength(definition.footReach, FOOT_BOUNDS);

        annotation { "Name" : "Top width (one part)" }
        isLength(definition.topWidth, TOP_W_BOUNDS);

        annotation { "Name" : "Add bed pad", "Default" : true }
        definition.addPad is boolean;

        if (definition.addPad)
        {
            annotation { "Name" : "Pad width" }
            isLength(definition.padWidth, PAD_W_BOUNDS);

            annotation { "Name" : "Pad thickness" }
            isLength(definition.padThickness, PAD_T_BOUNDS);
        }

        annotation { "Name" : "Merge supports into part" }
        definition.mergeWithPart is boolean;
    }
    {
        supportWallBody(context, id, definition);
    });

// Feature body as a plain function so a test feature can call it with a hand-built definition.
export function supportWallBody(context is Context, id is Id, definition is map)
{
    var bodies = evaluateQuery(context, definition.part);
    if (size(bodies) < 1)
        throw regenError("Select the part to support.", ["part"]);
    var planes = evaluateQuery(context, definition.supportPlanes);
    if (size(planes) < 1)
        throw regenError("Select at least one support plane.", ["supportPlanes"]);
    if (size(evaluateQuery(context, definition.bedPlane)) < 1)
        throw regenError("Select the print bed plane.", ["bedPlane"]);

    var bed;
    try
    {
        bed = evPlane(context, { "face" : definition.bedPlane });
    }
    catch (e)
    {
        throw regenError("Couldn't read the print bed plane.", ["bedPlane"]);
    }

    var bbox;
    try
    {
        bbox = evBox3d(context, { "topology" : definition.part, "tight" : true });
    }
    catch (e)
    {
        throw regenError("Couldn't measure the part.", ["part"]);
    }

    // "Up" is the bed normal, pointed toward the parts.
    var up = bed.normal;
    var center = (bbox.minCorner + bbox.maxCorner) / 2;
    if (dot(center - bed.origin, up) < 0 * meter)
        up = -up;

    var walls = [];
    var report = "";
    for (var i = 0; i < size(planes); i += 1)
    {
        var wallId = id + ("wall" ~ i);
        var res = buildOneWall(context, wallId, planes[i], up, bed.origin, bodies, definition);
        setProperty(context, {
                "entities" : res.body,
                "propertyType" : PropertyType.NAME,
                "value" : "Support " ~ (i + 1)
            });
        walls = append(walls, res.body);
        report = report ~ "Support " ~ (i + 1) ~ ": " ~ res.teeth ~ " attach points, " ~
            roundToPrecision(res.height / millimeter, 1) ~ " mm tall. ";
    }

    if (definition.mergeWithPart)
    {
        opBoolean(context, id + "merge", {
                "tools" : qUnion(concatenateArrays([[definition.part], walls])),
                "operationType" : BooleanOperationType.UNION
            });
    }

    reportFeatureInfo(context, id, report);
}

function buildOneWall(context is Context, wallId is Id, planeQuery is Query, up is Vector, bedOrigin is Vector,
    bodies is array, definition is map) returns map
{
    var sp;
    try
    {
        sp = evPlane(context, { "face" : planeQuery });
    }
    catch (e)
    {
        throw regenError("Couldn't read a support plane.", ["supportPlanes"], planeQuery);
    }

    var n = sp.normal;
    var cosNU = dot(n, up);
    if (abs(cosNU) > 0.98)
        throw regenError("Support plane is parallel to the bed. Pick a plane that stands up from the bed.",
            ["supportPlanes"], planeQuery);

    // 2D frame in the support plane: d = across, w = in-plane up.
    var d = normalize(cross(up, n));
    if (definition.flipSide && size(bodies) == 1)
        d = -d;
    var w = normalize(up - n * cosNU);
    var wu = dot(w, up);
    var O = sp.origin - w * (dot(sp.origin - bedOrigin, up) / wu); // plane origin dropped to bed level
    var t = definition.thickness;

    var at = function(s, h)
        {
            return O + d * s + w * (h / wu);
        };

    // Extent of each body in this frame.
    var ext = [];
    var hMax = 0 * meter;
    for (var b in bodies)
    {
        var bb = evBox3d(context, { "topology" : b, "tight" : true });
        var e = extentIn(bb, O, d, bedOrigin, up);
        ext = append(ext, e);
        hMax = max(hMax, e.hMax);
    }
    var hLimit = min(definition.supportHeight, hMax);
    var jumpTol = max(1.5 * millimeter, 2 * definition.gap);

    // Edge position (s) of a body at height h, from rays cast along d * sign starting at s = start.
    // Rays run at the wall's centre and both faces; returns the hit closest to the wall.
    var castAt = function(body, sign, start, h)
        {
            var faces = qOwnedByBody(body, EntityType.FACE);
            var best = undefined;
            for (var off in [-t / 2, 0 * meter, t / 2])
            {
                var origin = at(start, h) + n * off;
                for (var target in [faces, body])
                {
                    var hits = try silent(evRaycast(context, {
                                "entities" : target,
                                "ray" : line(origin, d * sign),
                                "closest" : true
                            }));
                    if (hits != undefined && size(hits) > 0)
                    {
                        var s = dot(hits[0].intersection - O, d);
                        if (best == undefined || s * sign < best * sign)
                            best = s;
                        break;
                    }
                }
            }
            return best;
        };

    // Trace one side: sample heights, drop misses and hole jumps, stop at a long run of misses.
    var traceSide = function(body, sign, start)
        {
            var step = max(SAMPLE_STEP, hLimit / MAX_SAMPLES);
            var rawH = [];
            var rawS = [];
            for (var h = step / 2; h <= hLimit; h += step)
            {
                var s = castAt(body, sign, start, h);
                if (s != undefined)
                {
                    rawH = append(rawH, h);
                    rawS = append(rawS, s);
                }
            }
            var tr = filterTrace(rawH, rawS, jumpTol, MAX_TRACE_GAP);
            if (size(tr.h) < 2)
            {
                if (size(evaluateQuery(context, qMeshGeometryFilter(body, MeshGeometry.YES))) > 0)
                    throw regenError("Rays didn't hit the mesh part. Convert the STL with mesh2step and select " ~
                        "the converted part.", ["part"]);
                throw regenError("Support plane doesn't cross the part below the max support height.",
                    ["supportPlanes"], planeQuery);
            }
            tr.edgeAt = function(hq)
                {
                    var s = castAt(body, sign, start, hq);
                    var guess = interp(tr.h, tr.s, hq);
                    return (s == undefined || abs(s - guess) > jumpTol) ? guess : s;
                };
            return tr;
        };

    var res;
    if (size(bodies) == 1)
    {
        // Wall on the low-s side; rays travel +d toward the part.
        var tr = traceSide(bodies[0], 1, ext[0].sMin - 5 * millimeter);
        res = singleProfile(tr, definition);
    }
    else
    {
        // Bridge: A is the body at lower s, B at higher s; the wall sits between them.
        var ia = (ext[0].sMid <= ext[1].sMid) ? 0 : 1;
        var ib = 1 - ia;
        var trA = traceSide(bodies[ia], -1, ext[ia].sMax + 5 * millimeter);
        var trB = traceSide(bodies[ib], 1, ext[ib].sMin - 5 * millimeter);
        res = bridgeProfile(trA, trB, definition);
        if (res.error != undefined)
            throw regenError(res.error, ["supportPlanes"], planeQuery);
    }

    // Sketch the profile on a plane offset by half the wall, then extrude through.
    var skPlane = plane(O - n * (t / 2), n, d);
    var yAx = cross(n, d);
    var pts = [];
    for (var p in res.points)
    {
        var wp = at(p[0], p[1]);
        pts = append(pts, vector(dot(wp - skPlane.origin, d), dot(wp - skPlane.origin, yAx)));
    }
    pts = closeLoop(dedupe(pts));

    var sketch = newSketchOnPlane(context, wallId + "sketch", { "sketchPlane" : skPlane });
    skPolyline(sketch, "outline", { "points" : pts });
    skSolve(sketch);

    try
    {
        opExtrude(context, wallId + "extrude", {
                "entities" : qSketchRegion(wallId + "sketch"),
                "direction" : n,
                "endBound" : BoundingType.BLIND,
                "endDepth" : t
            });
    }
    catch (e)
    {
        throw regenError("Couldn't build the support wall from its profile. Try a larger gap or fewer attach points.",
            ["supportPlanes"], planeQuery);
    }

    var sketchBodies = [qCreatedBy(wallId + "sketch", EntityType.BODY)];

    if (definition.addPad)
    {
        // Flat pad on the bed under the wall's bottom edge, for adhesion and stability.
        var padPlane = plane(O, up, d);
        var padY = cross(up, d);
        var half = padY * (definition.padWidth / 2);
        var c1 = at(res.padFrom, 0 * meter) - half;
        var c2 = at(res.padTo, 0 * meter) + half;
        var padSketch = newSketchOnPlane(context, wallId + "padSketch", { "sketchPlane" : padPlane });
        skRectangle(padSketch, "pad", {
                "firstCorner" : vector(dot(c1 - padPlane.origin, d), dot(c1 - padPlane.origin, padY)),
                "secondCorner" : vector(dot(c2 - padPlane.origin, d), dot(c2 - padPlane.origin, padY))
            });
        skSolve(padSketch);
        opExtrude(context, wallId + "pad", {
                "entities" : qSketchRegion(wallId + "padSketch"),
                "direction" : up,
                "endBound" : BoundingType.BLIND,
                "endDepth" : definition.padThickness
            });
        opBoolean(context, wallId + "union", {
                "tools" : qUnion([qCreatedBy(wallId + "extrude", EntityType.BODY),
                            qCreatedBy(wallId + "pad", EntityType.BODY)]),
                "operationType" : BooleanOperationType.UNION
            });
        sketchBodies = append(sketchBodies, qCreatedBy(wallId + "padSketch", EntityType.BODY));
    }

    opDeleteBodies(context, wallId + "deleteSketches", { "entities" : qUnion(sketchBodies) });

    return {
            "body" : qBodyType(qCreatedBy(wallId, EntityType.BODY), BodyType.SOLID),
            "teeth" : res.teeth,
            "height" : res.height
        };
}

// Body extent in the wall frame: s range across the plane, max height above the bed.
function extentIn(bb is Box3d, O is Vector, d is Vector, bedOrigin is Vector, up is Vector) returns map
{
    var sMin = undefined;
    var sMax = undefined;
    var hMax = undefined;
    for (var c = 0; c < 8; c += 1)
    {
        var corner = vector(c % 2 == 0 ? bb.minCorner[0] : bb.maxCorner[0],
            floor(c / 2) % 2 == 0 ? bb.minCorner[1] : bb.maxCorner[1],
            floor(c / 4) == 0 ? bb.minCorner[2] : bb.maxCorner[2]);
        var cs = dot(corner - O, d);
        var ch = dot(corner - bedOrigin, up);
        sMin = (sMin == undefined || cs < sMin) ? cs : sMin;
        sMax = (sMax == undefined || cs > sMax) ? cs : sMax;
        hMax = (hMax == undefined || ch > hMax) ? ch : hMax;
    }
    return { "sMin" : sMin, "sMax" : sMax, "sMid" : (sMin + sMax) / 2, "hMax" : hMax };
}

// ---------------------------------------------------------------- pure 2D math (no context)
// Kept in sync with tools/profile_check.py.

// Drop samples that jump away from their neighbours' median (rays through holes), then keep the
// first run with no gap longer than maxGap.
function filterTrace(rawH is array, rawS is array, jumpTol, maxGap) returns map
{
    var keepH = [];
    var keepS = [];
    var n = size(rawH);
    for (var i = 0; i < n; i += 1)
    {
        var win = [];
        for (var j = max(0, i - 3); j <= min(n - 1, i + 3); j += 1)
            win = append(win, rawS[j]);
        if (abs(rawS[i] - median(win)) <= jumpTol)
        {
            keepH = append(keepH, rawH[i]);
            keepS = append(keepS, rawS[i]);
        }
    }
    var outH = [];
    var outS = [];
    for (var i = 0; i < size(keepH); i += 1)
    {
        if (i > 0 && keepH[i] - keepH[i - 1] > maxGap)
            break;
        outH = append(outH, keepH[i]);
        outS = append(outS, keepS[i]);
    }
    return { "h" : outH, "s" : outS };
}

function median(values is array)
{
    var sorted = sort(values, function(a, b)
        {
            return (a - b) / meter;
        });
    return sorted[floor(size(sorted) / 2)];
}

function interp(hs is array, ss is array, h)
{
    if (h <= hs[0])
        return ss[0];
    for (var j = 1; j < size(hs); j += 1)
    {
        if (h <= hs[j])
        {
            var f = (h - hs[j - 1]) / (hs[j] - hs[j - 1]);
            return ss[j - 1] + (ss[j] - ss[j - 1]) * f;
        }
    }
    return ss[size(ss) - 1];
}

// Tooth centre heights between lo and hi.
function toothCenters(lo, hi, definition is map) returns array
{
    var bw = definition.toothBase;
    var teeth = [];
    if (hi < lo)
        return teeth;
    var minPitch = bw * 1.1;
    var n = 0;
    var pitch = 0 * meter;
    if (definition.attachMode == AttachMode.COUNT)
    {
        n = min(definition.toothCount, floor((hi - lo) / minPitch) + 1);
        pitch = n > 1 ? (hi - lo) / (n - 1) : 0 * meter;
    }
    else
    {
        pitch = max(definition.toothSpacing, minPitch);
        n = floor((hi - lo) / pitch) + 1;
    }
    var first = n > 1 ? lo + ((hi - lo) - pitch * (n - 1)) / 2 : (lo + hi) / 2;
    for (var k = 0; k < n; k += 1)
        teeth = append(teeth, first + pitch * k);
    return teeth;
}

// Wall edge facing one part, bottom to top up to hTop, with teeth spliced in.
// sign = +1 when the part is at higher s than the wall, -1 when lower.
// The wall edge sits gap away from the part; tooth tips reach overlap into it.
function toothedEdge(tr is map, hTop, sign is number, definition is map) returns map
{
    var g = definition.gap;
    var ov = definition.overlap;
    var bw = definition.toothBase;
    var tw = min(definition.toothTip, bw);
    var teeth = toothCenters(tr.h[0] + bw / 2, hTop - bw / 2, definition);
    var edge = tr.edgeAt;
    var pts = [vector(tr.s[0] - g * sign, 0 * meter)];
    var k = 0;
    for (var j = 0; j < size(tr.h); j += 1)
    {
        var h = tr.h[j];
        if (h > hTop)
            break;
        while (k < size(teeth) && teeth[k] - bw / 2 <= h)
        {
            var hk = teeth[k];
            pts = concatenateArrays([pts, [
                        vector(edge(hk - bw / 2) - g * sign, hk - bw / 2),
                        vector(edge(hk - tw / 2) + ov * sign, hk - tw / 2),
                        vector(edge(hk + tw / 2) + ov * sign, hk + tw / 2),
                        vector(edge(hk + bw / 2) - g * sign, hk + bw / 2)]]);
            k += 1;
        }
        if (k > 0 && h <= teeth[k - 1] + bw / 2)
            continue; // this sample sits inside a tooth
        pts = append(pts, vector(tr.s[j] - g * sign, h));
    }
    var topS = interp(tr.h, tr.s, hTop) - g * sign;
    if (abs(pts[size(pts) - 1][1] - hTop) > TOL)
        pts = append(pts, vector(topS, hTop));
    return { "points" : pts, "teeth" : size(teeth), "bottomS" : tr.s[0] - g * sign, "topS" : topS };
}

// One part: wall on the low-s side, toothed edge toward the part, straight outer edge to a foot.
function singleProfile(tr is map, definition is map) returns map
{
    var g = definition.gap;
    var hTop = tr.h[size(tr.h) - 1];
    var inner = toothedEdge(tr, hTop, 1, definition);
    var tW = definition.topWidth;
    var minInner = tr.s[0] - g;
    for (var s in tr.s)
        minInner = min(minInner, s - g);
    var footS = min(minInner - definition.footReach, minInner - tW);
    var outerTop = inner.topS - tW;
    var pts = append(inner.points, vector(outerTop, hTop));
    for (var j = size(tr.h) - 2; j > 0; j -= 1)
    {
        if (j % OUTER_EVERY != 0)
            continue;
        var h = tr.h[j];
        var slope = footS + (outerTop - footS) * (h / hTop);
        pts = append(pts, vector(min(slope, tr.s[j] - g - tW), h));
    }
    pts = append(pts, vector(footS, 0 * meter));
    return { "points" : pts, "teeth" : inner.teeth, "height" : hTop,
            "padFrom" : footS, "padTo" : inner.bottomS };
}

// Two parts: wall between A (lower s) and B (higher s), toothed on both sides, flat top.
function bridgeProfile(trA is map, trB is map, definition is map) returns map
{
    var hTop = min(trA.h[size(trA.h) - 1], trB.h[size(trB.h) - 1]);
    var left = toothedEdge(trA, hTop, -1, definition);
    var right = toothedEdge(trB, hTop, 1, definition);
    // the parts must leave room for a wall between them at every height
    for (var j = 0; j < size(trA.h); j += 1)
    {
        if (trA.h[j] > hTop)
            break;
        var room = interp(trB.h, trB.s, trA.h[j]) - trA.s[j] - 2 * definition.gap;
        if (room < definition.thickness / 2)
            return { "error" : "The two parts are too close (or overlap) across this plane to fit a wall between them." };
    }
    var pts = left.points;
    for (var k = size(right.points) - 1; k >= 0; k -= 1)
        pts = append(pts, right.points[k]);
    return { "points" : pts, "teeth" : left.teeth + right.teeth, "height" : hTop,
            "padFrom" : left.bottomS, "padTo" : right.bottomS };
}

function dedupe(pts is array) returns array
{
    var out = [];
    for (var p in pts)
    {
        if (size(out) == 0 || norm(p - out[size(out) - 1]) > TOL)
            out = append(out, p);
    }
    return out;
}

function closeLoop(pts is array) returns array
{
    if (norm(pts[size(pts) - 1] - pts[0]) > TOL)
        pts = append(pts, pts[0]);
    return pts;
}
