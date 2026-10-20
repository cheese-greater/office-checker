# Converts the Windows XP caption buttons in XP.css (MIT, pixel-traced SVGs) into the PNGs the build embeds.
# Get XP.css from https://unpkg.com/xp.css@0.2.6/dist/XP.css and pass its path.
param(
    [Parameter(Mandatory = $true)]
    [string] $XpCssPath,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Windows PowerShell leaves $PSScriptRoot empty in parameter defaults, so the default is resolved here.
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot 'caption'
}

$css = [System.IO.File]::ReadAllText($XpCssPath)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$states = [ordered]@{ 'normal' = ''; 'hover' = ':hover'; 'pressed' = ':not(:disabled):active' }

foreach ($button in 'Minimize', 'Maximize', 'Restore', 'Close') {
    foreach ($state in $states.Keys) {
        $selector = ".title-bar-controls button[aria-label=$button]$($states[$state]){background-image:url(`"data:image/svg+xml;charset=utf-8,"
        $start = $css.IndexOf($selector)
        if ($start -lt 0) {
            throw "XP.css has no $state image for $button."
        }
        $start += $selector.Length
        $svg = [uri]::UnescapeDataString($css.Substring($start, $css.IndexOf('"', $start) - $start))

        # Each path strokes one color, one pixel wide: M/m move, h/v draw runs. The viewBox starts at y -0.5, so pixel
        # column c covers x c..c+1 and row r covers y r-0.5..r+0.5; a pixel is painted when its center is under the stroke.
        $bitmap = New-Object System.Drawing.Bitmap 21, 21, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        foreach ($path in [regex]::Matches($svg, "<path stroke='([^']+)' d='([^']+)'")) {
            $color = [System.Drawing.ColorTranslator]::FromHtml($path.Groups[1].Value)
            $x = 0.0
            $y = 0.0
            foreach ($command in [regex]::Matches($path.Groups[2].Value, '([A-Za-z])(-?[\d.]+)(?: (-?[\d.]+))?')) {
                $amount = [double]$command.Groups[2].Value
                switch -CaseSensitive ($command.Groups[1].Value) {
                    'M' { $x = $amount; $y = [double]$command.Groups[3].Value }
                    'm' { $x += $amount; $y += [double]$command.Groups[3].Value }
                    'h' {
                        $row = [int][Math]::Ceiling($y - 0.5)
                        for ($column = [int][Math]::Ceiling($x - 0.5); $column -lt [Math]::Ceiling($x + $amount - 0.5); $column++) {
                            $bitmap.SetPixel($column, $row, $color)
                        }
                        $x += $amount
                    }
                    'v' {
                        $column = [int][Math]::Ceiling($x - 1)
                        for ($row = [int][Math]::Ceiling($y); $row -lt [Math]::Ceiling($y + $amount); $row++) {
                            $bitmap.SetPixel($column, $row, $color)
                        }
                        $y += $amount
                    }
                    default { throw "Unexpected path command '$($command.Value)' in the $state $button image." }
                }
            }
        }
        $outputPath = Join-Path $OutputDirectory ('{0}-{1}.png' -f $button.ToLowerInvariant(), $state)
        $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        Write-Host "Wrote $outputPath"
    }
}
