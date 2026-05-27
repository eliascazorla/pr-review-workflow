param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\MultiAgent_Orquestacion_MultiAgente.pptx")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

function New-OleColor {
    param(
        [int]$R,
        [int]$G,
        [int]$B
    )

    return [System.Drawing.ColorTranslator]::ToOle([System.Drawing.Color]::FromArgb($R, $G, $B))
}

$colors = [ordered]@{
    Background = New-OleColor 15 23 42
    Panel = New-OleColor 17 24 39
    PanelAlt = New-OleColor 30 41 59
    AccentBlue = New-OleColor 56 189 248
    AccentGreen = New-OleColor 52 211 153
    AccentOrange = New-OleColor 245 158 11
    AccentPurple = New-OleColor 167 139 250
    AccentRed = New-OleColor 248 113 113
    AccentTeal = New-OleColor 45 212 191
    Text = New-OleColor 248 250 252
    Muted = New-OleColor 148 163 184
    SoftText = New-OleColor 226 232 240
    White = New-OleColor 255 255 255
}

$fontTitle = "Aptos Display"
$fontBody = "Aptos"

function Set-SlideBackground {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][int]$Color
    )

    $Slide.FollowMasterBackground = $false
    $Slide.Background.Fill.Solid()
    $Slide.Background.Fill.ForeColor.RGB = $Color
}

function Add-Rectangle {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][int]$Fill,
        [int]$Line = $Fill,
        [switch]$Rounded
    )

    $shapeType = if ($Rounded) { 5 } else { 1 }
    $shape = $Slide.Shapes.AddShape($shapeType, $Left, $Top, $Width, $Height)
    $shape.Fill.Solid()
    $shape.Fill.ForeColor.RGB = $Fill
    $shape.Line.ForeColor.RGB = $Line
}

function Add-TextBox {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][string]$Text,
        [int]$FontSize = 18,
        [int]$Color = 0,
        [switch]$Bold,
        [int]$Align = 1,
        [string]$FontName = $fontBody
    )

    $box = $Slide.Shapes.AddTextbox(1, $Left, $Top, $Width, $Height)
    $box.TextFrame.WordWrap = $true
    $box.TextFrame.MarginLeft = 0
    $box.TextFrame.MarginRight = 0
    $box.TextFrame.MarginTop = 0
    $box.TextFrame.MarginBottom = 0
    $box.TextFrame.VerticalAnchor = 3
    $box.TextFrame.TextRange.Text = $Text
    $box.TextFrame.TextRange.Font.Name = $FontName
    $box.TextFrame.TextRange.Font.Size = $FontSize
    $box.TextFrame.TextRange.Font.Bold = [bool]$Bold
    $box.TextFrame.TextRange.Font.Color.RGB = $Color
    $box.TextFrame.TextRange.ParagraphFormat.Alignment = $Align
}

function Add-Card {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$Body,
        [Parameter(Mandatory = $true)][int]$Accent,
        [switch]$Compact
    )

    $panel = Add-Rectangle -Slide $Slide -Left $Left -Top $Top -Width $Width -Height $Height -Fill $colors.Panel -Line $colors.PanelAlt -Rounded
    $accentBar = Add-Rectangle -Slide $Slide -Left $Left -Top $Top -Width $Width -Height 8 -Fill $Accent -Line $Accent

    $titleSize = if ($Compact) { 20 } else { 22 }
    $bodySize = if ($Compact) { 14 } else { 16 }
    Add-TextBox -Slide $Slide -Left ($Left + 18) -Top ($Top + 20) -Width ($Width - 36) -Height 34 -Text $Title -FontSize $titleSize -Color $colors.White -Bold
    Add-TextBox -Slide $Slide -Left ($Left + 18) -Top ($Top + 58) -Width ($Width - 36) -Height ($Height - 76) -Text $Body -FontSize $bodySize -Color $colors.SoftText
}

