# stansGameV2

Unity game project. Owner: spuzzelsnest. Collaborator: Stan (Jack-Stan).
Repository: https://github.com/spuzzelsnest/stansgamev2, default branch `main`.

## Layout

| Path | What |
|---|---|
| `Assets/`, `Packages/`, `ProjectSettings/` | The Unity project. The editor version is in `ProjectSettings/ProjectVersion.txt`; open the project with exactly that version. |
| `3d-model/` | Blender source for the Stan Airborne character (`final/stan_airborne.blend`), renders and the build script. Kept outside `Assets/` on purpose so Unity does not import `.blend` files (that needs Blender on every machine). To use the character in the game, export an FBX into `Assets/` instead. |
| `vault/` | Obsidian notes for the project. Open `vault/` itself in Obsidian as a vault (not the repository root, or Obsidian indexes `Library/`) and start at `Home.md`. |
| `unity-additions/` | Drop-in C# scripts (first-person view, hit feedback, health bar, F10 settings) kept outside `Assets/` until someone copies them in; see its `README.md`. They use only the game's public API. |
| `_downloads/` | Raw source material (scans, LiDAR tiles, aerial photos). Ignored by git, never committed. |

Read `vault/Home.md` before design work and update the relevant note when a design decision changes.

## Git rules

- **Never commit Unity-generated folders:** `Library/`, `Temp/`, `Logs/`, `obj/`, `UserSettings/`, `Build/`, `Builds/`. Unity regenerates them. On 2026-11-11 a committed `Library/` (1.15 GB, files up to 319 MB) made GitHub reject the first push with `GH001: Large files detected`.
- **Git LFS is required.** Run `git lfs install` once per machine before cloning or committing. `.gitattributes` routes binaries (`.fbx`, `.blend`, `.png`, `.wav`, `.dll` and more) through LFS. When adding a new binary file type, add it to `.gitattributes` first, then commit the files.
- GitHub rejects any file over 100 MB that is not in LFS. Before pushing a large change, check `git status` for anything unexpected and `git lfs ls-files` for the binaries.
- `.gitignore` covers the Unity cache folders both at the repository root and one folder down (`/StansGame/Library/`), but never for folders directly inside `Assets/`, `Packages/` or `ProjectSettings/`.
- To bring an existing local project with a rejected history onto `main`, use `tools/join-repo.ps1` (see `README.md`); do not force-push.
- Player builds go out as GitHub Releases (zip of the build folder without `*_BackUpThisFolder_ButDontShipItWithYourGame/`), never as commits.
- Each line in `.gitattributes` and `.gitignore` holds one rule. A rule written on the same line as a `#` comment is ignored.
- Keep every `.meta` file with its asset. Move or rename assets inside the Unity editor, not in Explorer, so the `.meta` files follow.
- Project Settings, Editor: Asset Serialization = **Force Text**, Version Control = **Visible Meta Files**.
- Work on a branch per feature and merge into `main`. Do not force-push `main`.
- Unity scene and prefab files merge with UnityYAMLMerge (`merge=unityyamlmerge` in `.gitattributes`). Set the driver once per machine:

  ```bash
  git config merge.unityyamlmerge.name "Unity SmartMerge"
  git config merge.unityyamlmerge.driver "'<Unity editor folder>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force %O %B %A %A"
  ```

## Working on this project with Claude

- Code changes go in C# scripts under `Assets/`. Never edit anything under `Library/`.
- Do not hand-edit `.unity`, `.prefab` or `.asset` YAML unless asked; prefer changes through scripts or the editor.
- Run tests headless when the project has them:

  ```bash
  "<Unity editor folder>/Editor/Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -logFile -
  ```

- The character build script (`3d-model/build_stan_airborne.py`) has hard-coded paths from the machine it was written on; see `vault/Building the Model.md` before running it.
- Ask before pushing, force-pushing, deleting branches or rewriting history.
