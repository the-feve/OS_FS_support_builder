# OS_FS_support_builder — working rules

Onshape FeatureScript that models thin breakaway support walls for tall, thin 3D-printed parts.

- Before writing FeatureScript geometry code, read
  [`OS_FeatureScript_Reference/FeatureScript_Reference_Notes.md`](https://github.com/the-feve/OS_FeatureScript_Reference/blob/main/FeatureScript_Reference_Notes.md)
  (local: `C:\Users\matth\repos\OS_FeatureScript_Reference`). How we work (API budget,
  paste-not-push, checkpoint testing) is in that repo's `ONSHAPE_WORKFLOW.md` — not restated here.
- Log new gotchas in that repo, not here.
- `src/SupportWall.fs` is the local source of truth. Hand it over for paste; don't `put_featurescript`.
- Profile math lives in `buildProfile` (pure 2D, no context). `tools/profile_check.py` is a
  Python port of it — keep the two in sync and run it after changing the profile logic.
