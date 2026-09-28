# Test requests

A working channel between the development session (cloud, writes this branch) and a local
Claude session in the user's Unity project, which reads new files here and runs the tests.
Each request is `NNN-short-name.md`; results come back outside the repo, keyed by NNN.
This folder is scaffolding for development and should be removed before merging.

Limits the local session keeps: test effects under `Assets/VFX/**`, recipes, captures,
compile reports and measurements only. No package installs, game code or scene changes
without the user's approval.