function Add-SectionHeader {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][string]$Title,
        [string]$Subtitle = ""
    )

    Add-Rectangle -Slide $Slide -Left 0 -Top 0 -Width 960 -Height 10 -Fill $colors.AccentBlue -Line $colors.AccentBlue | Out-Null
    Add-TextBox -Slide $Slide -Left 36 -Top 20 -Width 230 -Height 24 -Text $Section.ToUpper() -FontSize 11 -Color $colors.AccentBlue -Bold
    Add-TextBox -Slide $Slide -Left 36 -Top 44 -Width 860 -Height 42 -Text $Title -FontSize 28 -Color $colors.White -Bold -FontName $fontTitle
    if ($Subtitle) {
        Add-TextBox -Slide $Slide -Left 36 -Top 82 -Width 880 -Height 28 -Text $Subtitle -FontSize 14 -Color $colors.Muted
    }
}

function Add-Pill {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][int]$Fill
    )

    $pill = $Slide.Shapes.AddShape(5, $Left, $Top, $Width, $Height)
    $pill.Fill.Solid()
    $pill.Fill.ForeColor.RGB = $Fill
    $pill.Line.ForeColor.RGB = $Fill
    $pill.TextFrame.TextRange.Text = $Text
    $pill.TextFrame.TextRange.Font.Name = $fontBody
    $pill.TextFrame.TextRange.Font.Size = 14
    $pill.TextFrame.TextRange.Font.Bold = $true
    $pill.TextFrame.TextRange.Font.Color.RGB = $colors.White
    $pill.TextFrame.TextRange.ParagraphFormat.Alignment = 2
    $pill.TextFrame.VerticalAnchor = 3
}

function Add-FlowBox {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][int]$Fill,
        [int]$FontSize = 16
    )

    $shape = $Slide.Shapes.AddShape(5, $Left, $Top, $Width, $Height)
    $shape.Fill.Solid()
    $shape.Fill.ForeColor.RGB = $Fill
    $shape.Line.ForeColor.RGB = $Fill
    $shape.TextFrame.TextRange.Text = $Text
    $shape.TextFrame.TextRange.Font.Name = $fontBody
    $shape.TextFrame.TextRange.Font.Size = $FontSize
    $shape.TextFrame.TextRange.Font.Bold = $true
    $shape.TextFrame.TextRange.Font.Color.RGB = $colors.White
    $shape.TextFrame.TextRange.ParagraphFormat.Alignment = 2
    $shape.TextFrame.VerticalAnchor = 3
}

function Add-Arrow {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [int]$Fill = $colors.Muted
    )

    $arrow = $Slide.Shapes.AddShape(33, $Left, $Top, $Width, $Height)
    $arrow.Fill.Solid()
    $arrow.Fill.ForeColor.RGB = $Fill
    $arrow.Line.ForeColor.RGB = $Fill
}

