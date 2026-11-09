# Workflow: release builds for friends

1. `tools/check.sh` green; working tree committed (builds should match a commit).
2. `tools/export.sh` — exports Windows/macOS/Linux, **verifies** each (game code `data_VioVau_*`/`VioVau.dll` present, data pack size sane), zips with `PLAYING.txt`. If it fails, fix the cause; never hand out a build it didn't verify.
3. Smoke-test the macOS build: unzip `build/VioletVaults-macos.zip` to a temp dir and run the app's binary with `-- --host --start-at=1 --autopilot --port=7799`; confirm a run starts (log shows `entered`).
4. Report the zip paths and sizes. If controls/joining changed, update `tools/friends-readme.txt` and the README first.

Needs export templates: Godot editor → Manage Export Templates, or unpacked into `build/godot-templates/`.
