param(
    [int]$TargetProcessId = 0,
    [string]$PhaseFile = '',
    [int]$DurationSeconds = 900,
    [string]$OutputFile = 'artifacts/resource-stress/gpu-samples.json',
    [string]$StopFile = 'artifacts/resource-stress/stop.flag',
    [string]$FixedPhase = 'production-idle'
)
$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$smi = Join-Path $env:WINDIR 'System32/nvidia-smi.exe'
$logicalProcessors = [Environment]::ProcessorCount
$previousCpu = $null
$previousTime = 0
$previousProcess = 0
$phase = $FixedPhase
$target = $TargetProcessId
$completedAt = $null
$sessionId = (Get-Process -Id $PID).SessionId
$compositorIds = @(Get-Process dwm -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sessionId } | ForEach-Object { $_.Id })
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($StopFile)))
$paths = @('\GPU Engine(*)\Utilization Percentage', '\GPU Process Memory(*)\Dedicated Usage', '\GPU Process Memory(*)\Shared Usage')
function Get-MaxValue($values) {
    $valid = @($values | Where-Object { -not [double]::IsNaN($_) -and -not [double]::IsInfinity($_) -and $_ -ge 0 })
    if ($valid.Count -eq 0) { return 0.0 }
    return [double](($valid | Measure-Object -Maximum).Maximum)
}
try {
    Get-Counter -Counter $paths -SampleInterval 1 -MaxSamples ($DurationSeconds + 5) -ErrorAction SilentlyContinue | ForEach-Object {
        if ($clock.Elapsed.TotalSeconds -ge $DurationSeconds) { throw 'SamplingComplete' }
        if ($PhaseFile -and (Test-Path -LiteralPath $PhaseFile)) {
            try {
                $marker = Get-Content -LiteralPath $PhaseFile -Raw | ConvertFrom-Json
                $target = [int]$marker.ProcessId
                $phase = [string]$marker.Phase
                if (-not $marker.Completed) { $completedAt = $null }
                if ($marker.Completed -and $null -eq $completedAt) { $completedAt = $clock.Elapsed.TotalSeconds }
            } catch { }
        }
        if ($null -ne $completedAt -and $clock.Elapsed.TotalSeconds - $completedAt -gt 5) { throw 'SamplingComplete' }
        if ($target -le 0) { return }
        $sample = $_
        $prefix = "pid_${target}_"
        $own = @($sample.CounterSamples | Where-Object { $_.Status -eq 0 -and $_.InstanceName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
        $engines = @($own | Where-Object { $_.Path -like '*\utilization percentage' })
        $gpu = Get-MaxValue ($engines | ForEach-Object { $_.CookedValue })
        $dwm = Get-MaxValue ($sample.CounterSamples | Where-Object { $_.Path -like '*\utilization percentage' -and $_.Status -eq 0 -and $_.InstanceName -match '^pid_\d+_' } | ForEach-Object { $_.CookedValue })
        $compositor = Get-MaxValue ($sample.CounterSamples | Where-Object {
            ($_.Path -like '*\utilization percentage' -and $_.Status -eq 0 -and $_.InstanceName -match '^pid_(\d+)_' -and $compositorIds -contains [int]$Matches[1])
        } | ForEach-Object { $_.CookedValue })
        $dedicated = Get-MaxValue ($own | Where-Object { $_.Path -like '*\dedicated usage' } | ForEach-Object { $_.CookedValue })
        $shared = Get-MaxValue ($own | Where-Object { $_.Path -like '*\shared usage' } | ForEach-Object { $_.CookedValue })
        $cpuPercent = $null; $workingSet = $null; $private = $null; $handles = $null
        $process = Get-Process -Id $target -ErrorAction SilentlyContinue
        if ($process) {
            $cpu = $process.TotalProcessorTime.TotalSeconds
            if ($previousProcess -eq $target -and $null -ne $previousCpu -and $clock.Elapsed.TotalSeconds -gt $previousTime) {
                $cpuPercent = ($cpu - $previousCpu) / ($clock.Elapsed.TotalSeconds - $previousTime) / $logicalProcessors * 100
            }
            $previousCpu = $cpu; $previousTime = $clock.Elapsed.TotalSeconds; $previousProcess = $target
            $workingSet = $process.WorkingSet64 / 1MB; $private = $process.PrivateMemorySize64 / 1MB; $handles = $process.HandleCount
        }
        $temperature = $null; $power = $null; $deviceGpu = $null; $deviceMemory = $null; $graphicsClock = $null; $pstate = $null; $thermalSlowdown = $null
        if (Test-Path -LiteralPath $smi) {
            $lines = @(& $smi --query-gpu=temperature.gpu,utilization.gpu,power.draw,memory.used,clocks.current.graphics,pstate,clocks_throttle_reasons.hw_thermal_slowdown --format=csv,noheader,nounits 2>$null)
            $line = $lines | Select-Object -First 1
            if ($LASTEXITCODE -eq 0 -and $line) {
                $parts = $line.Split(',') | ForEach-Object { $_.Trim() }
                $temperature = [double]::Parse($parts[0], [Globalization.CultureInfo]::InvariantCulture)
                $deviceGpu = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
                $power = [double]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
                $deviceMemory = [double]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
                $graphicsClock = $parts[4]; $pstate = $parts[5]; $thermalSlowdown = $parts[6]
                if ($temperature -ge 85 -or $thermalSlowdown -eq 'Active') {
                    [IO.File]::WriteAllText([IO.Path]::GetFullPath($StopFile), 'Temperature/thermal-throttling guard stopped the UI test.')
                    throw 'ThermalGuardStopped'
                }
            }
        }
        $records.Add([pscustomobject]@{
            Utc = [DateTimeOffset]::UtcNow.ToString('o'); ElapsedSeconds = $clock.Elapsed.TotalSeconds; Phase = $phase; ProcessId = $target
            ProcessGpuBusiestEnginePercent = $gpu; BusiestIndividualProcessEnginePercent = $dwm
            DwmGpuBusiestEnginePercent = $compositor; ProcessGpuCounterCount = $engines.Count
            ProcessDedicatedGpuMiB = $dedicated / 1MB; ProcessSharedGpuMiB = $shared / 1MB
            CpuPercentMachine = $cpuPercent; WorkingSetMiB = $workingSet; PrivateMiB = $private; Handles = $handles
            GpuTemperatureC = $temperature; DeviceGpuPercent = $deviceGpu; DevicePowerW = $power; DeviceMemoryMiB = $deviceMemory
            GraphicsClockMHz = $graphicsClock; PState = $pstate; HardwareThermalSlowdown = $thermalSlowdown
        })
    }
} catch {
    if ($_.Exception.Message -ne 'SamplingComplete') { throw }
} finally {
    $output = [IO.Path]::GetFullPath($OutputFile)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
    $records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding UTF8
    Write-Output "Recorded $($records.Count) samples: $output"
    $records | Group-Object Phase | ForEach-Object {
        $group = $_.Group
        Write-Output ("{0}: GPU avg={1:N2}% peak={2:N2}%, CPU avg={3:N2}%, RAM peak={4:N1} MiB, GPU temp max={5:N0} C" -f $_.Name,
            ($group.ProcessGpuBusiestEnginePercent | Measure-Object -Average).Average,
            ($group.ProcessGpuBusiestEnginePercent | Measure-Object -Maximum).Maximum,
            ($group.CpuPercentMachine | Measure-Object -Average).Average,
            ($group.WorkingSetMiB | Measure-Object -Maximum).Maximum,
            ($group.GpuTemperatureC | Measure-Object -Maximum).Maximum)
    }
}
