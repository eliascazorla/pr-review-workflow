param(
    [Parameter(Mandatory = $true)]
    [Alias("Input")]
    [string]$Path,

    [ValidateRange(1, 100)]
    [int]$Runs = 5,

    [ValidateSet("auto", "llm", "heuristic")]
    [string]$Mode = "heuristic",

    [string]$Model,

    [string]$BaseUrl,

    [string]$Output,

    [string]$JsonOutput
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Invoke-ReviewRun {
    param(
        [switch]$Parallel
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $arguments = @("main.py", "--input", $Path, "--mode", $Mode)
    if ($Parallel) {
        $arguments += "--parallel-agents"
    }
    if ($Model) {
        $arguments += "--model"
        $arguments += $Model
    }
    if ($BaseUrl) {
        $arguments += "--base-url"
        $arguments += $BaseUrl
    }

    & python @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark run failed with exit code $LASTEXITCODE."
    }
    $stopwatch.Stop()

    return [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
}

function Get-Statistics {
    param(
        [double[]]$Values
    )

    $sorted = @($Values | Sort-Object)
    $count = $sorted.Count
    $sum = ($sorted | Measure-Object -Sum).Sum
    $average = if ($count -gt 0) { $sum / $count } else { 0 }
    $median = if ($count -eq 0) {
        0
    }
    elseif ($count % 2 -eq 1) {
        $sorted[([int]($count / 2))]
    }
    else {
        ($sorted[($count / 2) - 1] + $sorted[$count / 2]) / 2
    }

    return [pscustomobject]@{
        Count = $count
        Average = [math]::Round($average, 2)
        Median = [math]::Round($median, 2)
        Min = if ($count -gt 0) { [math]::Round($sorted[0], 2) } else { 0 }
        Max = if ($count -gt 0) { [math]::Round($sorted[-1], 2) } else { 0 }
    }
}

$sequentialRuns = @()
$parallelRuns = @()

for ($i = 1; $i -le $Runs; $i++) {
    Write-Host "Running sequential benchmark $i of $Runs..."
    $sequentialRuns += Invoke-ReviewRun

    Write-Host "Running parallel benchmark $i of $Runs..."
    $parallelRuns += Invoke-ReviewRun -Parallel
}

$sequentialStats = Get-Statistics -Values $sequentialRuns
$parallelStats = Get-Statistics -Values $parallelRuns
$deltaAverage = [math]::Round($parallelStats.Average - $sequentialStats.Average, 2)
$speedupRatio = if ($parallelStats.Average -gt 0) { $sequentialStats.Average / $parallelStats.Average } else { 0 }
$speedupPercent = if ($sequentialStats.Average -gt 0) {
    [math]::Round((($sequentialStats.Average - $parallelStats.Average) / $sequentialStats.Average) * 100, 2)
} else {
    0
}

    $markdown = New-Object System.Collections.Generic.List[string]
    $markdown.Add("# Sequential vs Parallel Benchmark")
    $markdown.Add("")
    $markdown.Add(("Input: {0}" -f $Path))
    $markdown.Add(("Runs per mode: {0}" -f $Runs))
    $markdown.Add(("Analysis mode: {0}" -f $Mode))
    if ($Mode -ne "heuristic") {
        $markdown.Add("Note: LLM mode adds provider latency, so the comparison may be noisier.")
    }
    else {
        $markdown.Add("Note: heuristic mode keeps the benchmark mostly focused on orchestration overhead.")
    }
    $markdown.Add("")
    $markdown.Add("## Summary")
    $markdown.Add("")
    $markdown.Add("| Mode | Average (ms) | Median (ms) | Min (ms) | Max (ms) |")
    $markdown.Add("|---|---:|---:|---:|---:|")
    $markdown.Add("| Sequential | $($sequentialStats.Average) | $($sequentialStats.Median) | $($sequentialStats.Min) | $($sequentialStats.Max) |")
    $markdown.Add("| Parallel | $($parallelStats.Average) | $($parallelStats.Median) | $($parallelStats.Min) | $($parallelStats.Max) |")
    $markdown.Add("")
    $markdown.Add("## Comparison")
    $markdown.Add("")
    $markdown.Add("| Metric | Value |")
    $markdown.Add("|---|---:|")
    $markdown.Add(("| Average delta (parallel - sequential) | {0} ms |" -f $deltaAverage))
    $markdown.Add(("| Speedup ratio (sequential / parallel) | {0}x |" -f ([math]::Round($speedupRatio, 2))))
    $markdown.Add(("| Speedup percentage | {0}% |" -f $speedupPercent))
    $markdown.Add("")
    $markdown.Add("## Raw Runs")
    $markdown.Add("")
    $markdown.Add("| Run | Sequential (ms) | Parallel (ms) | Delta (ms) |")
    $markdown.Add("|---|---:|---:|---:|")

for ($i = 0; $i -lt $Runs; $i++) {
    $delta = [math]::Round($parallelRuns[$i] - $sequentialRuns[$i], 2)
    $markdown.Add("| $($i + 1) | $($sequentialRuns[$i]) | $($parallelRuns[$i]) | $delta |")
}

    $markdown.Add("")
    $markdown.Add("## Interpretation")
    $markdown.Add("")
    if ($deltaAverage -lt 0) {
        $markdown.Add(("Parallel execution was faster on average by {0} ms across {1} run(s)." -f ([math]::Abs($deltaAverage)), $Runs))
    }
    elseif ($deltaAverage -gt 0) {
        $markdown.Add(("Parallel execution was slower on average by {0} ms across {1} run(s)." -f $deltaAverage, $Runs))
    }
    else {
        $markdown.Add("Both modes had the same average runtime in this sample.")
    }

    $markdownText = ($markdown -join "`n").TrimEnd() + "`n"

if ($Output) {
    Set-Content -Path $Output -Value $markdownText -Encoding utf8
} else {
    Write-Host $markdownText
}

if ($JsonOutput) {
    $json = [ordered]@{
        input = $Path
        runs = $Runs
        mode = $Mode
        sequential = [ordered]@{
            runs = $sequentialRuns
            average_ms = $sequentialStats.Average
            median_ms = $sequentialStats.Median
            min_ms = $sequentialStats.Min
            max_ms = $sequentialStats.Max
        }
        parallel = [ordered]@{
            runs = $parallelRuns
            average_ms = $parallelStats.Average
            median_ms = $parallelStats.Median
            min_ms = $parallelStats.Min
            max_ms = $parallelStats.Max
        }
        comparison = [ordered]@{
            average_delta_ms = $deltaAverage
            speedup_ratio = [math]::Round($speedupRatio, 2)
            speedup_percent = $speedupPercent
        }
    }

    $json | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonOutput -Encoding utf8
}
