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

## HANK (project status)

This project has a status card in the-feve/00_Hank_Projects: the `projects/*.json` file(s)
whose `repos` list this repo (format: `docs/card-format.md` there). Matt reads it at
fevergeon.com/hank/.

- When Matt says **park it**, or before you finish a session that changed anything:
  update HANDOFF.md, then the card: where it's at, the ONE next step, Matt's to-dos
  (keep his ticks), a fresh standalone prompt, this session's id, today's date.
  Follow `.claude/skills/hank/SKILL.md` → *park it* in that repo.
- Cloud session: attach it with `add_repo` (push), clone, pull right before editing (the page
  commits Matt's ticks any time), edit, `node check.mjs --fix`, commit to its `main`.
  On the PC: `C:\Users\matth\repos\00_Hank_Projects`.
- Keep it short and plain: Matt has ADHD and reads it on his phone.
