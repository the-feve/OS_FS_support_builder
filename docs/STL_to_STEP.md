# STL → STEP for Onshape

Research summary, 2026-10-26. Vendor capabilities are from web sources (linked); the script is
**unverified** until its first real run.

## First question: do you need to convert at all?

Onshape imports STL/OBJ/3MF directly as **mesh bodies**, and *mixed modeling* lets you Boolean,
Shell, Hole, Split, Rib and Offset against them, measure mass, and snap planes and mate
connectors to mesh vertices. Onshape has **no native mesh→solid conversion** (none found in
changelogs through 1.214, Apr 2026).

- **Reference, fit check, or Boolean target** → import the STL directly. No conversion.
- **Need real faces to sketch on, fillet, or mate to** → convert first (below).

## Two kinds of "STEP from STL"

- **Faceted**: every triangle becomes a planar face. Valid solid, but heavy, and holes have no
  axis. "Refine" merges coplanar triangles, so **flat faces come out clean and curved faces stay
  as facet strips**. This is what FreeCAD, OCCT scripts and online converters produce.
- **Reconstructed**: real planes, cylinders and fillets are recognised (prismatic), or NURBS
  patches are fitted over organic shapes. No free one-command tool does this.

## Recommended workflow

| Part type | Tool | Notes |
| --- | --- | --- |
| Mostly flat / prismatic, quick | **FreeCAD batch script** (`tools/stl2step.py`) | Free, one command, whole folders. Rebuild holes with real features in Onshape |
| Mechanical, needs true cylinders and fillets | **Fusion → Mesh → Convert Mesh → Prismatic** | Best quick result. Commercial licence. Decimate to about 10k faces first, and generate face groups if it fails |
| Organic / curved | **Rhino 8**: ShrinkWrap → QuadRemesh → ToSubD → ToNURBS | Clean NURBS. Softens crisp edges |
| Any, pre-clean | MeshLab / Blender | Decimate, close holes, remove non-manifold geometry before converting |

Online converters (e.g. imagetostl) make faceted output too. Don't upload proprietary parts.

## The script

`tools/stl2step.py`: headless FreeCAD. It repairs the mesh, optionally decimates, builds a
faceted solid, merges coplanar faces and writes a STEP next to each STL.

```bash
# one file or a whole folder; optional out dir, sewing tolerance (mm), max faces
STL2STEP="./stls ./steps 0.05 20000" freecadcmd tools/stl2step.py
```
```bat
:: Windows
set STL2STEP=C:\path\stls C:\path\steps 0.05 20000
"C:\Program Files\FreeCAD 1.0\bin\FreeCADCmd.exe" tools\stl2step.py
```

Read the output: if the face count is far below the facet count, the part was mostly flat and the
STEP is clean. If the two counts are close, the part is curved and needs Fusion or Rhino.

To check on the first run: the `Mesh.decimate(int)` overload, `removeSplitter()` on a Solid, and
`Shape.fix` arguments.

## Sources
- Onshape meshes / mixed modeling: https://cad.onshape.com/help/Content/PartStudio/mixed_modeling.htm ,
  https://cad.onshape.com/help/Content/Document/working_with_imported_cad.htm
- Onshape changelog: https://www.onshape.com/en/changelog/
- FreeCAD Shape from Mesh: https://wiki.freecad.org/Part_ShapeFromMesh/en
- FreeCAD batch script discussion: https://forum.bambulab.com/t/stl-to-step-conversion-script-for-freecad/221116
- Fusion Convert Mesh: https://help.autodesk.com/cloudhelp/ENU/Fusion-Mesh/files/MESH-CONVERT-TO-SOLID.htm
- Rhino SubD→NURBS: https://discourse.mcneel.com/t/exporting-fbx-mesh-to-step-or-iges/110762
- imagetostl: https://imagetostl.com/convert/file/stl/to/step
