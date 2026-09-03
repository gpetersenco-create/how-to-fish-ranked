# Builds the mod, packs BepInEx + the plugin into a zip, and (unless -NoPublish) publishes a GitHub release plus the
# update manifest the in-game auto-updater reads.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 [-NoPublish] [-Notes "what changed"]
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\How to Fish\How to Fish",
    [string]$Repo = "gpetersenco-create/how-to-fish-ranked",
    [string]$Notes = "",
    [switch]$NoPublish
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build "$root\HowToFish1v1.sln" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$version = (Select-String -Path "$root\src\HowToFish1v1\Plugin.cs" -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
$dllDir = "$root\src\HowToFish1v1\bin\Release\netstandard2.1"
$stage = Join-Path $root "dist\stage"
$zip = Join-Path $root "dist\HowToFishRanked-$version.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\core" | Out-Null
New-Item -ItemType Directory -Force "$stage\BepInEx\plugins\HowToFish1v1" | Out-Null

# BepInEx loader (from the installed copy in the game folder)
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") {
    if (Test-Path "$GameDir\$f") { Copy-Item "$GameDir\$f" "$stage\$f" }
}
Copy-Item "$GameDir\BepInEx\core\*" "$stage\BepInEx\core\" -Recurse

# The mod
Copy-Item "$dllDir\HowToFish1v1.dll" "$stage\BepInEx\plugins\HowToFish1v1\"
Copy-Item "$dllDir\HowToFish1v1.Core.dll" "$stage\BepInEx\plugins\HowToFish1v1\"
$soundDir = "$root\src\HowToFish1v1\sounds"
Copy-Item "$soundDir\knife.mp3" "$stage\BepInEx\plugins\HowToFish1v1\"
Copy-Item "$soundDir\hitmarker.mp3" "$stage\BepInEx\plugins\HowToFish1v1\"

@"
HOW TO FISH - RANKED MOD  v$version
=================================

INSTALL (takes one minute)
1. In Steam: right-click How to Fish > Manage > Browse local files.
   Open the inner "How to Fish" folder, the one that contains "How to Fish.exe".
2. Extract EVERYTHING from this zip into that folder, so that "winhttp.dll" sits
   right next to "How to Fish.exe" and there is a "BepInEx" folder beside it.
3. Start the game. A "Ranked" button appears under "Character" on the main menu.

UPDATES
The mod updates itself: when a newer version is out it downloads it at startup and
shows "Mod updated, restart the game" on the main menu. Restart and you are current.

PLAY
- Whoever hosts: Ranked > pick mode and map > Matchmake. Then press Invite Friends in the lobby.
- Everyone else: accept the Steam invite. The lobby screen opens by itself.
- Pick your guns, attachments and skin, press Ready Up. The host presses Start Match.

LEADERBOARD / PRIVACY
The mod reports your Steam id, Steam name and ranked stats (rank points, wins, losses,
kills, deaths) to the mod's online leaderboard so everyone can see the global top 25.
To opt out, set ShareRank = false in BepInEx\config\com.gavin.howtofish1v1.cfg.

NOTES
- Everyone needs the same mod version; the lobby shows "NO MOD / OLD VER" otherwise.
- Your normal saves are never touched while ranked is running.
- Ranks are stored per Steam account in BepInEx\config\HowToFish1v1.ranks.json and survive updates.
- To uninstall: delete winhttp.dll, doorstop_config.ini and the BepInEx folder.
"@ | Set-Content -Path "$stage\INSTALL.txt" -Encoding utf8

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host "Release written to $zip"

if ($NoPublish) { exit 0 }

# ---------------------------------------------------------------- publish to GitHub
$tag = "v$version"
if ([string]::IsNullOrWhiteSpace($Notes)) { $Notes = "How to Fish Ranked $version" }
$exists = $false
try { gh release view $tag --repo $Repo 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { $exists = $true } } catch {}
if ($exists) {
    gh release upload $tag $zip "$dllDir\HowToFish1v1.dll" "$dllDir\HowToFish1v1.Core.dll" "$soundDir\knife.mp3" "$soundDir\hitmarker.mp3" --repo $Repo --clobber
} else {
    gh release create $tag $zip "$dllDir\HowToFish1v1.dll" "$dllDir\HowToFish1v1.Core.dll" "$soundDir\knife.mp3" "$soundDir\hitmarker.mp3" --repo $Repo --title "How to Fish Ranked $version" --notes $Notes
}
if ($LASTEXITCODE -ne 0) { throw "gh release failed" }

$base = "https://github.com/$Repo/releases/download/$tag"
$manifest = [ordered]@{
    version = $version
    notes = $Notes
    files = @(
        [ordered]@{ name = "HowToFish1v1.dll"; url = "$base/HowToFish1v1.dll" },
        [ordered]@{ name = "HowToFish1v1.Core.dll"; url = "$base/HowToFish1v1.Core.dll" },
        [ordered]@{ name = "knife.mp3"; url = "$base/knife.mp3" },
        [ordered]@{ name = "hitmarker.mp3"; url = "$base/hitmarker.mp3" }
    )
}
New-Item -ItemType Directory -Force "$root\updates" | Out-Null
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path "$root\updates\manifest.json" -Encoding utf8
git add "$root\updates\manifest.json"
git -c user.name="Gavin" -c user.email="gavin@petersensoftwares.com" commit -q -m "release: $version" 2>$null
git push origin HEAD 2>&1 | Select-Object -Last 1
Write-Host "Published $tag and manifest to $Repo"
