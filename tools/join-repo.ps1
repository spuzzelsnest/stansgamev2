<#
.SYNOPSIS
    Puts an existing local Unity project on top of the GitHub repository's main branch
    without losing any local file.

.DESCRIPTION
    Use this once on a machine whose project folder already has its own git history that
    GitHub rejected (for example "GH001: Large files detected" after Library/ was committed),
    or that was never connected to this repository.

    What it does, in order:
      1. Checks that git and git-lfs are installed and that git knows your name and e-mail.
      2. Copies the whole project folder to "<folder>-backup-<timestamp>" next to it
         (everything except the Unity cache folders Library/Temp/Logs/obj/UserSettings,
         which Unity rebuilds). Skip with -SkipBackup only if you already have a copy.
      3. Moves an existing .git folder aside to ".git-before-join-<timestamp>" (your old
         history stays on disk) and starts a fresh repository.
      4. Fetches main from GitHub and points the local main branch at it. Your files are
         not touched.
      5. Restores files that only exist on GitHub (3d-model/, vault/, README.md, ...),
         but only where no local file with the same path exists. Local files always win,
         except .gitignore and .gitattributes: those come from GitHub, and your old ones
         are kept as .gitignore.before-join and .gitattributes.before-join.
      6. Safety checks before anything is committed:
           - every Unity project found has its Library/ and Temp/ ignored;
           - no file larger than -MaxPlainMB goes in as a normal git file: those are moved
             to Git LFS by exact path;
           - prints what will be committed (file count and size, normal and LFS).
      7. Commits. Pushes to GitHub only when -Push is given.

    Nothing in the project folder is deleted or overwritten, apart from .gitignore and
    .gitattributes (whose old versions are kept as *.before-join).

.EXAMPLE
    # First run: everything except the push. Read the summary.
    powershell -ExecutionPolicy Bypass -File join-repo.ps1 -ProjectPath "D:\Unity\StansGame"

.EXAMPLE
    # Happy with the summary: push the commit that the first run made.
    powershell -ExecutionPolicy Bypass -File join-repo.ps1 -ProjectPath "D:\Unity\StansGame" -PushOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$Remote = 'https://github.com/spuzzelsnest/stansgamev2.git',
    [string]$Branch = 'main',
    [string]$Message = 'Add the Unity project',
    [int]$MaxPlainMB = 50,
    [switch]$SkipBackup,
    [switch]$Push,
    [switch]$PushOnly
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$cacheDirs = @('Library', 'Temp', 'Logs', 'obj', 'UserSettings')

