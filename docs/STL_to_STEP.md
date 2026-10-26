# STL → STEP for Onshape

Research summary, 2026-10-26. Vendor capabilities are from web sources (linked).

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

## Cleanup inside Onshape (searched 2026-10-26)

No community FeatureScript or app found that merges facets into clean faces after import.
- **InStep** (Onshape App Store) converts watertight STL to B-rep, but struggles over ~30k polygons,
  so decimate first. ([forum](https://forum.onshape.com/discussion/3864/importing-a-mesh-file-into-onshape))
- **Boolean Plus** (MBartlett21) is a community feature for working *with* meshes, not converting them.
  ([forum](https://forum.onshape.com/discussion/5617/how-to-turn-mesh-to-part-or-some-other-solution))
- A FeatureScript merger isn't practical at this scale: evaluating a million faces one by one is
  far too slow, and there's no "merge coplanar faces" op. Merge before import instead (mesh2step below).

## mesh2step: one-step converter (Blender + OpenCascade)

`tools/mesh2step/`. **Drag STL / OBJ / PLY files onto `mesh2step.bat`** and a `NAME.step`
appears next to each one. For the right-click menu: Win+R, `shell:sendto`, then drop a shortcut
to `mesh2step.bat` in that folder.

You need **Blender 4.2 or newer** (blender.org). On the first run, the script uses Blender's
own Python to install OpenCascade (pip package `cadquery-ocp`, the CAD kernel FreeCAD uses,
about 300 MB) into `%LOCALAPPDATA%\mesh2step\pylib`. Nothing else on the system is changed.
Delete that folder to uninstall.

What it does to each file:
1. **Repair:** merges duplicate vertices, removes degenerate and loose bits, fills holes, fixes normals.
2. **Planar merge:** triangles within 0.5° of flat merge into one face. This is lossless and
   is what gets rid of the "million faces" on flat areas.
3. **Decimate:** only if the part is still over 10,000 triangles, which in practice means curved parts.
4. **Build the CAD solid:** one face per merged region, sewn into a closed solid, with coplanar
   neighbours unified. Then it checks validity and writes STEP.

Options (add after the file names, from a terminal):

| Option | Default | Meaning |
| --- | --- | --- |
| `--faces N` | 10000 | Max triangles before building the solid (0 = never decimate) |
| `--angle A` | 0.5 | Planar-merge angle in degrees. Raise to 1–2 for noisy scans |
| `--iges` | off | Write `.igs` instead of `.step` |
| `--keep-stl` | off | Also save the cleaned mesh as `NAME_clean.stl` |

Tested 2026-10-26 on Linux with Blender 5.2.3 (bpy) and cadquery-ocp 8.0.1. The `.bat` launcher
hasn't been run on Windows yet.

| Test mesh | In | Out | Time |
| --- | --- | --- | --- |
| Box + cylinder, flat faces subdivided, hole punched | 50,170 triangles | **264 faces**, 2 valid solids, exact volume | 4 s |
| Blender monkey, subdivided (organic, open eyes) | 251,904 triangles | 10,000 faces, 3 valid solids | 25 s |

**Limit:** curved surfaces stay faceted, just with far fewer facets. The script prints a note
when a part is mostly curved. For true curves use:
- **Fusion → Mesh → Convert Mesh → Prismatic** for mechanical parts (commercial licence)
- **Rhino 8: QuadRemesh → ToSubD → ToNURBS** for organic shapes

Blender can't write STEP or IGES itself; those are B-rep CAD formats and Blender is mesh-only.
That's why OpenCascade does the final step.

## Sources
- Onshape meshes / mixed modeling: https://cad.onshape.com/help/Content/PartStudio/mixed_modeling.htm ,
  https://cad.onshape.com/help/Content/Document/working_with_imported_cad.htm
- Onshape changelog: https://www.onshape.com/en/changelog/
- FreeCAD Shape from Mesh: https://wiki.freecad.org/Part_ShapeFromMesh/en
- FreeCAD batch script discussion: https://forum.bambulab.com/t/stl-to-step-conversion-script-for-freecad/221116
- Fusion Convert Mesh: https://help.autodesk.com/cloudhelp/ENU/Fusion-Mesh/files/MESH-CONVERT-TO-SOLID.htm
- Rhino SubD→NURBS: https://discourse.mcneel.com/t/exporting-fbx-mesh-to-step-or-iges/110762
- imagetostl: https://imagetostl.com/convert/file/stl/to/step
