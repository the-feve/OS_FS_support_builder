# mesh2step.py: STL/OBJ/PLY -> cleaned STEP (or IGES), run inside headless Blender.
#
#   blender -b --factory-startup -P mesh2step.py -- IN.stl [IN2.stl ...]
#           [--faces 10000] [--angle 0.5] [--iges] [--keep-stl] [--pylib DIR]
#
# Blender cleans the mesh; OpenCascade (pip package "cadquery-ocp", the kernel FreeCAD uses)
# builds the solid and writes the CAD file. Output lands next to each input as NAME.step.
# The first run installs cadquery-ocp into --pylib using Blender's bundled Python.
#
# Pipeline per file:
#   1. repair: merge duplicate verts, drop degenerate/loose bits, fill holes, fix normals
#   2. planar dissolve: merge near-flat triangles (--angle degrees) into n-gons -> lossless
#   3. decimate to --faces only if still denser than that (0 = never)
#   4. one planar CAD face per n-gon (non-flat n-gons are split into triangles), sew, make solid
#   5. unify same-domain faces (merges any coplanar neighbours left), check, write STEP/IGES
import sys, os, math, time, argparse

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser(prog="mesh2step.py")
ap.add_argument("inputs", nargs="+")
ap.add_argument("--faces", type=int, default=10000, help="max triangles after cleanup (0 = no decimation)")
ap.add_argument("--angle", type=float, default=0.5, help="planar merge angle, degrees (0 = off)")
ap.add_argument("--merge", type=float, default=1e-4, help="merge-by-distance tolerance (mm)")
ap.add_argument("--sew", type=float, default=1e-3, help="sewing tolerance (mm)")
ap.add_argument("--iges", action="store_true", help="write IGES instead of STEP")
ap.add_argument("--keep-stl", action="store_true", help="also write NAME_clean.stl")
ap.add_argument("--pylib", default=os.environ.get("MESH2STEP_PYLIB", ""),
                help="folder for the OpenCascade install (default: %%LOCALAPPDATA%%\\mesh2step\\pylib)")
a = ap.parse_args(argv)

if not a.pylib:
    base = os.environ.get("LOCALAPPDATA") or os.path.join(os.path.expanduser("~"), ".local", "share")
    a.pylib = os.path.join(base, "mesh2step", "pylib")
if os.path.isdir(a.pylib):
    sys.path.insert(0, a.pylib)


def ensure_ocp():
    """First run: pip-install cadquery-ocp into a private folder with Blender's own Python."""
    try:
        import OCP  # noqa: F401
        return
    except ImportError:
        pass
    import subprocess
    print(f"[mesh2step] First run: installing OpenCascade (cadquery-ocp, ~300 MB) into {a.pylib}", flush=True)
    py = sys.executable
    if subprocess.call([py, "-m", "pip", "--version"]) != 0:
        subprocess.check_call([py, "-m", "ensurepip", "--user"])
    os.makedirs(a.pylib, exist_ok=True)
    subprocess.check_call([py, "-m", "pip", "install", "--disable-pip-version-check",
                           "--target", a.pylib, "cadquery-ocp"])
    sys.path.insert(0, a.pylib)


ensure_ocp()

import bpy, bmesh
try:
    from OCP.gp import gp_Pnt
    from OCP.BRepBuilderAPI import (BRepBuilderAPI_MakePolygon, BRepBuilderAPI_MakeFace,
                                    BRepBuilderAPI_Sewing, BRepBuilderAPI_MakeSolid)
    from OCP.BRep import BRep_Builder, BRep_Tool
    from OCP.TopoDS import TopoDS, TopoDS_Compound
    from OCP.TopAbs import TopAbs_SHELL, TopAbs_FACE
    from OCP.TopExp import TopExp_Explorer
    from OCP.ShapeFix import ShapeFix_Solid, ShapeFix_Shape
    from OCP.ShapeUpgrade import ShapeUpgrade_UnifySameDomain
    from OCP.BRepCheck import BRepCheck_Analyzer
    from OCP.GProp import GProp_GProps
    from OCP.BRepGProp import BRepGProp
    from OCP.STEPControl import STEPControl_Writer, STEPControl_AsIs
    from OCP.IGESControl import IGESControl_Writer
    from OCP.Interface import Interface_Static
    from OCP.IFSelect import IFSelect_RetDone
except ImportError:
    sys.exit("OpenCascade (cadquery-ocp) couldn't be loaded. Delete the pylib folder and run again "
             "to reinstall: " + a.pylib)


def static(cls, name):
    """OCP < 8 names static methods with a _s suffix; OCP 8 dropped it."""
    return getattr(cls, name + "_s", None) or getattr(cls, name)


def log(msg):
    print("[mesh2step] " + msg, flush=True)


def count_faces(shape):
    n, ex = 0, TopExp_Explorer(shape, TopAbs_FACE)
    while ex.More():
        n += 1
        ex.Next()
    return n


