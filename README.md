# stansgamev2

Game project by spuzzelsnest and Stan.

- `3d-model/`: the Stan Airborne character (Blender 5.3 + MPFB 2.0.18), its build script and renders.
- `vault/`: Obsidian notes for the project. Open the `vault/` folder in Obsidian as a vault and start at `Home.md`.
- `CLAUDE.md`: project rules for Claude Code and for git (LFS, what never gets committed).
- `unity-additions/`: drop-in scripts for first-person view, hit feedback, a health bar and an F10 settings window (see its README).
- `tools/join-repo.ps1`: puts an existing local Unity project on top of `main` without losing any file (see below).

## Adding an existing Unity project (first push)

Use this when your project folder already has its own git history that GitHub rejected (for example `GH001: Large files detected` because `Library/` was committed). A plain `git pull` does not work in that case: the local history is unrelated to `main`, and the large files stay in it.

1. Close Unity. Install [Git LFS](https://git-lfs.com) if `git lfs version` gives an error.
2. Download [`tools/join-repo.ps1`](tools/join-repo.ps1) (Raw, then Save as) to any folder.
3. In PowerShell, from that folder, run it against your project folder (the folder that holds `Assets/`, or the folder one level above it):

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\join-repo.ps1 -ProjectPath "D:\path\to\your\project"
   ```

   It copies the project to `<folder>-backup-<timestamp>` first (without Unity's `Library/` cache), moves the old `.git` aside to `.git-before-join-<timestamp>`, takes `main` from GitHub, adds the files that only exist on GitHub, and commits your project on top. Local files are never deleted or overwritten; only `.gitignore` and `.gitattributes` come from GitHub, and your old ones are kept as `*.before-join`. Files over 50 MB that no LFS rule covers are moved to LFS by path. It stops without committing if `Library/` or `Temp/` would be included.
4. Read the summary (file count, sizes, largest files). When it looks right, push:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\join-repo.ps1 -ProjectPath "D:\path\to\your\project" -PushOnly
   ```

5. Open the project in Unity again. From now on, work with the normal `git pull` / `git commit` / `git push`.

Builds of the game are published as [GitHub Releases](https://github.com/spuzzelsnest/stansgamev2/releases), not committed.

![Stan Airborne, front](3d-model/final/full_front.png)
