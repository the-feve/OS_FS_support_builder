# stl2step.py — headless FreeCAD (0.21 / 1.x) STL -> STEP batch converter.
# UNVERIFIED until first real run; see docs/STL_to_STEP.md.
#
# Usage:  STL2STEP="in.stl|folder [out_dir] [tol_mm=0.05] [max_faces=0]" freecadcmd stl2step.py
# (Args go in an env var because freecadcmd treats extra CLI args as files to open.)
import os, sys, glob, time
import FreeCAD, Mesh, Part

args = os.environ.get("STL2STEP", "").split()
if not args:
    sys.exit("set STL2STEP='input.stl|folder [out_dir] [tol] [max_faces]'")
src = args[0]
out_dir = args[1] if len(args) > 1 else None
tol = float(args[2]) if len(args) > 2 else 0.05      # sewing tolerance, mm
target = int(args[3]) if len(args) > 3 else 0        # 0 = no decimation

if os.path.isdir(src):
    files = sorted(set(glob.glob(os.path.join(src, "*.stl")) + glob.glob(os.path.join(src, "*.STL"))))
else:
    files = [src]


def convert(path):
    t0 = time.time()
    m = Mesh.Mesh(path)
    # Repair first: makeShapeFromMesh does no validation.
    m.removeDuplicatedPoints()
    m.removeDuplicatedFacets()
    m.fixDegenerations(1e-6)
    m.removeNonManifolds()
    m.fixIndices()
    m.harmonizeNormals()
    if target and m.CountFacets > target:
        m.decimate(target)
    print(f"{os.path.basename(path)}: {m.CountFacets} facets, closed={m.isSolid()}")

    shp = Part.Shape()
    shp.makeShapeFromMesh(m.Topology, tol)
    try:
        solid = Part.Solid(Part.Shell(shp.Faces))
        if solid.Volume < 0:
            solid.reverse()
        result = solid.removeSplitter()          # merge coplanar faces (FreeCAD "Refine")
        if not result.isValid():
            result.fix(tol, tol, tol)
        kind = "solid"
    except Exception as e:                       # open / non-manifold mesh
        print(f"  solid failed ({e}); exporting refined shell")
        result = shp.removeSplitter()
        kind = "shell"

    out = os.path.join(out_dir or os.path.dirname(path),
                       os.path.splitext(os.path.basename(path))[0] + ".step")
    result.exportStep(out)
    print(f"  -> {out} [{kind}, {len(result.Faces)} faces, {time.time() - t0:.1f}s]")


if out_dir:
    os.makedirs(out_dir, exist_ok=True)
for f in files:
    try:
        convert(f)
    except Exception as e:
        print("FAILED", f, e)