# ---------------------------------------------------------------- Blender side
def load_mesh(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(path)[1].lower()
    if ext == ".stl":
        bpy.ops.wm.stl_import(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    elif ext == ".ply":
        bpy.ops.wm.ply_import(filepath=path)
    else:
        raise ValueError(f"unsupported type {ext} (use .stl, .obj or .ply)")
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not objs:
        raise ValueError("no mesh in file")
    for o in bpy.context.scene.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def repair(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=a.merge)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=a.merge)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
    boundary = [e for e in bm.edges if e.is_boundary]
    if boundary:
        bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
        log(f"  filled holes along {len(boundary)} open edges")
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def planar_merge(obj):
    """Merge near-flat triangles into n-gons; split any n-gon that isn't truly flat."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    if a.angle > 0:
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(a.angle), use_dissolve_boundaries=False,
                                 verts=bm.verts, edges=bm.edges)
    bm.normal_update()
    bent = []
    for f in bm.faces:
        if len(f.verts) > 3:
            c, nrm = f.calc_center_median(), f.normal
            if max(abs((v.co - c).dot(nrm)) for v in f.verts) > a.sew * 0.5:
                bent.append(f)
    if bent:
        bmesh.ops.triangulate(bm, faces=bent)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def decimate(obj, target):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    n = len(obj.data.polygons)
    mod = obj.modifiers.new("decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = target / n
    bpy.ops.object.modifier_apply(modifier=mod.name)
    log(f"  decimated {n} -> {len(obj.data.polygons)} triangles")


# ---------------------------------------------------------------- OpenCascade side
def planar_face(points):
    poly = BRepBuilderAPI_MakePolygon()
    for pt in points:
        poly.Add(pt)
    poly.Close()
    if not poly.IsDone():
        return None
    mf = BRepBuilderAPI_MakeFace(poly.Wire(), True)
    return mf.Face() if mf.IsDone() else None


def to_solid(obj):
    me = obj.data
    me.calc_loop_triangles()
    tris_of = {}
    for lt in me.loop_triangles:
        tris_of.setdefault(lt.polygon_index, []).append(lt.vertices)
    verts = [gp_Pnt(v.co.x, v.co.y, v.co.z) for v in me.vertices]
    sew = BRepBuilderAPI_Sewing(a.sew)
    split, skipped = 0, 0
    for p in me.polygons:
        f = planar_face([verts[i] for i in p.vertices])
        if f is not None:
            sew.Add(f)
            continue
        # Not flat enough for OpenCascade: fall back to this face's triangles.
        split += 1
        for tri in tris_of.get(p.index, []):
            tf = planar_face([verts[i] for i in tri])
            if tf is not None:
                sew.Add(tf)
            else:
                skipped += 1
    if split:
        log(f"  split {split} slightly bent faces into triangles")
    if skipped:
        log(f"  skipped {skipped} zero-area triangles")
    sew.Perform()
    sewn = sew.SewedShape()

    solids, open_shells = [], []
    ex = TopExp_Explorer(sewn, TopAbs_SHELL)
    while ex.More():
        shell = static(TopoDS, "Shell")(ex.Current())
        ms = BRepBuilderAPI_MakeSolid(shell) if static(BRep_Tool, "IsClosed")(shell) else None
        if ms is not None and ms.IsDone():
            fix = ShapeFix_Solid(ms.Solid())
            fix.Perform()
            solids.append(fix.Solid())
        else:
            open_shells.append(shell)
        ex.Next()
    if open_shells:
        log(f"  WARNING: {len(open_shells)} shell(s) still open after hole filling; written as surfaces")
    if len(solids) > 1:
        log(f"  {len(solids)} separate bodies")

    parts = solids + open_shells
    if not parts:
        raise RuntimeError("sewing produced no shells")
    if len(parts) == 1:
        shape = parts[0]
    else:
        shape, b = TopoDS_Compound(), BRep_Builder()
        b.MakeCompound(shape)
        for p in parts:
            b.Add(shape, p)

    unify = ShapeUpgrade_UnifySameDomain(shape, True, True, False)
    unify.Build()
    shape = unify.Shape()
    if not BRepCheck_Analyzer(shape).IsValid():
        sf = ShapeFix_Shape(shape)
        sf.Perform()
        shape = sf.Shape()
    return shape, len(solids)


def write(shape, out):
    if a.iges:
        static(Interface_Static, "SetCVal")("write.iges.unit", "MM")
        w = IGESControl_Writer("MM", 1)  # 1 = B-rep faces
        w.AddShape(shape)
        w.ComputeModel()
        ok = w.Write(out)
    else:
        static(Interface_Static, "SetCVal")("write.step.unit", "MM")
        w = STEPControl_Writer()
        w.Transfer(shape, STEPControl_AsIs)
        ok = w.Write(out) == IFSelect_RetDone
    if not ok:
        raise RuntimeError("writer failed")


# ---------------------------------------------------------------- main
def convert(path):
    t0 = time.time()
    log(f"{os.path.basename(path)}")
    obj = load_mesh(path)
    log(f"  imported {len(obj.data.polygons)} triangles")
    repair(obj)
    planar_merge(obj)
    if a.faces and tri_count(obj) > a.faces:
        decimate(obj, a.faces)
        repair(obj)
        planar_merge(obj)
    log(f"  cleaned: {len(obj.data.polygons)} faces ({tri_count(obj)} triangles)")

    base = os.path.splitext(path)[0]
    if a.keep_stl:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.ops.wm.stl_export(filepath=base + "_clean.stl", export_selected_objects=True)

    shape, nsolids = to_solid(obj)
    props = GProp_GProps()
    static(BRepGProp, "VolumeProperties")(shape, props)
    out = base + (".igs" if a.iges else ".step")
    write(shape, out)
    log(f"  -> {os.path.basename(out)}: {count_faces(shape)} CAD faces, "
        f"volume {props.Mass() / 1000:.2f} cm3, {time.time() - t0:.1f}s")
    if count_faces(shape) > 0.5 * tri_count(obj):
        log("  note: mostly curved part, so it stays faceted. Fusion Prismatic or Rhino gives true curves.")


failed = 0
for f in a.inputs:
    try:
        convert(os.path.abspath(f))
    except Exception as e:
        failed += 1
        log(f"  FAILED: {e}")
sys.exit(1 if failed else 0)
