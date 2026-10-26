FeatureScript 3083;
import(path : "onshape/std/geometry.fs", version : "3083.0");

// Print Support Wall
// Builds thin breakaway support walls for tall, thin 3D-printed parts. Each wall lies on a
// user-picked plane, stands on the print bed plane, and touches the part through a row of
// small teeth ("attach points") that follow the part's edge however it curves.
//
// How the edge is found: rays are cast across the support plane at many heights, toward the
// part. The first hit at each height traces the part's edge in that plane. All profile math
// is plain 2D (s = across the plane toward the part, h = height above the bed).

export enum AttachMode
{
    annotation { "Name" : "Count" }
    COUNT,
    annotation { "Name" : "Spacing" }
    SPACING
}

const WALL_BOUNDS      = { (millimeter) : [0.2, 1.2, 20] } as LengthBoundSpec;
const GAP_BOUNDS       = { (millimeter) : [0, 1.0, 20] } as LengthBoundSpec;
const OVERLAP_BOUNDS   = { (millimeter) : [0, 0.15, 2] } as LengthBoundSpec;
const TIP_BOUNDS       = { (millimeter) : [0.1, 0.6, 10] } as LengthBoundSpec;
const BASE_W_BOUNDS    = { (millimeter) : [0.2, 3, 50] } as LengthBoundSpec;
const SPACING_BOUNDS   = { (millimeter) : [0.5, 8, 500] } as LengthBoundSpec;
const HEIGHT_BOUNDS    = { (millimeter) : [1, 80, 2000] } as LengthBoundSpec;
const FOOT_BOUNDS      = { (millimeter) : [0, 20, 1000] } as LengthBoundSpec;
const TOP_W_BOUNDS     = { (millimeter) : [0.5, 3, 100] } as LengthBoundSpec;
const PAD_W_BOUNDS     = { (millimeter) : [1, 10, 200] } as LengthBoundSpec;
const PAD_T_BOUNDS     = { (millimeter) : [0.1, 0.6, 10] } as LengthBoundSpec;
const COUNT_BOUNDS     = { (unitless) : [1, 12, 500] } as IntegerBoundSpec;

const SAMPLE_STEP = 1 * millimeter;  // edge-trace resolution
const MAX_SAMPLES = 300;             // cap on rays per wall for the edge trace
const OUTER_EVERY = 4;               // outer edge uses every Nth trace sample
const TOL = 1e-6 * meter;

annotation { "Feature Type Name" : "Print Support Wall",
             "Feature Type Description" : "Thin breakaway support walls for tall, thin 3D-printed parts.<br>" ~
                 "Pick the part, one or more support planes, and the print bed plane. Each wall follows " ~
                 "the part's edge in its plane and touches it through small teeth." }
export const printSupportWall = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Part to support", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
        definition.part is Query;

        annotation { "Name" : "Support planes", "Filter" : GeometryType.PLANE }
        definition.supportPlanes is Query;

        annotation { "Name" : "Flip support side", "UIHint" : UIHint.OPPOSITE_DIRECTION }
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
            annotation { "Name" : "Attach point count" }
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

        annotation { "Name" : "Foot reach on bed" }
        isLength(definition.footReach, FOOT_BOUNDS);

        annotation { "Name" : "Top width" }
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
    if (size(evaluateQuery(context, definition.part)) < 1)
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

    // "Up" is the bed normal, pointed toward the part.
    var up = bed.normal;
    var center = (bbox.minCorner + bbox.maxCorner) / 2;
    if (dot(center - bed.origin, up) < 0 * meter)
        up = -up;

    var faces = qOwnedByBody(definition.part, EntityType.FACE);
    var walls = [];
    var report = "";
    for (var i = 0; i < size(planes); i += 1)
    {
        var wallId = id + ("wall" ~ i);
        var res = buildOneWall(context, wallId, planes[i], up, bed.origin, bbox, faces, definition);
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
    bbox is Box3d, faces is Query, definition is map) returns map
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

    // 2D frame in the support plane: d = across (toward the part), w = in-plane up.
    var d = normalize(cross(up, n));
    if (definition.flipSide)
        d = -d;
    var w = normalize(up - n * cosNU);
    var wu = dot(w, up);
    var O = sp.origin - w * (dot(sp.origin - bedOrigin, up) / wu); // plane origin dropped to bed level

    var at = function(s, h)
        {
            return O + d * s + w * (h / wu);
        };

    // Part extent in this frame.
    var sMin = undefined;
    var hMax = undefined;
    for (var c = 0; c < 8; c += 1)
    {
        var corner = vector(c % 2 == 0 ? bbox.minCorner[0] : bbox.maxCorner[0],
            floor(c / 2) % 2 == 0 ? bbox.minCorner[1] : bbox.maxCorner[1],
            floor(c / 4) == 0 ? bbox.minCorner[2] : bbox.maxCorner[2]);
        var cs = dot(corner - O, d);
        var ch = dot(corner - bedOrigin, up);
        if (sMin == undefined || cs < sMin)
            sMin = cs;
        if (hMax == undefined || ch > hMax)
            hMax = ch;
    }
    var rayStart = sMin - 5 * millimeter;

    // Edge position (s) of the part at height h, or undefined if the ray misses.
    var castAt = function(h)
        {
            var hits = try silent(evRaycast(context, {
                        "entities" : faces,
                        "ray" : line(at(rayStart, h), d),
                        "closest" : true
                    }));
            if (hits == undefined || size(hits) == 0)
                return undefined;
            return dot(hits[0].intersection - O, d);
        };

    // Trace the edge from the bed up to the height limit; stop at the first miss after the first hit.
    var hLimit = min(definition.supportHeight, hMax);
    var step = max(SAMPLE_STEP, hLimit / MAX_SAMPLES);
    var sampleH = [];
    var sampleS = [];
    for (var h = step / 2; h <= hLimit; h += step)
    {
        var s = castAt(h);
        if (s == undefined)
        {
            if (size(sampleH) > 0)
                break;
            continue;
        }
        sampleH = append(sampleH, h);
        sampleS = append(sampleS, s);
    }
    if (size(sampleH) < 2)
        throw regenError("Support plane doesn't cross the part below the max support height.",
            ["supportPlanes"], planeQuery);

    var last = size(sampleH) - 1;
    var hStart = sampleH[0];
    var hTop = sampleH[last];

    var interpEdge = function(h)
        {
            if (h <= sampleH[0])
                return sampleS[0];
            for (var j = 1; j < size(sampleH); j += 1)
            {
                if (h <= sampleH[j])
                {
                    var f = (h - sampleH[j - 1]) / (sampleH[j] - sampleH[j - 1]);
                    return sampleS[j - 1] + (sampleS[j] - sampleS[j - 1]) * f;
                }
            }
            return sampleS[last];
        };
    var edgeAt = function(h)
        {
            var s = castAt(h);
            return s == undefined ? interpEdge(h) : s;
        };

    var res = buildProfile(sampleH, sampleS, hStart, hTop, edgeAt, definition);

    // Sketch the profile on a plane offset by half the wall, then extrude through.
    var t = definition.thickness;
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
        var a = at(res.footS, 0 * meter);
        var b = at(res.innerBottomS, 0 * meter);
        var half = padY * (definition.padWidth / 2);
        var c1 = a - half;
        var c2 = b + half;
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
            "height" : hTop
        };
}

