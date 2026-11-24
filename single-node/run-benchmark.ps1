param(
    [switch] $Pilot,
    [string] $OutputDirectory,
    [ValidateRange(1, 3)]
    [int[]] $Repetitions = @(1, 2, 3),
    [ValidateSet('Squirix', 'Garnet', 'Valkey', 'Redis')]
    [string[]] $Only = @('Squirix', 'Garnet', 'Valkey', 'Redis')
)

$ErrorActionPreference = 'Stop'
$useRtk = [bool](Get-Command rtk -ErrorAction SilentlyContinue)

function Invoke-BenchDocker {
    if ($useRtk) { rtk proxy docker @args }
    else { docker @args }
}

$aspnetImage = 'mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586'
$sourceRoot = $PSScriptRoot.Replace('\', '/')
$certMount = "type=bind,source=$sourceRoot/certs,target=/certs,readonly"
$loadMount = "type=bind,source=$sourceRoot/bin/Release/net10.0,target=/load,readonly"
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$seconds = 6
$rates = '2000,5000,8000,20000'
$outputRoot = Join-Path $PSScriptRoot 'measurements'
$trialNumbers = $Repetitions
if ($Pilot) {
    $trialNumbers = @(0)
    $seconds = 2
    $rates = '2000,20000'
    $outputRoot = Join-Path $PSScriptRoot 'pilot'
}
if ($OutputDirectory) { $outputRoot = $OutputDirectory }
[void][IO.Directory]::CreateDirectory($outputRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
$images = @{
    Squirix = $aspnetImage
    Garnet = 'ghcr.io/microsoft/garnet@sha256:a5b11ca8292400a6239437c34c96aaa515a55ab3613ef2e41a33914405f89442'
    Valkey = 'valkey/valkey@sha256:1c2b25b09f354c66fe994733acdf12fcfd4bdf6f3bd92b88a93a70378d884056'
    Redis = 'redis@sha256:1f88291b4c57bdf249f02eb085a8669889b2d2954e91d4de5118a5da6b78dd53'
}
$orders = @(
    @('Squirix', 'Garnet', 'Valkey', 'Redis'),
    @('Garnet', 'Redis', 'Squirix', 'Valkey'),
    @('Redis', 'Valkey', 'Garnet', 'Squirix')
)

foreach ($repetition in $trialNumbers) {
    $order = $orders[[Math]::Max(0, $repetition - 1)]
    foreach ($system in $order) {
        if ($system -notin $Only) { continue }
        $output = Join-Path $outputRoot "$system-$repetition.jsonl"
        if (Test-Path -LiteralPath $output) {
            Write-Host "Keeping completed $system repetition $repetition"
            continue
        }
        $partial = "$output.$runId.partial"

        $containerName = "squirix-bench-$runId-$($system.ToLowerInvariant())-$repetition"
        $server = @('run', '--detach', '--rm', '--name', $containerName, '--cpuset-cpus', '12-15',
            '--memory', '4g', '--mount', $certMount)
        switch ($system) {
            'Squirix' {
                $server += @('--mount', "type=bind,source=$sourceRoot/server-package/tools/net10.0/any,target=/server,readonly",
                    '--env', 'SQUIRIX_TLS_CERTIFICATE_PATH=/certs/server.pfx',
                    '--env', 'SQUIRIX_TLS_CERTIFICATE_PASSWORD=benchmark-cert',
                    '--env', 'Logging__LogLevel__Default=Warning',
                    $aspnetImage, 'dotnet', '/server/Squirix.Server.Host.dll',
                    'run', '--urls', 'https://127.0.0.1:6380')
            }
            'Garnet' {
                $server += @('--mount', "type=bind,source=$sourceRoot/garnet.conf,target=/bench.conf,readonly",
                    $images[$system], '--config-import-path', '/bench.conf', '--bind', '127.0.0.1', '--port', '6380', '--tls',
                    '--cert-file-name', '/certs/server.pfx', '--cert-password', 'benchmark-cert',
                    '--memory', '512m', '--index', '16m', '--no-obj')
            }
            { $_ -in @('Redis', 'Valkey') } {
                $binary = $system.ToLowerInvariant() + '-server'
                $server += @('--mount', "type=bind,source=$sourceRoot/redis-family.conf,target=/bench.conf,readonly",
                    $images[$system], $binary, '/bench.conf')
            }
        }

        Write-Host "Starting $system repetition $repetition"
        Invoke-BenchDocker @server
        if ($LASTEXITCODE -ne 0) { throw "Could not start $system" }

        try {
            Start-Sleep -Seconds 2
            $client = @('run', '--rm', '--name', "$containerName-load", '--network', "container:$containerName",
                '--cpuset-cpus', '16-23', '--memory', '3g', '--mount', $certMount, '--mount', $loadMount,
                '--env', 'SSL_CERT_FILE=/certs/ca.crt', $aspnetImage, 'dotnet', '/load/CacheLoad.dll',
                $system, $repetition, $seconds, $rates)
            Invoke-BenchDocker @client | ForEach-Object {
                if ($_ -like '{*') { [IO.File]::AppendAllText($partial, $_ + "`n", $utf8) }
                Write-Output $_
            }
            if ($LASTEXITCODE -ne 0) { throw "Load generation failed for $system repetition $repetition" }
            if (!(Test-Path -LiteralPath $partial) -or
                @(Get-Content -LiteralPath $partial).Count -ne $rates.Split(',').Length) {
                throw "Incomplete measurements for $system repetition $repetition"
            }
            [IO.File]::Move($partial, $output)
        }
        catch {
            Invoke-BenchDocker logs --tail 35 $containerName
            throw
        }
        finally {
            Invoke-BenchDocker stop --time 2 $containerName | Out-Null
        }
    }
}
