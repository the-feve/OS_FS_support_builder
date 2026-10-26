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
  far too slow, and there's no "merge coplanar faces" op. Merge before import instead ([mesh2step](https://github.com/the-feve/mesh2step)).

## Converter

Moved to its own repo: **[the-feve/mesh2step](https://github.com/the-feve/mesh2step)**. Drag
STL / OBJ / PLY files onto `mesh2step.bat` to get clean STEP or IGES files (Blender + OpenCascade).

## Sources
- Onshape meshes / mixed modeling: https://cad.onshape.com/help/Content/PartStudio/mixed_modeling.htm ,
  https://cad.onshape.com/help/Content/Document/working_with_imported_cad.htm
- Onshape changelog: https://www.onshape.com/en/changelog/
- FreeCAD Shape from Mesh: https://wiki.freecad.org/Part_ShapeFromMesh/en
- FreeCAD batch script discussion: https://forum.bambulab.com/t/stl-to-step-conversion-script-for-freecad/221116
- Fusion Convert Mesh: https://help.autodesk.com/cloudhelp/ENU/Fusion-Mesh/files/MESH-CONVERT-TO-SOLID.htm
- Rhino SubD→NURBS: https://discourse.mcneel.com/t/exporting-fbx-mesh-to-step-or-iges/110762
- imagetostl: https://imagetostl.com/convert/file/stl/to/step