function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Info([string]$text) { Write-Host "   $text" }
function Warn([string]$text) { Write-Host "   WARNING: $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host ''; Write-Host "STOPPED: $text" -ForegroundColor Red; exit 1 }

function Git {
    # Runs git in the project folder; stops the script on a non-zero exit code.
    # Windows PowerShell turns any stderr line of a native command into an error when
    # stderr is redirected, so the check relies on the exit code only.
    $ErrorActionPreference = 'Continue'
    $out = & git.exe -C $root @args 2>&1
    if ($LASTEXITCODE -ne 0) { Fail ("git $($args -join ' ') failed:`n" + ($out | Out-String)) }
    return $out | ForEach-Object { "$_" }
}

function Quiet {
    # Runs a native command, discards its output and returns only the exit code.
    $ErrorActionPreference = 'Continue'
    $exe, $rest = $args
    & $exe @rest *> $null
    return $LASTEXITCODE
}

# ---------------------------------------------------------------- 0. folder
if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) { Fail "Folder not found: $ProjectPath" }
$root = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath.TrimEnd('\')

if ($PushOnly) {
    Step 'Pushing the commit made by the previous run'
    $head = (Git rev-parse --abbrev-ref HEAD | Out-String).Trim()
    if ($head -ne $Branch) { Fail "Current branch is '$head', expected '$Branch'." }
    Git fetch origin $Branch | Out-Null
    $behind = [int]((Git rev-list --count "HEAD..origin/$Branch" | Out-String).Trim())
    if ($behind -gt 0) { Fail "GitHub has $behind new commit(s) since the first run. Run: git -C `"$root`" pull --rebase origin $Branch   and then -PushOnly again." }
    & git.exe -C $root push -u origin $Branch
    if ($LASTEXITCODE -ne 0) { Fail 'Push failed; see the git output above. Nothing was lost locally.' }
    Step 'Done. The project is on GitHub.'
    exit 0
}

# ---------------------------------------------------------------- 1. tools
Step 'Checking git and Git LFS'
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Fail 'git is not installed (https://git-scm.com/download/win).' }
if ((Quiet git.exe lfs version) -ne 0) { Fail 'Git LFS is not installed (https://git-lfs.com), or not on PATH.' }
Info ((& git.exe --version) + ' / ' + (& git.exe lfs version))
$name = (& git.exe config user.name); $mail = (& git.exe config user.email)
if (-not $name -or -not $mail) { Fail 'git does not know who you are. Run once: git config --global user.name "Your Name"  and  git config --global user.email "you@example.com"' }
Info "Committing as $name <$mail>"

$projects = @(Get-ChildItem -LiteralPath $root -Recurse -Filter 'ProjectVersion.txt' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -eq 'ProjectSettings' -and ('\' + $_.FullName.Substring($root.Length + 1)) -notmatch '\\(Library|Temp)\\' } |
    ForEach-Object { $_.Directory.Parent.FullName })
if ($projects.Count -eq 0) { Fail "No Unity project found under $root (no ProjectSettings\ProjectVersion.txt)." }
foreach ($p in $projects) {
    $rel = if ($p -eq $root) { '(repository root)' } else { $p.Substring($root.Length + 1) }
    Info "Unity project: $rel  ($((Get-Content -LiteralPath (Join-Path $p 'ProjectSettings\ProjectVersion.txt') -TotalCount 1)))"
    if ($p -ne $root -and $p.Substring($root.Length + 1).Contains('\')) {
        Fail "The Unity project sits more than one folder deep ($rel). Move it to the repository root or one folder down, or ask Stan to extend .gitignore."
    }
}

# ---------------------------------------------------------------- 2. backup
if ($SkipBackup) {
    Step 'Backup skipped (-SkipBackup)'
} else {
    $backup = "$root-backup-$stamp"
    Step "Backing up the project to $backup"
    $xd = @()
    foreach ($p in $projects) { foreach ($d in $cacheDirs) { $xd += (Join-Path $p $d) } }
    & robocopy $root $backup /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NP /XD @xd | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Backup copy failed (robocopy exit code $LASTEXITCODE). Nothing was changed." }
    $n = (Get-ChildItem -LiteralPath $backup -Recurse -File -Force | Measure-Object).Count
    Info "Backup done: $n files."
}

# ---------------------------------------------------------------- 3. fresh repository
Step 'Starting a fresh repository (old history kept on disk)'
$oldGit = Join-Path $root '.git'
if (Test-Path -LiteralPath $oldGit) {
    $aside = Join-Path $root ".git-before-join-$stamp"
    Rename-Item -LiteralPath $oldGit -NewName (Split-Path $aside -Leaf)
    Info "Old .git moved to $(Split-Path $aside -Leaf)"
}
Git init | Out-Null
Git symbolic-ref HEAD "refs/heads/$Branch" | Out-Null
Git lfs install --local | Out-Null
Git remote add origin $Remote | Out-Null
# Keep the old history folder and the local backups out of the new repository.
Add-Content -LiteralPath (Join-Path $root '.git\info\exclude') -Value @('/.git-before-join-*/', '*.before-join')

# ---------------------------------------------------------------- 4. fetch GitHub main
Step "Fetching $Branch from $Remote"
Git fetch origin $Branch | Out-Null
Git update-ref "refs/heads/$Branch" "refs/remotes/origin/$Branch" | Out-Null
Git reset --mixed -q | Out-Null
Info ('GitHub is at ' + ((Git log -1 --format='%h %s' HEAD) | Out-String).Trim())

# ---------------------------------------------------------------- 5. GitHub-only files and git rules
Step 'Bringing in files that only exist on GitHub'
foreach ($f in '.gitignore', '.gitattributes') {
    $local = Join-Path $root $f
    if (Test-Path -LiteralPath $local) {
        Copy-Item -LiteralPath $local -Destination "$local.before-join" -Force
        Info "Your old $f is kept as $f.before-join"
    }
    Git checkout HEAD -- $f | Out-Null
}
$remoteFiles = @(Git -c core.quotepath=off ls-tree -r --name-only HEAD)
$missing = @(); $both = @()
foreach ($rf in $remoteFiles) {
    if ($rf -in '.gitignore', '.gitattributes') { continue }
    if (Test-Path -LiteralPath (Join-Path $root ($rf -replace '/', '\'))) { $both += $rf } else { $missing += $rf }
}
if ($missing.Count -gt 0) {
    $list = Join-Path $env:TEMP "join-repo-$stamp.txt"
    [IO.File]::WriteAllLines($list, [string[]]$missing)
    Git --literal-pathspecs checkout HEAD --pathspec-from-file=$list | Out-Null
    Remove-Item -LiteralPath $list
}
Info "$($missing.Count) file(s) added from GitHub."
if ($both.Count -gt 0) {
    Info "$($both.Count) file(s) exist on both sides; your local version is kept:"
    $both | Select-Object -First 15 | ForEach-Object { Info "  $_" }
}
$nested = @(Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in '.gitignore', '.gitattributes' -and $_.DirectoryName -ne $root -and ('\' + $_.FullName.Substring($root.Length + 1)) -notmatch '\\(\.git|\.git-before-join-[^\\]+|Library|Temp)\\' })
foreach ($n in $nested) { Warn "Extra $($n.Name) in $($n.DirectoryName.Substring($root.Length + 1)): its rules also apply. Check it, or delete it if it is an old copy." }

# ---------------------------------------------------------------- 6. safety checks
Step 'Safety checks'
foreach ($p in $projects) {
    foreach ($d in 'Library', 'Temp') {
        $rel = if ($p -eq $root) { "$d/" } else { ($p.Substring($root.Length + 1) -replace '\\', '/') + "/$d/" }
        if ((Quiet git.exe -C $root check-ignore -q --no-index $rel) -ne 0) { Fail "$rel is NOT ignored by .gitignore. Nothing was committed; your files are untouched." }
    }
}
Info 'Unity cache folders (Library/, Temp/) are ignored.'

$candidates = @(Git -c core.quotepath=off ls-files --cached --others --exclude-standard)
$limit = [int64]$MaxPlainMB * 1MB
$tracked = 0
foreach ($c in $candidates) {
    $full = Join-Path $root ($c -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    if ((Get-Item -LiteralPath $full).Length -le $limit) { continue }
    $filter = ((Git check-attr filter -- $c) | Out-String).Trim()
    if ($filter -notmatch ': filter: lfs$') {
        Git lfs track --filename -- $c | Out-Null
        Info "Large file moved to LFS: $c ($([math]::Round((Get-Item -LiteralPath $full).Length / 1MB)) MB)"
        $tracked++
    }
}
if ($tracked -gt 0) { Info "$tracked large file(s) added to .gitattributes as LFS." }

Git add -A | Out-Null
$staged = @(Git -c core.quotepath=off diff --cached --name-only --diff-filter=AM HEAD)
$lfsFiles = @{}
# Paths go in as arguments, in batches: piping into git's stdin from Windows PowerShell adds a BOM and CRLF.
for ($i = 0; $i -lt $staged.Count; $i += 100) {
    $batch = $staged[$i..([math]::Min($i + 99, $staged.Count - 1))]
    foreach ($a in (Git -c core.quotepath=off check-attr --cached filter -- @batch)) {
        if ($a -match '^(.*): filter: lfs$') { $lfsFiles[$Matches[1]] = $true }
    }
}
$plainBytes = 0; $lfsBytes = 0; $plainCount = 0; $lfsCount = 0; $biggest = @()
foreach ($s in $staged) {
    $full = Join-Path $root ($s -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $len = (Get-Item -LiteralPath $full).Length
    if ($lfsFiles.ContainsKey($s)) { $lfsBytes += $len; $lfsCount++ } else {
        $plainBytes += $len; $plainCount++
        if ($len -gt $limit) { Fail "$s ($([math]::Round($len / 1MB)) MB) would go in as a normal git file. Nothing was committed." }
    }
    $biggest += [pscustomobject]@{ MB = [math]::Round($len / 1MB, 1); Path = $s }
}
Info ("To commit: {0} normal file(s), {1:N0} MB; {2} LFS file(s), {3:N0} MB." -f $plainCount, ($plainBytes / 1MB), $lfsCount, ($lfsBytes / 1MB))
Info 'Largest files:'
$biggest | Sort-Object MB -Descending | Select-Object -First 8 | ForEach-Object { Info ("  {0,8} MB  {1}" -f $_.MB, $_.Path) }
if ($lfsBytes -gt 1GB) { Warn 'More than 1 GB of LFS data. Check the LFS storage quota of the GitHub account before pushing (github.com > Settings > Billing).' }

# ---------------------------------------------------------------- 7. commit and push
Step 'Committing'
if ($staged.Count -eq 0) { Info 'Nothing new to commit: the folder already matches GitHub.' }
else {
    Git commit -q -m $Message | Out-Null
    Info ('Committed: ' + ((Git log -1 --format='%h %s') | Out-String).Trim())
}

if ($Push) {
    Step 'Pushing to GitHub'
    & git.exe -C $root push -u origin $Branch
    if ($LASTEXITCODE -ne 0) { Fail 'Push failed; see the git output above. The commit is safe locally; fix the cause and run again with -PushOnly.' }
    Step 'Done. The project is on GitHub.'
} else {
    Step 'Done, not pushed yet'
    Info 'Check the summary above. To push, run the same command again with -PushOnly instead of the other options.'
}
