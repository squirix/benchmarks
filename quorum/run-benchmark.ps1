param(
    [switch] $Pilot,
    [ValidateRange(1, 3)]
    [int[]] $Repetitions = @(1, 2, 3),
    [ValidateSet('Squirix', 'etcd')]
    [string[]] $Only = @('Squirix', 'etcd')
)

$ErrorActionPreference = 'Stop'
$useRtk = [bool](Get-Command rtk -ErrorAction SilentlyContinue)
function Invoke-BenchDocker {
    if ($useRtk) { rtk proxy docker @args }
    else { docker @args }
}

$aspnet = 'mcr.microsoft.com/dotnet/aspnet@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586'
$etcd = 'quay.io/coreos/etcd@sha256:641c8cb2492aedf4c693b20029a56f325f9b18754bb7dc9f6eac22c99786228c'
$root = $PSScriptRoot.Replace('\', '/')
$privateFiles = "$root/.local"
$serverPackage = (Resolve-Path "$root/../single-node/server-package/tools/net10.0/any").Path.Replace('\', '/')
$runId = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$nodes = @('A', 'B', 'C')
$cpu = @{ A = '0-3'; B = '4-7'; C = '8-11' }
$hosts = @('node-a', 'node-b', 'node-c')
$clientUrls = ($hosts | ForEach-Object { "https://${_}:2379" }) -join ','
$initial = ($hosts | ForEach-Object { "$_=https://${_}:2380" }) -join ','
$utf8 = [Text.UTF8Encoding]::new($false)
$trialNumbers = $Repetitions
$seconds = 5
$rates = '200,400,600,1200'
$outputRoot = Join-Path $privateFiles 'measurements'
if ($Pilot) {
    $trialNumbers = @(0)
    $seconds = 2
    $rates = '200,1200'
    $outputRoot = Join-Path $privateFiles 'pilot'
}
[void][IO.Directory]::CreateDirectory($outputRoot)

function Write-Settings($node) {
    $hostName = 'node-' + $node.ToLowerInvariant()
    $peers = foreach ($peer in $nodes) {
        @{ NodeId = $peer; Uri = "https://node-$($peer.ToLowerInvariant()):5000" }
    }
    $settings = @{ Squirix = @{ Cluster = @{
        NodeId = $node; Uri = "https://${hostName}:5000"
        VirtualNodes = 128; ReplicaCount = 3; PersistenceEnabled = $true
        ReplicationEnabled = $true; AutomaticFailoverEnabled = $true; QuorumReadsEnabled = $true
        Peers = @($peers)
    } } }
    $file = "$privateFiles/$node.json"
    [IO.File]::WriteAllText($file, ($settings | ConvertTo-Json -Depth 8), $utf8)
    return $file
}

function Start-Node($system, $node, $name, $network, $volume) {
    $hostName = 'node-' + $node.ToLowerInvariant()
    $command = @('run', '--detach', '--rm', '--name', $name, '--network', $network, '--network-alias', $hostName,
        '--cpuset-cpus', $cpu[$node], '--memory', '4g', '--mount', "type=volume,source=$volume,target=/data",
        '--mount', "type=bind,source=$privateFiles/certs,target=/certs,readonly")
    if ($system -eq 'Squirix') {
        $settings = Write-Settings $node
        $command += @('--mount', "type=bind,source=$settings,target=/cluster.json,readonly",
            '--mount', "type=bind,source=$serverPackage,target=/server,readonly",
            '--env-file', "$privateFiles/squirix.env",
            '--env', "SQUIRIX_MANAGEMENT_URI=https://${hostName}:5002",
            '--env', "SQUIRIX_TLS_CERTIFICATE_PATH=/certs/$node.pfx",
            '--env', 'SQUIRIX_TLS_CERTIFICATE_PASSWORD=benchmark-cert',
            '--env', 'SQUIRIX_CLUSTER_MTLS_CA_PATH=/certs/ca.crt',
            '--env', "SQUIRIX_CLUSTER_MTLS_CERT_PFX_PATH=/certs/$node.pfx",
            '--env', 'SQUIRIX_CLUSTER_MTLS_CERT_PFX_PASSWORD=benchmark-cert',
            '--env', 'SQUIRIX_CLUSTER_MTLS_INTERNAL_PORT=5100',
            '--env', 'Logging__LogLevel__Default=Warning',
            $aspnet, 'dotnet', '/server/Squirix.Server.Host.dll', 'run', '--settings', '/cluster.json',
            '--persist', '--data-dir', '/data')
    } else {
        $command += @('--entrypoint', '/usr/local/bin/etcd', $etcd, "--name=$hostName", '--data-dir=/data/state',
            '--listen-client-urls=https://0.0.0.0:2379', "--advertise-client-urls=https://${hostName}:2379",
            '--listen-peer-urls=https://0.0.0.0:2380', "--initial-advertise-peer-urls=https://${hostName}:2380",
            "--initial-cluster=$initial", '--initial-cluster-state=new', "--initial-cluster-token=$network",
            "--cert-file=/certs/$node.crt", "--key-file=/certs/$node.key",
            "--peer-cert-file=/certs/$node.crt", "--peer-key-file=/certs/$node.key",
            '--peer-trusted-ca-file=/certs/ca.crt', '--peer-client-cert-auth=true', '--log-level=warn')
    }
    Invoke-BenchDocker @command | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not start $system node $node" }
}

function Get-EtcdStatus($container) {
    $flags = @("--endpoints=$clientUrls", '--cacert=/certs/ca.crt', '--dial-timeout=2s', '--command-timeout=4s')
    $status = Invoke-BenchDocker exec $container /usr/local/bin/etcdctl @flags endpoint status -w json 2>$null
    if ($LASTEXITCODE -eq 0) { return ($status -join '') | ConvertFrom-Json }
}

function Wait-EtcdLeader($container, [bool] $mustBeA) {
    for ($attempt = 0; $attempt -lt 14; $attempt++) {
        $status = @(Get-EtcdStatus $container)
        $nodeA = $status | Where-Object Endpoint -eq 'https://node-a:2379' | Select-Object -First 1
        $leader = $status | Where-Object { $_.Status.leader -ne 0 -and $_.Status.header.member_id -eq $_.Status.leader } |
            Select-Object -First 1
        if ($status.Count -eq 3 -and $nodeA -and $leader -and (!$mustBeA -or $leader.Endpoint -eq $nodeA.Endpoint)) {
            $consistent = @($status | Where-Object {
                !$_.Status.errors -and $_.Status.leader -eq $leader.Status.leader -and $_.Status.raftTerm -eq $leader.Status.raftTerm
            })
            if ($consistent.Count -eq 3) { return @{ Leader = $leader; NodeA = $nodeA; Members = $status } }
        }
        Start-Sleep -Seconds 1
    }
    throw 'etcd cluster did not reach the expected leader state'
}

function Save-EtcdEvidence($status, $phase, $repetition, $file) {
    $members = foreach ($member in $status.Members) {
        @{
            Endpoint = $member.Endpoint
            Id = ([UInt64]$member.Status.header.member_id).ToString('x')
            Leader = ([UInt64]$member.Status.leader).ToString('x')
            Term = $member.Status.raftTerm
        }
    }
    $row = @{ System = 'etcd'; Repetition = $repetition; Phase = $phase; Nodes = @($members) }
    [IO.File]::AppendAllText($file, ($row | ConvertTo-Json -Depth 5 -Compress) + "`n", $utf8)
}

foreach ($repetition in $trialNumbers) {
    $systems = if ($repetition % 2 -eq 0) { @('Squirix', 'etcd') } else { @('etcd', 'Squirix') }
    foreach ($system in $systems) {
        if ($system -notin $Only) { continue }
        $output = Join-Path $outputRoot "$($system.ToLowerInvariant())-$repetition.jsonl"
        if (Test-Path -LiteralPath $output) {
            Write-Host "Keeping completed $system repetition $repetition"
            continue
        }
        $partial = "$output.$runId.partial"
        $network = "quorum-bench-$runId-$($system.ToLowerInvariant())-$repetition"
        $started = [Collections.Generic.List[string]]::new()
        $volumes = [Collections.Generic.List[string]]::new()
        Invoke-BenchDocker network create --internal $network | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not create benchmark bridge' }
        try {
            foreach ($node in $nodes) {
                $container = "$network-$($node.ToLowerInvariant())"
                $volume = "$container-data"
                Invoke-BenchDocker volume create $volume | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "Could not create $volume" }
                $volumes.Add($volume)
                Start-Node $system $node $container $network $volume
                $started.Add($container)
            }
            $leaderContainer = $started[0]
            if ($system -eq 'etcd') {
                $status = Wait-EtcdLeader $leaderContainer $false
                if ($status.Leader.Endpoint -ne $status.NodeA.Endpoint) {
                    $member = ([UInt64]$status.NodeA.Status.header.member_id).ToString('x')
                    $flags = @("--endpoints=$($status.Leader.Endpoint)", '--cacert=/certs/ca.crt')
                    Invoke-BenchDocker exec $leaderContainer /usr/local/bin/etcdctl @flags move-leader $member | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw 'Could not move etcd leadership to node A' }
                }
                $before = Wait-EtcdLeader $leaderContainer $true
                Save-EtcdEvidence $before 'before' $repetition $partial
                $endpoint = 'https://localhost:2379'
            } else {
                $endpoint = 'https://localhost:5000'
            }
            Write-Host "Measuring $system repetition $repetition"
            $load = @('run', '--rm', '--name', "$network-load", '--network', "container:$leaderContainer",
                '--cpuset-cpus', '16-23', '--memory', '3g',
                '--mount', "type=bind,source=$root/bin/Release/net10.0,target=/load,readonly",
                '--mount', "type=bind,source=$privateFiles/certs/ca.crt,target=/ca.crt,readonly",
                '--mount', "type=bind,source=$privateFiles/token.txt,target=/credentials/token.txt,readonly",
                '--env', 'SSL_CERT_FILE=/ca.crt', $aspnet, 'dotnet', '/load/CacheQuorum.dll',
                $system, $endpoint, $repetition, $seconds, $rates)
            Invoke-BenchDocker @load | ForEach-Object {
                if ($_ -like '{*') { [IO.File]::AppendAllText($partial, $_ + "`n", $utf8) }
                Write-Output $_
            }
            if ($LASTEXITCODE -ne 0) { throw "Load generation failed for $system" }
            if ($system -eq 'etcd') {
                $after = Wait-EtcdLeader $leaderContainer $true
                if ($after.Leader.Status.raftTerm -ne $before.Leader.Status.raftTerm) {
                    throw 'etcd changed Raft term during the trial'
                }
                Save-EtcdEvidence $after 'after' $repetition $partial
            }
            if (!(Test-Path -LiteralPath $partial) -or
                @(Get-Content -LiteralPath $partial).Count -ne ($rates.Split(',').Length * 2 + 2)) {
                throw "Incomplete measurements for $system repetition $repetition"
            }
            [IO.File]::Move($partial, $output)
        } catch {
            foreach ($container in $started) { Invoke-BenchDocker logs --tail 15 $container }
            throw
        } finally {
            foreach ($container in $started) { Invoke-BenchDocker stop --time 2 $container | Out-Null }
            foreach ($volume in $volumes) { Invoke-BenchDocker volume rm $volume | Out-Null }
            Invoke-BenchDocker network rm $network | Out-Null
        }
    }
}
