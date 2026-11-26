# Regenerate the multi-resolution Windows icon and PNG from the matching geometric design.
Add-Type -AssemblyName System.Drawing
$iconDirectory = Join-Path $PSScriptRoot '../Wideska/Resources/Icons'
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $canvas = New-Object System.Drawing.Bitmap 512,512
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1A1D29'))
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#BDA7FF'))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(16,16,144,144,180,90)
    $path.AddArc(352,16,144,144,270,90)
    $path.AddArc(352,352,144,144,0,90)
    $path.AddArc(16,352,144,144,90,90)
    $path.CloseFigure()
    $graphics.FillPath($background,$path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#BDA7FF')),28
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $calendar = New-Object System.Drawing.Drawing2D.GraphicsPath
    $calendar.AddArc(112,128,48,48,180,90)
    $calendar.AddArc(352,128,48,48,270,90)
    $calendar.AddArc(352,352,48,48,0,90)
    $calendar.AddArc(112,352,48,48,90,90)
    $calendar.CloseFigure()
    $graphics.DrawPath($pen,$calendar)
    $graphics.DrawLine($pen,184,104,184,152)
    $graphics.DrawLine($pen,328,104,328,152)
    $graphics.FillRectangle($accent,126,194,260,20)
    $whitePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),24
    $whitePen.StartCap = 'Round'; $whitePen.EndCap = 'Round'; $whitePen.LineJoin = 'Round'
    $points = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 172,264),(New-Object System.Drawing.PointF 208,336),(New-Object System.Drawing.PointF 256,272),(New-Object System.Drawing.PointF 304,336),(New-Object System.Drawing.PointF 340,264))
    $graphics.DrawLines($whitePen,$points)
    $bitmap = New-Object System.Drawing.Bitmap $size,$size
    $scaled = [System.Drawing.Graphics]::FromImage($bitmap)
    $scaled.InterpolationMode = 'HighQualityBicubic'
    $scaled.DrawImage($canvas,0,0,$size,$size)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($size,$stream.ToArray())
    if ($size -eq 256) { $bitmap.Save((Join-Path $iconDirectory 'Wideska.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $scaled.Dispose(); $bitmap.Dispose(); $whitePen.Dispose(); $calendar.Dispose(); $pen.Dispose(); $path.Dispose(); $accent.Dispose(); $background.Dispose(); $graphics.Dispose(); $canvas.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $iconDirectory 'Wideska.ico'))
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame[0] -eq 256) { 0 } else { $frame[0] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
} finally { $writer.Dispose(); $file.Dispose() }