// Pure 2D profile math: no context access. Returns closed outline points as vector(s, h).
function buildProfile(sampleH is array, sampleS is array, hStart, hTop, edgeAt is function, definition is map) returns map
{
    var g = definition.gap;
    var ov = definition.overlap;
    var bw = definition.toothBase;
    var tw = min(definition.toothTip, bw);
    var last = size(sampleH) - 1;

    // Tooth centre heights.
    var teeth = [];
    var lo = hStart + bw / 2;
    var hi = hTop - bw / 2;
    if (hi >= lo)
    {
        var minPitch = bw * 1.1;
        var n = 0;
        var pitch = 0 * meter;
        if (definition.attachMode == AttachMode.COUNT)
        {
            n = definition.toothCount;
            var maxN = floor((hi - lo) / minPitch) + 1;
            n = min(n, maxN);
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
    }

    var toothPts = function(hk)
        {
            return [vector(edgeAt(hk - bw / 2) - g, hk - bw / 2),
                    vector(edgeAt(hk - tw / 2) + ov, hk - tw / 2),
                    vector(edgeAt(hk + tw / 2) + ov, hk + tw / 2),
                    vector(edgeAt(hk + bw / 2) - g, hk + bw / 2)];
        };

    // Inner edge, bottom to top, with teeth spliced in.
    var innerBottomS = sampleS[0] - g;
    var pts = [vector(innerBottomS, 0 * meter)];
    var k = 0;
    for (var j = 0; j <= last; j += 1)
    {
        var h = sampleH[j];
        while (k < size(teeth) && teeth[k] - bw / 2 <= h)
        {
            pts = concatenateArrays([pts, toothPts(teeth[k])]);
            k += 1;
        }
        if (k > 0 && h <= teeth[k - 1] + bw / 2)
            continue; // this sample sits inside a tooth
        pts = append(pts, vector(sampleS[j] - g, h));
    }

    // Outer edge: straight line from the top down to a foot on the bed, kept at least topWidth
    // away from the inner edge.
    var minInner = sampleS[0] - g;
    for (var j = 0; j <= last; j += 1)
        minInner = min(minInner, sampleS[j] - g);
    var tW = definition.topWidth;
    var footS = min(minInner - definition.footReach, minInner - tW);
    var outerTop = sampleS[last] - g - tW;
    var outerAt = function(h)
        {
            return footS + (outerTop - footS) * (h / hTop);
        };

    pts = append(pts, vector(outerTop, hTop));
    for (var j = last - 1; j > 0; j -= 1)
    {
        if (j % OUTER_EVERY != 0)
            continue;
        var h = sampleH[j];
        pts = append(pts, vector(min(outerAt(h), sampleS[j] - g - tW), h));
    }
    pts = append(pts, vector(footS, 0 * meter));

    return { "points" : pts, "teeth" : size(teeth), "footS" : footS, "innerBottomS" : innerBottomS };
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
