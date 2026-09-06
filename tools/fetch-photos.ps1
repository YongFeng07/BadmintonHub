# fetch-photos.ps1 — download freely-licensed real-world facility photos from
# Wikimedia Commons for the SportHub demo seed. Re-runnable: skips files that
# already exist, writes docs/PHOTO_CREDITS.md with author/license/source for
# every committed image (Commons attribution requirement).
#
# The target list is PINNED to hand-reviewed Commons files (real photographs of
# the right sport, free license) so re-runs stay deterministic.
#
# Usage:  powershell -File tools\fetch-photos.ps1
param(
    [string]$OutDir = "SportHub\wwwroot\images\courts",
    [string]$CreditsPath = "docs\PHOTO_CREDITS.md",
    [int]$Width = 900
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Api = "https://commons.wikimedia.org/w/api.php"
$Headers = @{ "User-Agent" = "SportHub-Demo/1.0 (AMIT2014 assignment demo; static seeding)" }

# target file -> exact Commons page title (hand-reviewed: right sport, real
# photograph, free license, checked on the file's description page)
$Pins = [ordered]@{
    "badminton-01" = "File:Badminton Court.jpg"
    "badminton-02" = "File:Badminton Court at IITD.jpg"
    "badminton-03" = "File:Badminton court view.jpg"
    "badminton-04" = "File:Kai Chuen Court Badminton Court.jpg"
    "badminton-05" = "File:Jiangmen NO.1 Middle School-Gymnasium.jpg"
    "badminton-06" = "File:2011 HKOpen 3.jpg"
    "badminton-hall" = "File:Hougang Sports Hall .jpg"
    "pool-unit"     = "File:10-lane, 50M Pool.jpg"
    "pool-facility" = "File:Hallenbad Embrach.jpg"
    "pool-view"     = "File:Basingstoke Sports Centre Pool.jpg"
    "gym-unit"      = "File:Chuze Fitness Interior.jpg"
    "gym-facility"  = "File:Crunch Interior 2023.jpg"
    "gym-view"      = "File:Sport star fitness center; Dnipro, Ukraine; 29.08.19.jpg"
    "squash-unit"     = "File:Ap Lei Chau Sports Centre Level 2 Squash Court 201612.jpg"
    "squash-facility" = "File:La salle de squash.jpg"
    "squash-view"     = "File:Glass Squash Court.JPG"
    "table-unit"      = "File:Table Tennis room at Manor Resort.jpg"
    "table-facility"  = "File:Hirano table tennis center.jpg"
    "table-view"      = "File:Table-tennis-match-karlskrona-idrottshall.jpg"
    "futsal-unit"     = "File:ACS(BR) Futsal Court.JPG"
    "futsal-facility" = "File:Calcio punizione C5.jpg"
    "futsal-view"     = "File:Futsal Ground Miyashita Park Tokyo.jpg"
}

$OutDirFull = Join-Path (Get-Location) $OutDir
if (-not (Test-Path $OutDirFull)) { New-Item -ItemType Directory -Path $OutDirFull -Force | Out-Null }

function Get-PinnedInfo([string]$Title) {
    $uri = $Api + "?action=query&format=json&prop=imageinfo&iiprop=url|extmetadata&iiurlwidth=$Width&titles=" + [Uri]::EscapeDataString($Title)
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            $resp = Invoke-RestMethod -Uri $uri -Headers $Headers -TimeoutSec 60
            $page = $resp.query.pages.PSObject.Properties.Value | Select-Object -First 1
            if (-not $page -or -not $page.imageinfo) { return $null }
            $info = $page.imageinfo[0]
            $meta = $info.extmetadata
            $license = if ($meta.LicenseShortName.Value) { $meta.LicenseShortName.Value } else { "unknown" }
            $artist = if ($meta.Artist.Value) { ($meta.Artist.Value -replace "<[^>]+>", "") -replace "\s+", " " } else { "" }
            return [pscustomobject]@{
                Title   = $Title
                Thumb   = $info.thumburl
                Width   = $info.thumbwidth
                License = $license
                Artist  = $artist
                PageUrl = $info.descriptionurl
            }
        } catch {
            Write-Host ("  retry {0}/4 for {1} ({2})" -f $attempt, $Title, $_.Exception.Message)
            Start-Sleep -Seconds (8 * $attempt)
        }
    }
    return $null
}

