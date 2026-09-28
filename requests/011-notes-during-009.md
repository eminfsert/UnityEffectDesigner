# 011: Notes while 009 runs (no new test)

Plugin 0.5.3 (package unchanged, 0.5.2) changes only `vfxref.py`: a `smoke` class (the
background's hue but darker: translucent dark smoke over grass) split out of `mid`, a
lower brightness bound for automatic background hues, and `compare` reading the capture
background from the bottom corners (`summary.background_used`). On the references it reads:
bright 52 → 33 → 19 → 1 → 2 %, ink up to 10 %, smoke 1 → 33 → 73 → 78 % (0 → 1.2 s).

- **Do not switch versions in the middle of 009**: finish it on the version you started,
  so all critic rounds compare the same way. Update afterwards and re-run
  `vfxref.py sheet` + one `compare` of the final capture, so the report has the smoke curve.
- **ref_01 (the white-hot dome frame)**: the development session has it from the user's
  chat; it is not in the project. Ask the user to add it as `ref_01.png` if they still have
  it (it is the first of the five screenshots they shared in chat: the big bright yellow
  dome). Until then, keep your spec note that the first frame's numbers come from the
  development session.
- The 008 §5 "expected warning" on the dark compare was my mistake: with a ground filling
  the frame, ink is measured against the ground, which is valid. The warning now depends
  on the background actually measured.
