# OS_FS_support_builder

Onshape custom feature **Print Support Wall**: models thin breakaway support walls for tall, thin
3D-printed parts. Each wall stands on the print bed, follows the part's edge however it
curves, and touches the part only through a row of small teeth ("attach points").

![profile preview](docs/profile_preview.png)

*Wall profiles from `tools/profile_check.py` for three edge shapes: a leaning curved fin, an S-bulge,
and a part whose base sits above the bed.*

## Inputs

| Input | What it does |
| --- | --- |
| Parts to support | One part to brace it, or two to bridge them. Solids or imported meshes |
| Support planes | One wall per plane (multi-select). The wall is centred on the plane |
| Flip support side | Which side of the part the wall stands on |
| Print bed plane | The build plate. "Up" is taken as the side the part is on |
| Max support height | Wall stops here or at the top of the part, whichever is lower |
| Wall thickness | Default 1.2 mm (3 lines at 0.4 mm) |
| Attach points by | **Count** (evenly spread) or **Spacing** (fixed pitch, centred) |
| Gap to part (attach point length) | Clearance between the wall and the part. The attach points bridge it. Held across the whole wall thickness |
| Attach point width / height | Size of each contact neck at the part: across the wall × up (default **1 × 1 mm**). Separate from the wall thickness, so a thick wall for a tall support still breaks off cleanly. Each neck has a flat top and a 45° underside, so it prints without support |
| Contact overlap | How far each attach point buries into the part so it fuses in the slicer (default 0.1 mm). Its tip reaches the nearest point across its width |
| Foot reach on bed | How far the wall's foot extends away from the part |
| Top width | Minimum wall width at the top, and the minimum width anywhere |
| Add bed pad | Thin pad under the wall bottom for adhesion (width, thickness) |
| Merge supports into part | Union walls into the part so it exports as one STL |

## How it works

1. Rays are cast across each support plane at ~1 mm height steps toward the part. The first hit at
   each height traces the part's edge in that plane, so any curve works.
2. The wall's edge is offset by the gap. Separate small attach points (necks, 1 × 1 mm contact by
   default) bridge the gap from inside the wall to just into the part, with a 45° underside (no
   overhangs).
3. The outer edge runs straight from the top to the foot, but never comes closer than *top width*
   to the inner edge.
4. The 2D profile is sketched on the plane, extruded to wall thickness, and the bed pad is unioned on.

If the part's base is above the bed (curved bottoms), the wall drops straight down from the
lowest point of contact.

## Supporting an STL part

Select the imported mesh directly. *Part to support* accepts mesh bodies (STL/OBJ/3MF), and
ray-casting works on them (confirmed 2026-10-27 on `eye_mesh.stl`). Perforated parts are fine:
rays that slip through holes are filtered out.

## Bridging two parts

Pick **two parts** to build one wall between them, with teeth on both sides and one shared foot.
The wall is as tall as the lower of the two traces (capped by *Max support height*). If the parts
are too close across the plane to fit a wall, the feature says so. *Flip support side*, *Foot
reach* and *Top width* only apply to the one-part mode.

## Install (paste flow)

1. In your Onshape document, create a Feature Studio.
2. Copy `src/SupportWall.fs` (PowerShell: `Get-Content -Raw src\SupportWall.fs | Set-Clipboard`).
3. Ctrl+A, Ctrl+V in the Studio, keeping the `FeatureScript <n>;` line and import version Onshape
   generated if they differ from this file's 3083, then Commit.
4. Open the Notices panel (glasses icon) and report anything listed.

## Status

- **v1 runs in Onshape** (2026-10-27), including directly on an imported STL mesh.
- **v3 (current file) not yet pasted:** trace through holes, gap across the full wall thickness,
  two-part bridge, separate 1 × 1 mm attach-point necks. Its profile math is checked in `tools/profile_check.py`.

## Roadmap
- **Drag handle for attach-point count/spacing.** Onshape's feature dialog has no slider widget
  that I know of. The closest equivalent is an on-canvas drag arrow (a manipulator) along the
  wall's contact edge that changes spacing live. Planned once v1 compiles.
- Triangular gusset feet (mouse-ear style) as an alternative to the flat pad.
- Normal-direction gap (currently horizontal, so clearance shrinks on steep overhangs:
  about gap × cos(lean)).

## Related docs
- [docs/STL_to_STEP.md](docs/STL_to_STEP.md): getting STL files into Onshape cleanly. Converter now lives in [the-feve/mesh2step](https://github.com/the-feve/mesh2step)
- [docs/prior_art.md](docs/prior_art.md): what already exists