function Get-JpgBytes([string]$Url) {
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            $tmp = [System.IO.Path]::GetTempFileName()
            Invoke-WebRequest -Uri $Url -Headers $Headers -OutFile $tmp -TimeoutSec 90 | Out-Null
            $bytes = [System.IO.File]::ReadAllBytes($tmp)
            Remove-Item $tmp -Force -ErrorAction SilentlyContinue
            if ($bytes.Length -gt 20000) { return $bytes }
            return $null
        } catch {
            Write-Host ("  download retry {0}/4 for {1}" -f $attempt, $Url)
            Start-Sleep -Seconds (8 * $attempt)
        }
    }
    return $null
}

function Test-Jpeg([byte[]]$Bytes) {
    return $Bytes.Length -gt 20000 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xD8
}

$Credits = @()
foreach ($entry in $Pins.GetEnumerator()) {
    $target = $entry.Key
    $dest = Join-Path $OutDirFull "$target.jpg"
    $exists = Test-Path $dest

    $info = Get-PinnedInfo $entry.Value
    if (-not $info) { Write-Warning ("no image info for {0} ({1}) - skipped" -f $target, $entry.Value); continue }

    if (-not $exists) {
        $bytes = Get-JpgBytes $info.Thumb
        if (-not $bytes -or -not (Test-Jpeg $bytes)) {
            # upload.wikimedia.org can rate-limit (HTTP 429) per IP. Fall back to
            # commons.wikimedia.org/w/thumb.php, which serves the same Thumbor
            # thumbnails from the wiki host.
            $leaf = [System.IO.Path]::GetFileName($entry.Value)
            $thumbPhp = "https://commons.wikimedia.org/w/thumb.php?f=" + [Uri]::EscapeDataString($leaf) + "&width=$Width"
            Write-Host ("  fallback to thumb.php for {0}" -f $target)
            $bytes = Get-JpgBytes $thumbPhp
        }
        if (-not $bytes -or -not (Test-Jpeg $bytes)) {
            Write-Warning ("download failed or not a JPEG for {0} - skipped" -f $target)
            continue
        }
        [System.IO.File]::WriteAllBytes($dest, $bytes)
        Write-Host ("GOT   {0}.jpg ({1} bytes, {2}px, {3})" -f $target, (Get-Item $dest).Length, $info.Width, $info.License)
    } else {
        Write-Host ("SKIP  {0}.jpg (already exists, {1})" -f $target, $info.License)
    }

    # Credit every pin whose file exists, so the credits file is self-healing.
    $Credits += [pscustomobject]@{
        File = "$target.jpg"
        Title = $info.Title
        License = $info.License
        Artist = $info.Artist
        PageUrl = $info.PageUrl
    }
    Start-Sleep -Seconds 3   # be polite to the Commons API
}

# ---- write credits file (single-quoted strings: no interpolation, no escapes) ----
$lines = @()
$lines += '# Photo credits - real-world demo images'
$lines += ''
$lines += 'All court/facility photos committed under `SportHub/wwwroot/images/courts/` are real-world'
$lines += 'photographs downloaded from **Wikimedia Commons** under free licenses. Seeded demo'
$lines += 'content only - no real business is represented.'
$lines += ''
$lines += '| File | Source | Author | License |'
$lines += '|---|---|---|---|'
foreach ($c in $Credits | Sort-Object File) {
    $src = '[Commons](' + $c.PageUrl + ')'
    $artist = $c.Artist
    if (-not $artist -or $artist -eq 'unknown') { $artist = 'see Commons page' }
    $lines += '| `' + $c.File + '` | ' + $src + ' | ' + $artist + ' | ' + $c.License + ' |'
}
$lines += ''
$lines += 'Regenerate with `powershell -File tools\fetch-photos.ps1` (skips existing files).'
$lines += 'The target list is pinned; if you change a pin, delete the old file first.'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines((Join-Path (Get-Location) $CreditsPath), $lines, $utf8NoBom)
Write-Host ('Credits written to ' + $CreditsPath + ' (' + $Credits.Count + ' images)')
