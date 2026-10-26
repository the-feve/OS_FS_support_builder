# Prior art (web search, 2026-10-26)

**Bottom line:** the idea exists in industrial build-prep software, but not as a parametric CAD
feature. I found no Onshape FeatureScript or App Store app for it. The App Store and
Printables/Thingiverse were not browsed directly.

| Tool | What it does | Gap vs. this feature |
| --- | --- | --- |
| [GrabCAD Print Wall Stabilizer](https://resources.grabcad.com/stabilizer-walls-best-practices/) | Stabilizer walls for tall, thin parts with a set **number of contact points**, depth, and pierce distance | Closest in purpose. Stratasys FDM only, auto-placed, not CAD |
| [Materialise Magics block supports](https://reseller.materialise.com/fr/node/4227) / [Ansys Additive Prep](https://ansyshelp.ansys.com/public/Views/Secured/corp/v242/en/add_prep/add_prep_block_support_params.html) | Thin walls with **teeth**, perforation, fragmentation | Closest in geometry. Mesh build prep, auto under overhangs, metal/SLA |
| [Fusion Additive](https://help.autodesk.com/cloudhelp/ENU/Fusion-CAM/files/MFG-AM-HOW-TO-ADD-SOLID-BAR-SUPPORTS.htm) | Volume/bar supports driven by overhang angle | No user-plane wall, no teeth |
| [Cura Cylindric Custom Support](https://all3dp.com/2/cura-custom-supports/) | Click-placed primitive supports | Doesn't follow edges, no teeth |
| Prusa / Bambu / Orca | Enforcers, blockers, painting, tree supports | [Won't support vertical faces even when painted](https://forum.bambulab.com/t/supports-to-stabilize-vertical-surfaces/39137) |
| Onshape built-in Rib, community scripts | Solid ribs | No bed grounding, no breakaway contacts |

**Common practice:** people hand-model fins and "stabilizer walls" with about a 0.1–0.2 mm gap or small tabs.

**What's new here:** a parametric, history-based feature that puts a wall on a user plane,
follows a curved edge, makes the number of contact teeth easy to change, and grounds the wall
on a chosen bed plane.