function Add-BulletList {
    param(
        [Parameter(Mandatory = $true)]$Slide,
        [Parameter(Mandatory = $true)][double]$Left,
        [Parameter(Mandatory = $true)][double]$Top,
        [Parameter(Mandatory = $true)][double]$Width,
        [Parameter(Mandatory = $true)][double]$Height,
        [Parameter(Mandatory = $true)][string[]]$Items,
        [int]$FontSize = 18,
        [int]$Color = 0
    )

    $text = ($Items | ForEach-Object { "• $_" }) -join "`r`n`r`n"
    Add-TextBox -Slide $Slide -Left $Left -Top $Top -Width $Width -Height $Height -Text $text -FontSize $FontSize -Color $Color
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$ppt = $null
$presentation = $null

try {
    $ppt = New-Object -ComObject PowerPoint.Application
    $ppt.Visible = -1
    $presentation = $ppt.Presentations.Add()
    $presentation.PageSetup.SlideWidth = 960
    $presentation.PageSetup.SlideHeight = 540

    # Slide 1: title
    $slide = $presentation.Slides.Add(1, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-Rectangle -Slide $slide -Left 0 -Top 0 -Width 960 -Height 16 -Fill $colors.AccentBlue -Line $colors.AccentBlue | Out-Null
    Add-Rectangle -Slide $slide -Left 54 -Top 98 -Width 16 -Height 260 -Fill $colors.AccentGreen -Line $colors.AccentGreen | Out-Null
    Add-TextBox -Slide $slide -Left 90 -Top 94 -Width 650 -Height 62 -Text "MultiAgent" -FontSize 36 -Color $colors.White -Bold -FontName $fontTitle
    Add-TextBox -Slide $slide -Left 90 -Top 150 -Width 660 -Height 106 -Text "Orquestación multi-agente para revisar Pull Requests" -FontSize 28 -Color $colors.Text -Bold -FontName $fontTitle
    Add-TextBox -Slide $slide -Left 90 -Top 268 -Width 560 -Height 56 -Text "3 especialistas + 1 supervisor + GitHub Actions" -FontSize 20 -Color $colors.Muted
    Add-TextBox -Slide $slide -Left 90 -Top 320 -Width 560 -Height 88 -Text "La idea: dividir el análisis en dimensiones claras, coordinar agentes especializados y consolidar el resultado en un reporte coherente, accionable y automatizable." -FontSize 18 -Color $colors.SoftText
    Add-Pill -Slide $slide -Left 90 -Top 430 -Width 128 -Height 34 -Text "QUALITY" -Fill $colors.AccentBlue | Out-Null
    Add-Pill -Slide $slide -Left 228 -Top 430 -Width 128 -Height 34 -Text "SECURITY" -Fill $colors.AccentRed | Out-Null
    Add-Pill -Slide $slide -Left 366 -Top 430 -Width 170 -Height 34 -Text "TESTS COVERAGE" -Fill $colors.AccentPurple | Out-Null
    Add-Rectangle -Slide $slide -Left 686 -Top 84 -Width 220 -Height 350 -Fill $colors.Panel -Line $colors.PanelAlt -Rounded | Out-Null
    Add-TextBox -Slide $slide -Left 712 -Top 116 -Width 170 -Height 40 -Text "Stack" -FontSize 20 -Color $colors.White -Bold -FontName $fontTitle
    Add-BulletList -Slide $slide -Left 712 -Top 168 -Width 170 -Height 220 -Items @(
        "Python CLI",
        "OpenAI function calling",
        "GitHub Actions",
        "Inline PR review",
        "Reportes Markdown + JSON"
    ) -FontSize 16 -Color $colors.SoftText

    # Slide 2: problem
    $slide = $presentation.Slides.Add(2, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Problema" -Title "Un solo prompt no alcanza para revisar un PR complejo" -Subtitle "La revisión de PRs tiene dimensiones distintas que conviene separar para obtener feedback útil."
    Add-Card -Slide $slide -Left 40 -Top 142 -Width 420 -Height 310 -Title "¿Qué pasa con un enfoque generalista?" -Body @"
• Tiende a dar feedback superficial.
• Mezcla calidad, seguridad y tests.
• Puede enfocarse en una sola dimensión y olvidar el resto.
• Es difícil de testear y de comparar entre ejecuciones.
"@ -Accent $colors.AccentOrange | Out-Null
    Add-Card -Slide $slide -Left 500 -Top 142 -Width 420 -Height 310 -Title "Objetivo del lab" -Body @"
• Separar responsabilidades.
• Analizar solo la dimensión correcta.
• Coordinar hallazgos sin duplicar trabajo.
• Entregar un reporte consolidado y accionable.
"@ -Accent $colors.AccentGreen | Out-Null

    # Slide 3: architecture
    $slide = $presentation.Slides.Add(3, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Arquitectura" -Title "Flujo completo: del diff al reporte" -Subtitle "El supervisor decide qué agentes invocar y el sistema consolida los resultados."
    Add-FlowBox -Slide $slide -Left 32 -Top 168 -Width 120 -Height 60 -Text "Input" -Fill $colors.AccentBlue | Out-Null
    Add-Arrow -Slide $slide -Left 160 -Top 181 -Width 34 -Height 34 | Out-Null
    Add-FlowBox -Slide $slide -Left 200 -Top 168 -Width 120 -Height 60 -Text "CLI" -Fill $colors.AccentTeal | Out-Null
    Add-Arrow -Slide $slide -Left 328 -Top 181 -Width 34 -Height 34 | Out-Null
    Add-FlowBox -Slide $slide -Left 368 -Top 168 -Width 160 -Height 60 -Text "Supervisor" -Fill $colors.AccentPurple | Out-Null
    Add-Arrow -Slide $slide -Left 536 -Top 181 -Width 34 -Height 34 | Out-Null
    Add-FlowBox -Slide $slide -Left 576 -Top 168 -Width 146 -Height 60 -Text "Reporte" -Fill $colors.AccentGreen | Out-Null
    Add-Arrow -Slide $slide -Left 730 -Top 181 -Width 34 -Height 34 | Out-Null
    Add-FlowBox -Slide $slide -Left 770 -Top 168 -Width 160 -Height 60 -Text "GitHub / PR" -Fill $colors.AccentOrange | Out-Null
    Add-TextBox -Slide $slide -Left 410 -Top 252 -Width 120 -Height 26 -Text "invoca" -FontSize 13 -Color $colors.Muted -Align 2
    Add-FlowBox -Slide $slide -Left 218 -Top 300 -Width 170 -Height 72 -Text "Quality Agent" -Fill $colors.AccentBlue -FontSize 17 | Out-Null
    Add-FlowBox -Slide $slide -Left 395 -Top 300 -Width 170 -Height 72 -Text "Security Agent" -Fill $colors.AccentRed -FontSize 17 | Out-Null
    Add-FlowBox -Slide $slide -Left 572 -Top 300 -Width 210 -Height 72 -Text "Tests Coverage Agent" -Fill $colors.AccentPurple -FontSize 16 | Out-Null
    Add-TextBox -Slide $slide -Left 54 -Top 414 -Width 850 -Height 74 -Text "Cada agente devuelve hallazgos estructurados por línea y sugerencia. El supervisor agrega métricas de coordinación, resuelve conflictos básicos y produce el reporte final en Markdown y JSON." -FontSize 18 -Color $colors.SoftText

    # Slide 4: agents
    $slide = $presentation.Slides.Add(4, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Especialización" -Title "Tres agentes, tres dimensiones claras" -Subtitle "Cada agente tiene un dominio acotado, un prompt propio y un formato de salida estructurado."
    Add-Card -Slide $slide -Left 36 -Top 150 -Width 280 -Height 292 -Title "Quality" -Body @"
• Naming
• Complejidad
• Duplicación
• Readability
• Maintainability
"@ -Accent $colors.AccentBlue | Out-Null
    Add-Card -Slide $slide -Left 340 -Top 150 -Width 280 -Height 292 -Title "Security" -Body @"
• Secrets expuestos
• SQL injection
• Shell peligroso
• Primitivas inseguras
• Dependencias sospechosas
"@ -Accent $colors.AccentRed | Out-Null
    Add-Card -Slide $slide -Left 644 -Top 150 -Width 280 -Height 292 -Title "Tests coverage" -Body @"
• Código nuevo sin tests
• Cobertura débil
• Edge cases no cubiertos
• Priorización de pruebas
"@ -Accent $colors.AccentPurple | Out-Null

    # Slide 5: supervisor
    $slide = $presentation.Slides.Add(5, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Supervisor" -Title "El supervisor es router, coordinador y sintetizador" -Subtitle "No solo agrega resultados: decide a quién invocar y cómo combinar los hallazgos."
    Add-Card -Slide $slide -Left 36 -Top 142 -Width 294 -Height 308 -Title "Roles del supervisor" -Body @"
• Router: decide qué invocar.
• Coordinador: ejecuta agentes.
• Sintetizador: consolida el reporte.
• Árbitro: evita duplicación y contradicciones.
"@ -Accent $colors.AccentTeal | Out-Null
    Add-Card -Slide $slide -Left 350 -Top 142 -Width 286 -Height 308 -Title "Modos de ejecución" -Body @"
• LLM con function calling.
• Heurística como fallback.
• Secuencial vs paralelo.
• Labels como señales de control.
"@ -Accent $colors.AccentOrange | Out-Null
    Add-Card -Slide $slide -Left 656 -Top 142 -Width 248 -Height 308 -Title "Detalle clave" -Body @"
La label
`tests-coverage-review-needed`
se trata como señal de entrada para forzar el agente de cobertura.
"@ -Accent $colors.AccentPurple -Compact | Out-Null

    # Slide 6: GitHub Actions
    $slide = $presentation.Slides.Add(6, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "GitHub Actions" -Title "Cómo se integra en un PR real" -Subtitle "El workflow corre sobre el diff del PR y publica comentarios, etiquetas y artefactos."
    Add-BulletList -Slide $slide -Left 44 -Top 156 -Width 390 -Height 300 -Items @(
        "Se dispara con PR opened / synchronize / ready_for_review / labeled.",
        "Genera el diff entre base y head; no analiza todo el repo.",
        "Lee secrets del repo consumidor.",
        "Publica comentarios inline y, si hay findings, pide cambios.",
        "Agrega labels por dimensión cuando detecta hallazgos.",
        "Puede bloquear el merge si el check es requerido."
    ) -FontSize 18 -Color $colors.SoftText
    Add-Card -Slide $slide -Left 476 -Top 156 -Width 430 -Height 300 -Title "Políticas importantes" -Body @"
• Forks: modo heurístico por seguridad.
• run-comparison: habilita el reporte comparativo.
• parallel-agents: ejecuta agentes en paralelo.
• tokens y runtime quedan en los reportes para comparar ejecuciones.
"@ -Accent $colors.AccentBlue | Out-Null

    # Slide 7: metrics
    $slide = $presentation.Slides.Add(7, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Métricas" -Title "Cómo medimos si la coordinación escala" -Subtitle "La pregunta no es solo si funciona, sino si el supervisor mejora al sumar agentes."
    Add-Card -Slide $slide -Left 36 -Top 148 -Width 280 -Height 282 -Title "Qué medimos" -Body @"
• runtime_ms
• total_tokens
• coverage_score
• findings por dimensión
• agent_execution_mode
"@ -Accent $colors.AccentGreen | Out-Null
    Add-Card -Slide $slide -Left 336 -Top 148 -Width 280 -Height 282 -Title "Comparación" -Body @"
• 2 agentes vs 3 agentes
• secuencial vs paralelo
• token growth
• hallazgos únicos
• redundancia
"@ -Accent $colors.AccentOrange | Out-Null
    Add-Card -Slide $slide -Left 636 -Top 148 -Width 284 -Height 282 -Title "Señal de buena coordinación" -Body @"
• baja duplicación
• buen routing
• tiempo razonable
• cobertura mayor sin ruido
• salida coherente
"@ -Accent $colors.AccentPurple | Out-Null
    Add-TextBox -Slide $slide -Left 54 -Top 448 -Width 840 -Height 42 -Text "El benchmark también compara latencia secuencial vs paralela para ver si la concurrencia realmente aporta valor." -FontSize 16 -Color $colors.Muted

    # Slide 8: takeaways
    $slide = $presentation.Slides.Add(8, 12)
    Set-SlideBackground -Slide $slide -Color $colors.Background
    Add-SectionHeader -Slide $slide -Section "Takeaways" -Title "Qué aprendimos construyendo el sistema" -Subtitle "Estas son las decisiones que conviene explicar en la presentación."
    Add-Card -Slide $slide -Left 36 -Top 148 -Width 410 -Height 292 -Title "Lo que más valor aportó" -Body @"
• Separar dimensiones con especialización real.
• Mantener salidas estructuradas.
• Usar el supervisor como capa de coordinación.
• Medir runtime y tokens para comparar ejecuciones.
"@ -Accent $colors.AccentTeal | Out-Null
    Add-Card -Slide $slide -Left 474 -Top 148 -Width 450 -Height 292 -Title "Lo que vale contar en el cierre" -Body @"
• Labels no son solo decoración: pueden cambiar el routing.
• Debug y reporte limpio deben separarse.
• El supervisor es el componente más delicado.
• El siguiente salto natural es GitHub App / multi-repo.
"@ -Accent $colors.AccentBlue | Out-Null
    Add-TextBox -Slide $slide -Left 54 -Top 466 -Width 840 -Height 34 -Text "Cierre sugerido: 'La especialización de agentes aporta valor cuando cada dimensión tiene señales propias y el supervisor puede coordinar sin mezclar criterios.'" -FontSize 16 -Color $colors.SoftText

    $presentation.SaveAs($OutputPath, 24)
}
finally {
    if ($presentation) {
        $presentation.Close()
    }
    if ($ppt) {
        $ppt.Quit()
    }
}

Write-Host "Presentation created at $OutputPath"
