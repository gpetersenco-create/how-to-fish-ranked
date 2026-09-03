# Builds the mod and packs BepInEx + the plugin into one zip that friends extract into the game folder.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\How to Fish\How to Fish"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build "$root\HowToFish1v1.sln" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$version = (Select-String -Path "$root\src\HowToFish1v1\Plugin.cs" -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
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
Copy-Item "$root\src\HowToFish1v1\bin\Release\netstandard2.1\HowToFish1v1.dll" "$stage\BepInEx\plugins\HowToFish1v1\"
Copy-Item "$root\src\HowToFish1v1\bin\Release\netstandard2.1\HowToFish1v1.Core.dll" "$stage\BepInEx\plugins\HowToFish1v1\"

@"
HOW TO FISH - RANKED MOD  v$version
=================================

INSTALL (takes one minute)
1. In Steam: right-click How to Fish > Manage > Browse local files.
   Open the inner "How to Fish" folder, the one that contains "How to Fish.exe".
2. Extract EVERYTHING from this zip into that folder, so that "winhttp.dll" sits
   right next to "How to Fish.exe" and there is a "BepInEx" folder beside it.
3. Start the game. A "Ranked" button appears under "Character" on the main menu.

PLAY
- Whoever hosts: Ranked > pick mode and map > Matchmake. Then press Invite Friends in the lobby.
- Everyone else: accept the Steam invite. The lobby screen opens by itself.
- Pick your guns and attachments, press Ready Up. The host presses Start Match.

NOTES
- Everyone needs this exact mod version ($version); the lobby shows "NO MOD" otherwise.
- Your normal saves are never touched while ranked is running.
- Ranks are stored per Steam account in BepInEx\config\HowToFish1v1.ranks.json.
- To uninstall: delete winhttp.dll, doorstop_config.ini and the BepInEx folder.
"@ | Set-Content -Path "$stage\INSTALL.txt" -Encoding utf8

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host "Release written to $zip"
