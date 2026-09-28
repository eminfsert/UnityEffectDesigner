# 014: Notes while 013 §2 runs (no new test)

Plugin 0.5.7 (package unchanged, 0.5.5) only changes `vfxtex.py puff`: a round body with
6 bumps around its edge (a cartoon cloud) and strokes where the first 3 bumps meet the body,
instead of four overlapping lobes (the clover you saw). Defaults: `--lobes 6 --strokes 3`.

- As before, **do not switch versions in the middle of the iterate run**. If the iterate
  needs new puffs, `puff_seams.py` from 009 is fine; or update between rounds only if the
  user agrees, and say which version made which texture.
- The compact capture response is still ~15 KB for CoinPickup (the per-time color stats).
  That fits; if a larger block does not, read `resultFile` instead of asking for `detail
  "full"`.
