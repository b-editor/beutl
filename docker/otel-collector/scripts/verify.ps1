[CmdletBinding()]
param(
    [ValidateSet('All', 'Config', 'Health', 'Smoke', 'Privacy', 'Golden', 'Dashboards', 'Negative')]
    [string]$Mode = 'All',
    [switch]$NoStart,
    [string]$OtlpHttpEndpoint = 'http://localhost:4318',
    [string]$GrafanaUrl = 'http://localhost:3000'
)

$ErrorActionPreference = 'Stop'
$StackRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $StackRoot 'compose.yml'
$Fixtures = Join-Path $StackRoot 'fixtures'
$Artifacts = Join-Path $StackRoot 'artifacts'
$EnvFile = Join-Path $StackRoot '.env'
$script:RunContext = $null
$script:RunFixturesSent = $false
$script:CanaryBases = @(
    'privacy-installation-canary',
    'privacy-session-canary',
    'privacy-instance-canary',
    'privacy-exception-message-canary',
    'privacy-trace-path-canary',
    'privacy-log-body-canary',
    'privacy-log-path-canary',
    'privacy-email-canary',
    'privacy-metric-path-canary',
    'privacy-product-resource-path-canary',
    'privacy-product-scope-canary',
    'privacy-product-tracestate-canary',
    'privacy-product-path-canary',
    'privacy-trace-scope-canary',
    'privacy-trace-tracestate-canary',
    'privacy-log-scope-canary',
    'privacy-log-severity-canary',
    'privacy-metric-scope-canary',
    'privacy-metric-description-canary',
    'privacy-metric-unit-canary',
    'privacy-renderer-value-canary',
    'privacy-trigger-value-canary',
    'privacy-error-value-canary',
    'privacy-feature-value-canary',
    'privacy-bucket-value-canary',
    'privacy-scope-name-canary',
    'privacy-scope-version-canary',
    'privacy-rejected-scope-attribute-canary',
    'privacy-rejected-tracestate-canary',
    'invalid-product-path-canary'
)

function Get-GrafanaAdminPassword {
    $password = $env:GRAFANA_ADMIN_PASSWORD
    if ([string]::IsNullOrWhiteSpace($password) -and (Test-Path -LiteralPath $EnvFile)) {
        $line = Get-Content -LiteralPath $EnvFile |
            Where-Object { $_ -match '^GRAFANA_ADMIN_PASSWORD=' } |
            Select-Object -Last 1
        if ($null -ne $line) {
            $password = $line.Substring($line.IndexOf('=') + 1).Trim()
        }
    }

    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'GRAFANA_ADMIN_PASSWORD must be set in the environment or an untracked .env file.'
    }

    $forbidden = @('admin', 'password', 'changeme', 'replace-with-a-random-secret')
    if ($password.Length -lt 16 -or $forbidden -contains $password.ToLowerInvariant()) {
        throw 'GRAFANA_ADMIN_PASSWORD must be a non-default secret of at least 16 characters.'
    }

    return $password
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose -f $ComposeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ClickHouse {
    param(
        [Parameter(Mandatory)][string]$Sql,
        [string]$User = 'default'
    )

    $result = $Sql | & docker compose -f $ComposeFile exec -T clickhouse clickhouse-client --user $User --multiquery
    if ($LASTEXITCODE -ne 0) {
        throw 'ClickHouse command failed.'
    }

    return ($result | Out-String).Trim()
}

function Invoke-InternalHttp {
    param(
        [Parameter(Mandatory)][string]$Service,
        [Parameter(Mandatory)][string]$Uri
    )

    $result = & docker compose -f $ComposeFile exec -T $Service wget -qO- $Uri
    if ($LASTEXITCODE -ne 0) {
        throw "Internal HTTP request to $Service failed: $Uri"
    }

    return ($result | Out-String)
}

function New-HexId {
    return [Guid]::NewGuid().ToString('N')
}

function Get-PrometheusScalar {
    param([Parameter(Mandatory)][string]$Query)

    $encoded = [uri]::EscapeDataString($Query)
    $payload = (Invoke-InternalHttp prometheus "http://localhost:9090/api/v1/query?query=$encoded") | ConvertFrom-Json
    $results = @($payload.data.result)
    if ($results.Count -eq 0) {
        return [double]0
    }

    return [double]$results[0].value[1]
}

function Get-LokiTraceResult {
    param(
        [Parameter(Mandatory)][string]$TraceId,
        [Parameter(Mandatory)][int64]$StartUnixNano
    )

    $query = [uri]::EscapeDataString("{service_name=`"beutl.desktop`"} | trace_id=`"$TraceId`"")
    $end = [DateTimeOffset]::UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() * 1000000
    return (Invoke-InternalHttp loki "http://localhost:3100/loki/api/v1/query_range?query=$query&start=$StartUnixNano&end=$end&limit=100") | ConvertFrom-Json
}

function New-RunContext {
    $nonce = (New-HexId).Substring(0, 12)
    [int64]$runStartUnixNano = Invoke-ClickHouse "SELECT toUnixTimestamp64Nano(now64(9, 'UTC'))"

    $context = [PSCustomObject]@{
        Nonce = $nonce
        RunStartUnixNano = $runStartUnixNano
        InstallationIds = @(0..4 | ForEach-Object { New-HexId })
        SessionIds = @(0..4 | ForEach-Object { New-HexId })
        ProductTraceIds = @(0..4 | ForEach-Object { New-HexId })
        ProductStartEventIds = @(0..4 | ForEach-Object { New-HexId })
        ProductFailedTraceIds = @(0..4 | ForEach-Object { New-HexId })
        ProductFailedEventIds = @(0..4 | ForEach-Object { New-HexId })
        ProductFeatureTraceIds = @(0..4 | ForEach-Object { New-HexId })
        BuiltinFeatureEventIds = @(0..4 | ForEach-Object { New-HexId })
        ExtensionFeatureEventIds = @(0..4 | ForEach-Object { New-HexId })
        InvalidTraceId = New-HexId
        InvalidEventId = New-HexId
        DurationPoisonTraceId = New-HexId
        DurationPoisonEventId = New-HexId
        EndPoisonTraceId = New-HexId
        EndPoisonEventId = New-HexId
        DurationPoisonInstallationId = New-HexId
        DurationPoisonSessionId = New-HexId
        ContractTraceIds = @(0..2 | ForEach-Object { New-HexId })
        ContractEventIds = @(0..2 | ForEach-Object { New-HexId })
        DiagnosticTraceId = New-HexId
        DiagnosticSpanId = (New-HexId).Substring(0, 16)
        BoundaryTraceIds = @(0..3 | ForEach-Object { New-HexId })
        BoundaryEventIds = @(0..3 | ForEach-Object { New-HexId })
        BoundaryInstallationId = New-HexId
        BoundarySessionId = New-HexId
        GoldenPrefix = "golden-$nonce-"
        RejectedBaseline = Get-PrometheusScalar 'sum(beutl_beutl_analytics_rejected_spans_total)'
        QualityOperationBaseline = Get-PrometheusScalar 'sum(beutl_beutl_quality_operation_total)'
        QualityUncleanBaseline = Get-PrometheusScalar 'sum(beutl_beutl_quality_unclean_session_total)'
        QualityDurationBaseline = Get-PrometheusScalar 'sum(beutl_beutl_quality_operation_duration_count)'
    }

    $allEventIds = @(
        $context.ProductStartEventIds
        $context.ProductFailedEventIds
        $context.BuiltinFeatureEventIds
        $context.ExtensionFeatureEventIds
        $context.InvalidEventId
        $context.DurationPoisonEventId
        $context.EndPoisonEventId
        $context.ContractEventIds
        $context.BoundaryEventIds
    )
    $quotedIds = ($allEventIds | ForEach-Object { "'$_'" }) -join ','
    $rawBaseline = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId IN ($quotedIds)"
    Assert-Equal $rawBaseline '0' 'Run-unique ClickHouse event baseline was not empty.'

    $null = & docker compose -f $ComposeFile exec -T tempo wget -qO- "http://localhost:3200/api/traces/$($context.DiagnosticTraceId)" 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw 'Run-unique Tempo trace existed before fixture submission.'
    }

    $lokiBaseline = Get-LokiTraceResult -TraceId $context.DiagnosticTraceId -StartUnixNano $runStartUnixNano
    if (@($lokiBaseline.data.result).Count -ne 0) {
        throw 'Run-unique Loki trace metadata existed before fixture submission.'
    }

    return $context
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)][string]$Actual,
        [Parameter(Mandatory)][string]$Expected,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Actual.Trim() -ne $Expected.Trim()) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Text.Contains($Needle, [System.StringComparison]::Ordinal)) {
        throw "$Message Missing '$Needle'."
    }
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Needle,
        [Parameter(Mandatory)][string]$Message
    )

    if ($Text.Contains($Needle, [System.StringComparison]::Ordinal)) {
        throw "$Message Found forbidden value '$Needle'."
    }
}

function Get-OtlpFixturePayload {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$AgeMinutes = 0,
        [System.Collections.IDictionary]$Replacements = @{},
        [switch]$PreserveTimestamps
    )

    # Keep OTLP fixture timestamps inside Tempo and Loki's live ingestion
    # windows regardless of when this verification script is run. Only the
    # test transport data is rewritten; fixtures retain their readable shape.
    $payload = Get-Content -Raw $Path
    if (-not $PreserveTimestamps) {
        [int64]$startUnixNano = [DateTimeOffset]::UtcNow.AddMinutes(-$AgeMinutes).ToUnixTimeMilliseconds() * 1000000
        [int64]$endUnixNano = $startUnixNano + 1000000
        $payload = [regex]::Replace($payload, '"timeUnixNano": "\d+"', ('"timeUnixNano": "{0}"' -f $startUnixNano))
        $payload = [regex]::Replace($payload, '"startTimeUnixNano": "\d+"', ('"startTimeUnixNano": "{0}"' -f $startUnixNano))
        $payload = [regex]::Replace($payload, '"endTimeUnixNano": "\d+"', ('"endTimeUnixNano": "{0}"' -f $endUnixNano))
    }

    foreach ($entry in $Replacements.GetEnumerator()) {
        $payload = $payload.Replace([string]$entry.Key, [string]$entry.Value)
    }
    if ($null -ne $script:RunContext) {
        foreach ($base in $script:CanaryBases) {
            $payload = $payload.Replace($base, "$base-$($script:RunContext.Nonce)")
        }
    }
    return $payload
}

function Test-StaticAssets {
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $PSCommandPath,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "PowerShell verifier has parse errors: $($parseErrors.Message -join '; ')"
    }

    $jsonFiles = @(
        Get-ChildItem -LiteralPath $Fixtures -Filter '*.json' -File
        Get-ChildItem -LiteralPath (Join-Path $StackRoot 'grafana/dashboards') -Filter '*.json' -File
    )
    foreach ($jsonFile in $jsonFiles) {
        try {
            Get-Content -Raw -LiteralPath $jsonFile.FullName | ConvertFrom-Json | Out-Null
        }
        catch {
            throw "Invalid JSON asset '$($jsonFile.FullName)': $($_.Exception.Message)"
        }
    }

    $composeText = Get-Content -Raw -LiteralPath $ComposeFile
    if ($composeText -match '(?m)^\s*(image|FROM):\s*[^#\r\n]*:latest(?:\s|$)') {
        throw 'Compose or Collector image uses the mutable latest tag.'
    }
    foreach ($binding in @('127.0.0.1:4317:4317', '127.0.0.1:4318:4318', '127.0.0.1:3000:3000')) {
        Assert-Contains $composeText $binding "Compose is missing loopback-only binding $binding."
    }

    $collectorText = Get-Content -Raw -LiteralPath (Join-Path $StackRoot 'otel-collector-config.yaml')
    foreach ($forbiddenComponent in @('debug/', 'file/', 'jaeger/')) {
        Assert-NotContains $collectorText $forbiddenComponent 'A removed Collector component returned.'
    }
    Assert-Contains $collectorText 'send_timestamps: false' 'Prometheus exporter may retain stale source timestamps.'
    Assert-Contains $collectorText 'endpoint: 0.0.0.0:13133' 'Collector live health endpoint is missing.'
    Assert-Contains $collectorText 'ParseJSON(String(bucket_counts))[15]' 'Quality histogram bucket-total validation is missing.'
    Assert-Contains $collectorText '(end_time - start_time) > Duration("24h")' 'Product span duration boundary is missing.'
    Assert-NotContains $collectorText 'storage:' 'Collector queue unexpectedly gained persistent storage.'

    $rawMigrationText = Get-Content -Raw -LiteralPath (Join-Path $StackRoot 'clickhouse/migrations/001-product-spans.sql')
    Assert-Contains $rawMigrationText 'TTL toDateTime(IngestedAt) + INTERVAL 90 DAY DELETE' 'Raw TTL truncates to a date or trusts the client event clock.'
    $rollupMigrationText = Get-Content -Raw -LiteralPath (Join-Path $StackRoot 'clickhouse/migrations/002-rollups-and-views.sql')
    Assert-Contains $rollupMigrationText 'MetricFamily LowCardinality(String)' 'Daily and monthly snapshots do not have independent publication keys.'
}

function Test-Config {
    [void](Get-GrafanaAdminPassword)
    Test-StaticAssets
    Invoke-Compose config --quiet
    Invoke-Compose run --rm --no-deps --entrypoint /otelcol-contrib otel-collector validate '--config=/etc/otelcol-contrib/config.yaml'
    Write-Host 'Static assets, compose, and collector configuration validation passed.'
}

function Start-Stack {
    if (-not $NoStart) {
        Invoke-Compose up -d --wait --wait-timeout 180
    }
}

function Test-Health {
    $required = @('clickhouse', 'tempo', 'loki', 'otel-collector', 'prometheus', 'analytics-rollup', 'grafana')
    $json = & docker compose -f $ComposeFile ps --format json
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect compose service health.'
    }

    $services = @($json | ConvertFrom-Json)
    foreach ($name in $required) {
        $service = $services | Where-Object Service -eq $name | Select-Object -First 1
        if ($null -eq $service) {
            throw "Required service '$name' is not running."
        }

        if ($service.State -ne 'running') {
            throw "Service '$name' is '$($service.State)', not running."
        }

        if ($service.Health -ne 'healthy') {
            throw "Service '$name' health is '$($service.Health)', not healthy."
        }
    }

    # Docker Compose reports container-only exposed ports with PublishedPort 0.
    # Restrict this check to actual host bindings so internal backend ports do
    # not accidentally become public while retaining the intended OTLP/Grafana
    # ingress surface.
    $publishedPorts = @(
        $services |
            ForEach-Object { $_.Publishers } |
            Where-Object { $_ -and [int]$_.PublishedPort -gt 0 } |
            ForEach-Object { '{0}:{1}:{2}' -f $_.URL, $_.PublishedPort, $_.TargetPort } |
            Sort-Object -Unique
    )
    $expectedPorts = @('127.0.0.1:3000:3000', '127.0.0.1:4317:4317', '127.0.0.1:4318:4318')
    $portDifference = @(Compare-Object -ReferenceObject $expectedPorts -DifferenceObject $publishedPorts)
    if ($portDifference.Count -gt 0) {
        $actual = if ($publishedPorts.Count -gt 0) { $publishedPorts -join ', ' } else { '(none)' }
        throw "Unexpected host port bindings. Expected '$($expectedPorts -join ', ')'; got '$actual'."
    }

    $grafanaHealth = Invoke-RestMethod -Uri "$GrafanaUrl/api/health" -Method Get
    if ($grafanaHealth.database -ne 'ok') {
        throw 'Grafana API did not report an OK database.'
    }

    Invoke-Compose exec -T otel-collector /busybox wget --spider --quiet 'http://127.0.0.1:13133/'
    Invoke-Compose exec -T otel-collector /otelcol-contrib validate '--config=/etc/otelcol-contrib/config.yaml'
    Write-Host 'All compose services, loopback port allowlist, live Collector endpoint, and Grafana health checks passed.'
}

function Send-OtlpFixture {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('traces', 'logs', 'metrics')][string]$Signal,
        [System.Collections.IDictionary]$Replacements = @{},
        [int]$AgeMinutes = 0,
        [switch]$PreserveTimestamps
    )

    $payload = Get-OtlpFixturePayload (Join-Path $Fixtures $Name) -AgeMinutes $AgeMinutes -Replacements $Replacements -PreserveTimestamps:$PreserveTimestamps
    $fixtureEndpoint = "$OtlpHttpEndpoint/v1/$Signal"
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $fixtureEndpoint -Method Post -ContentType 'application/json' -Body $payload | Out-Null
            return
        }
        catch {
            $statusCode = if ($null -ne $_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            if ($attempt -lt 3 -and $statusCode -in @(429, 503)) {
                Start-Sleep -Milliseconds (250 * $attempt)
                continue
            }
            throw "OTLP fixture '$Name' could not reach '$fixtureEndpoint' after $attempt attempt(s). $($_.Exception.Message)"
        }
    }
}

function Send-RunFixtures {
    $context = $script:RunContext
    $firstSeenMonth = [DateTimeOffset]::UtcNow.ToString('yyyy-MM')

    for ($index = 0; $index -lt 5; $index++) {
        $common = [ordered]@{
            'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' = $context.InstallationIds[$index]
            'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' = $context.SessionIds[$index]
            '2026-08' = $firstSeenMonth
        }

        $product = [ordered]@{}
        $product['11111111111111111111111111111111'] = $context.ProductTraceIds[$index]
        $product['1111111111111111'] = $context.ProductTraceIds[$index].Substring(0, 16)
        $product['cccccccccccccccccccccccccccccccc'] = $context.ProductStartEventIds[$index]
        foreach ($entry in $common.GetEnumerator()) { $product[$entry.Key] = $entry.Value }
        Send-OtlpFixture -Name 'product-span.json' -Signal traces -Replacements $product

        $failed = [ordered]@{}
        $failed['12121212121212121212121212121212'] = $context.ProductFailedTraceIds[$index]
        $failed['1212121212121212'] = $context.ProductFailedTraceIds[$index].Substring(0, 16)
        $failed['edededededededededededededededed'] = $context.ProductFailedEventIds[$index]
        foreach ($entry in $common.GetEnumerator()) { $failed[$entry.Key] = $entry.Value }
        Send-OtlpFixture -Name 'product-failed-span.json' -Signal traces -Replacements $failed

        $feature = [ordered]@{}
        $feature['13131313131313131313131313131313'] = $context.ProductFeatureTraceIds[$index]
        $feature['1313131313131313'] = $context.ProductFeatureTraceIds[$index].Substring(0, 16)
        $feature['1414141414141414'] = (New-HexId).Substring(0, 16)
        $feature['b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1'] = $context.BuiltinFeatureEventIds[$index]
        $feature['e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1'] = $context.ExtensionFeatureEventIds[$index]
        foreach ($entry in $common.GetEnumerator()) { $feature[$entry.Key] = $entry.Value }
        Send-OtlpFixture -Name 'product-feature-spans.json' -Signal traces -Replacements $feature
    }

    $invalid = [ordered]@{
        '33333333333333333333333333333333' = $context.InvalidTraceId
        '3333333333333333' = $context.InvalidTraceId.Substring(0, 16)
        'ffffffffffffffffffffffffffffffff' = $context.InvalidEventId
        'dddddddddddddddddddddddddddddddd' = (New-HexId)
        'eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' = (New-HexId)
        '2026-08' = $firstSeenMonth
    }
    Send-OtlpFixture -Name 'invalid-product-span.json' -Signal traces -Replacements $invalid

    $poisonNow = [DateTimeOffset]::UtcNow
    $durationStart = $poisonNow.AddHours(-25)
    $durationEnd = $poisonNow.AddMinutes(-1)
    $futureEndStart = $poisonNow.AddMinutes(4)
    $futureEnd = $poisonNow.AddMinutes(6)
    $durationPoison = [ordered]@{
        '19191919191919191919191919191919' = $context.DurationPoisonTraceId
        '1919191919191919' = $context.DurationPoisonTraceId.Substring(0, 16)
        'f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1' = $context.DurationPoisonEventId
        '20202020202020202020202020202020' = $context.EndPoisonTraceId
        '2020202020202020' = $context.EndPoisonTraceId.Substring(0, 16)
        'f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2' = $context.EndPoisonEventId
        'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' = $context.DurationPoisonInstallationId
        'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' = $context.DurationPoisonSessionId
        '2026-08' = $firstSeenMonth
        '5555555555555555555' = $durationStart.ToUnixTimeMilliseconds() * 1000000
        '6666666666666666666' = $durationEnd.ToUnixTimeMilliseconds() * 1000000
        '7777777777777777777' = $futureEndStart.ToUnixTimeMilliseconds() * 1000000
        '8888888888888888888' = $futureEnd.ToUnixTimeMilliseconds() * 1000000
    }
    Send-OtlpFixture -Name 'invalid-duration-span.json' -Signal traces -Replacements $durationPoison -PreserveTimestamps

    $contract = [ordered]@{
        '44444444444444444444444444444444' = $context.ContractTraceIds[0]
        '4444444444444444' = $context.ContractTraceIds[0].Substring(0, 16)
        '30303030303030303030303030303030' = $context.ContractEventIds[0]
        '55555555555555555555555555555555' = $context.ContractTraceIds[1]
        '5555555555555555' = $context.ContractTraceIds[1].Substring(0, 16)
        '60606060606060606060606060606060' = $context.ContractEventIds[1]
        '66666666666666666666666666666666' = $context.ContractTraceIds[2]
        '6666666666666666' = $context.ContractTraceIds[2].Substring(0, 16)
        '90909090909090909090909090909090' = $context.ContractEventIds[2]
        '2026-08' = $firstSeenMonth
    }
    Send-OtlpFixture -Name 'privacy-contract-canaries.json' -Signal traces -Replacements $contract

    $diagnostic = [ordered]@{
        '22222222222222222222222222222222' = $context.DiagnosticTraceId
        '2222222222222222' = $context.DiagnosticSpanId
    }
    Send-OtlpFixture -Name 'diagnostic-trace.json' -Signal traces -Replacements $diagnostic
    Send-OtlpFixture -Name 'diagnostic-log.json' -Signal logs -Replacements $diagnostic

    # Deliberately older than Prometheus's one-minute lookback. With
    # send_timestamps=false the scrape is current, while both desktop and
    # PackageTools delta streams must increase the shared low-cardinality sums.
    Send-OtlpFixture -Name 'quality-metrics.json' -Signal metrics -AgeMinutes 10

    $now = [DateTimeOffset]::UtcNow
    $boundaryStarts = @(
        $now.AddMinutes(4),
        $now.AddMinutes(6),
        $now.AddDays(-7).AddMinutes(5),
        $now.AddDays(-7).AddMinutes(-5)
    )
    $timestampMarkers = @('1111111111111111111', '2222222222222222222', '3333333333333333333', '4444444444444444444')
    $endMarkers = @('1111111111112111111', '2222222222223222222', '3333333333334333333', '4444444444445444444')
    $boundary = [ordered]@{
        'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' = $context.BoundaryInstallationId
        'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' = $context.BoundarySessionId
        '2026-08' = $firstSeenMonth
    }
    for ($index = 0; $index -lt 4; $index++) {
        $staticTrace = ('{0}' -f (15 + $index)) * 16
        $staticSpan = ('{0}' -f (15 + $index)) * 8
        $boundary[$staticTrace] = $context.BoundaryTraceIds[$index]
        $boundary[$staticSpan] = $context.BoundaryTraceIds[$index].Substring(0, 16)
        $boundary[(@('a2', 'b2', 'c2', 'd2')[$index] * 16)] = $context.BoundaryEventIds[$index]
        [int64]$start = $boundaryStarts[$index].ToUnixTimeMilliseconds() * 1000000
        $boundary[$timestampMarkers[$index]] = $start
        $boundary[$endMarkers[$index]] = $start + 1000000
    }
    Send-OtlpFixture -Name 'time-boundary-spans.json' -Signal traces -Replacements $boundary -PreserveTimestamps

    # Cross one Prometheus scrape, then send a second delta from both sources.
    # This proves cumulative aggregation and gives rate/histogram panels a
    # genuine current-run increase instead of a single unchanged sample.
    Start-Sleep -Seconds 12
    Send-OtlpFixture -Name 'quality-metrics.json' -Signal metrics -AgeMinutes 10

    # Wait through another complete scrape and exporter batch.
    Start-Sleep -Seconds 15
}

function Ensure-RunFixtures {
    if ($null -eq $script:RunContext) {
        $script:RunContext = New-RunContext
    }
    if (-not $script:RunFixturesSent) {
        Send-RunFixtures
        $script:RunFixturesSent = $true
    }
    return $script:RunContext
}

function Test-Smoke {
    $context = Ensure-RunFixtures
    $acceptedIds = @(
        $context.ProductStartEventIds
        $context.ProductFailedEventIds
        $context.BuiltinFeatureEventIds
        $context.ExtensionFeatureEventIds
        $context.BoundaryEventIds[0]
        $context.BoundaryEventIds[2]
    )
    $acceptedSql = ($acceptedIds | ForEach-Object { "'$_'" }) -join ','
    $count = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId IN ($acceptedSql)"
    Assert-Equal $count ([string]$acceptedIds.Count) 'Current-run product spans did not arrive exactly once.'

    $fresh = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId IN ($acceptedSql) AND toUnixTimestamp64Nano(IngestedAt) >= $($context.RunStartUnixNano)"
    Assert-Equal $fresh ([string]$acceptedIds.Count) 'Product assertion was satisfied by rows older than this verification run.'

    $invalidIds = @($context.InvalidEventId, $context.DurationPoisonEventId, $context.EndPoisonEventId) + @($context.ContractEventIds) + @($context.BoundaryEventIds[1], $context.BoundaryEventIds[3])
    $invalidSql = ($invalidIds | ForEach-Object { "'$_'" }) -join ','
    $invalid = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId IN ($invalidSql)"
    Assert-Equal $invalid '0' 'An invalid event/value/scope/time span bypassed the fail-closed contract.'

    $rejectedAfter = Get-PrometheusScalar 'sum(beutl_beutl_analytics_rejected_spans_total)'
    if ($rejectedAfter -le $context.RejectedBaseline) {
        throw "Rejected counter did not increase for the current run. Baseline=$($context.RejectedBaseline), after=$rejectedAfter."
    }

    $qualityAfter = Get-PrometheusScalar 'sum(beutl_beutl_quality_operation_total)'
    $uncleanAfter = Get-PrometheusScalar 'sum(beutl_beutl_quality_unclean_session_total)'
    $durationAfter = Get-PrometheusScalar 'sum(beutl_beutl_quality_operation_duration_count)'
    $qualityDelta = $qualityAfter - $context.QualityOperationBaseline
    $uncleanDelta = $uncleanAfter - $context.QualityUncleanBaseline
    $durationDelta = $durationAfter - $context.QualityDurationBaseline
    if ($qualityDelta -ne 6 -or $uncleanDelta -ne 6 -or $durationDelta -ne 6) {
        throw "Quality value gates did not retain exactly two valid desktop plus PackageTools deltas. operation=$qualityDelta, unclean=$uncleanDelta, duration=$durationDelta."
    }

    foreach ($invalidDuration in @(
        'beutl_beutl_quality_operation_duration_count{beutl_operation="project.open",beutl_outcome="failed"}',
        'beutl_beutl_quality_operation_duration_count{beutl_operation="preview.first_frame",beutl_outcome="failed"}',
        'beutl_beutl_quality_operation_duration_count{beutl_operation="preview.playback_summary",beutl_outcome="failed"}',
        'beutl_beutl_quality_operation_duration_count{beutl_operation="media.export",beutl_outcome="failed"}'
    )) {
        if ((Get-PrometheusScalar $invalidDuration) -ne 0) {
            throw "An invalid duration value or explicit-bucket layout reached Prometheus: $invalidDuration"
        }
    }

    $qualityPayload = (Invoke-InternalHttp prometheus 'http://localhost:9090/api/v1/query?query=beutl_beutl_quality_operation_total') | ConvertFrom-Json
    $sampleTimestamp = [double]$qualityPayload.data.result[0].value[0]
    $nowTimestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    if (($nowTimestamp - $sampleTimestamp) -gt 60) {
        throw 'Prometheus retained the ten-minute-old OTLP source timestamp instead of stamping the current scrape.'
    }

    $tempoPayload = (Invoke-InternalHttp tempo "http://localhost:3200/api/traces/$($context.DiagnosticTraceId)") | ConvertFrom-Json
    $tempoSpans = @($tempoPayload.batches | ForEach-Object { $_.scopeSpans } | ForEach-Object { $_.spans })
    if ($tempoSpans.Count -lt 1) {
        throw 'Current-run diagnostic trace did not arrive in Tempo.'
    }
    foreach ($span in $tempoSpans) {
        if ([int64]$span.startTimeUnixNano -lt $context.RunStartUnixNano) {
            throw 'Tempo assertion was satisfied by a trace older than this verification run.'
        }
    }
    $loki = Get-LokiTraceResult -TraceId $context.DiagnosticTraceId -StartUnixNano $context.RunStartUnixNano
    if (@($loki.data.result).Count -lt 1) {
        throw 'Current-run diagnostic log did not arrive in Loki.'
    }
    foreach ($stream in @($loki.data.result)) {
        if ($stream.stream.trace_id -ne $context.DiagnosticTraceId) {
            throw 'Loki current-run query returned a mismatched trace ID.'
        }
        foreach ($value in @($stream.values)) {
            if ([int64]$value[0] -lt $context.RunStartUnixNano) {
                throw 'Loki assertion was satisfied by a log older than this verification run.'
            }
        }
    }

    Write-Host 'Run-scoped OTLP smoke, time boundaries, counter deltas, and source-timestamp regression checks passed.'
}

function Test-Privacy {
    $context = Ensure-RunFixtures
    $diagnosticCanary = "privacy-installation-canary-$($context.Nonce)"
    $raw = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE ResourceAttributes['beutl.installation.id'] = '$diagnosticCanary'"
    Assert-Equal $raw '0' 'Current-run diagnostic installation ID entered the product raw table.'

    $tempo = Invoke-InternalHttp tempo "http://localhost:3200/api/traces/$($context.DiagnosticTraceId)"
    $lokiPayload = Get-LokiTraceResult -TraceId $context.DiagnosticTraceId -StartUnixNano $context.RunStartUnixNano
    $loki = $lokiPayload | ConvertTo-Json -Depth 30
    # Query every retained series in this isolated verification stack rather
    # than just the one fixture metric. This catches accidental resource label
    # propagation from either the application pipeline or collector internals.
    $prometheusSeries = Invoke-InternalHttp prometheus 'http://localhost:9090/api/v1/query?query=%7B__name__%3D~%22.%2B%22%7D'
    $prometheusMetadata = Invoke-InternalHttp prometheus 'http://localhost:9090/api/v1/metadata'
    $prometheusTarget = & docker compose -f $ComposeFile exec -T otel-collector /busybox wget -qO- 'http://localhost:8889/metrics'
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read the Collector Prometheus exporter for privacy verification.'
    }
    $prometheusTarget = $prometheusTarget | Out-String
    $prometheus = $prometheusSeries + $prometheusMetadata + $prometheusTarget

    # Product spans must never be routed to Tempo, even though both signals
    # enter through the same public OTLP receiver.
    $null = & docker compose -f $ComposeFile exec -T tempo wget -qO- "http://localhost:3200/api/traces/$($context.ProductTraceIds[0])" 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw 'Product journey trace was retained by Tempo.'
    }

    $canaries = @($script:CanaryBases | ForEach-Object { "$_-$($context.Nonce)" })

    foreach ($canary in $canaries) {
        $rawCanary = Invoke-ClickHouse @"
SELECT count()
FROM beutl_analytics.product_spans FINAL
WHERE position(
    concat(
        TraceState, '|', SpanName, '|', ServiceName, '|', ScopeName, '|', ScopeVersion, '|',
        StatusMessage, '|', arrayStringConcat(mapValues(ResourceAttributes), '|'), '|',
        arrayStringConcat(mapValues(SpanAttributes), '|')
    ),
    '$canary'
) > 0
"@
        Assert-Equal $rawCanary '0' 'ClickHouse raw product privacy boundary failed.'
        Assert-NotContains $tempo $canary 'Tempo privacy boundary failed.'
        Assert-NotContains $loki $canary 'Loki privacy boundary failed.'
        Assert-NotContains $prometheus $canary 'Prometheus privacy boundary failed.'
    }

    Assert-NotContains $prometheus 'service_instance_id' 'Prometheus retained service.instance.id as a metric label.'
    Assert-NotContains $prometheus 'service.instance.id' 'Prometheus retained service.instance.id as a metric label.'

    # Installation/session IDs are permitted only in product raw storage. The
    # current run's random IDs must be absent from all non-raw backends.
    foreach ($id in @($context.InstallationIds) + @($context.SessionIds) + @(
        $context.BoundaryInstallationId, $context.BoundarySessionId,
        $context.DurationPoisonInstallationId, $context.DurationPoisonSessionId)) {
        Assert-NotContains $tempo $id 'Tempo retained a product identifier.'
        Assert-NotContains $loki $id 'Loki retained a product identifier.'
        Assert-NotContains $prometheus $id 'Prometheus retained a product identifier.'
    }

    Assert-Contains $loki 'diagnostic' 'Sanitized Loki event was not retained.'
    $productShape = Invoke-ClickHouse "SELECT concat(ScopeName, '|', ScopeVersion, '|', TraceState) FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($context.ProductStartEventIds[0])' AND toUnixTimestamp64Nano(IngestedAt) >= $($context.RunStartUnixNano)"
    Assert-Equal $productShape 'Beutl.ProductAnalytics|v1|' 'Product scope/tracestate was not canonicalized.'
    Write-Host 'Current-run ClickHouse, Tempo, Loki, and Prometheus privacy/cross-routing checks passed.'
}

function Invoke-Rollup {
    param(
        [string]$RunId = (New-HexId),
        [switch]$ExpectFailure
    )

    $rollup = Get-Content -Raw (Join-Path $StackRoot 'clickhouse/recompute.sql')
    $failAfterStage = if ($ExpectFailure.IsPresent) { 1 } else { 0 }
    $output = $rollup | & docker compose -f $ComposeFile exec -T clickhouse clickhouse-client --user beutl_rollup --database beutl_analytics --multiquery --param_run_id $RunId --param_fail_after_stage $failAfterStage 2>&1
    $exitCode = $LASTEXITCODE
    if ($ExpectFailure) {
        if ($exitCode -eq 0) {
            throw 'Intentional rollup failure unexpectedly succeeded.'
        }
    }
    elseif ($exitCode -ne 0) {
        throw "Rollup '$RunId' failed: $($output | Out-String)"
    }
    return $RunId
}

function Test-UpgradeMigrations {
    param([Parameter(Mandatory)]$Context)

    $probeDatabase = "beutl_upgrade_probe_$($Context.Nonce)"
    try {
        Invoke-ClickHouse "DROP DATABASE IF EXISTS $probeDatabase" | Out-Null

        # Recreate the pre-upgrade raw layout, retain one exporter-shaped row,
        # and prove the migration is data-preserving and repeatable.
        $oldRaw = (Get-Content -Raw (Join-Path $StackRoot 'clickhouse/migrations/001-product-spans.sql')).
            Replace('beutl_analytics', $probeDatabase).
            Replace('PARTITION BY toYYYYMM(IngestedAt)', 'PARTITION BY toYYYYMM(Timestamp)').
            Replace('TTL toDateTime(IngestedAt) + INTERVAL 90 DAY DELETE', 'TTL toDateTime(Timestamp) + INTERVAL 90 DAY DELETE')
        Invoke-ClickHouse $oldRaw | Out-Null
        Invoke-ClickHouse @"
INSERT INTO $probeDatabase.product_spans
(
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName,
    SpanKind, ServiceName, ResourceAttributes, ScopeName, ScopeVersion,
    SpanAttributes, Duration, StatusCode, StatusMessage,
    ``Events.Timestamp``, ``Events.Name``, ``Events.Attributes``,
    ``Links.TraceId``, ``Links.SpanId``, ``Links.TraceState``, ``Links.Attributes``
)
SELECT
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName,
    SpanKind, ServiceName, ResourceAttributes, ScopeName, ScopeVersion,
    SpanAttributes, Duration, StatusCode, StatusMessage,
    ``Events.Timestamp``, ``Events.Name``, ``Events.Attributes``,
    ``Links.TraceId``, ``Links.SpanId``, ``Links.TraceState``, ``Links.Attributes``
FROM beutl_analytics.product_spans
LIMIT 1
"@ | Out-Null
        $rawUpgrade = (Get-Content -Raw (Join-Path $StackRoot 'clickhouse/upgrades/003-ingested-at-retention.sql')).Replace('beutl_analytics', $probeDatabase)
        Invoke-ClickHouse $rawUpgrade | Out-Null
        Invoke-ClickHouse $rawUpgrade | Out-Null
        $rawShape = Invoke-ClickHouse "SELECT concat(partition_key, '|', sorting_key) FROM system.tables WHERE database = '$probeDatabase' AND name = 'product_spans'"
        Assert-Equal $rawShape 'toYYYYMM(IngestedAt)|EventId' 'Raw retention upgrade did not produce the expected table key.'
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.product_spans") '1' 'Raw retention upgrade did not preserve the exporter-shaped row.'

        Invoke-ClickHouse "DROP DATABASE $probeDatabase" | Out-Null
        Invoke-ClickHouse @"
CREATE DATABASE $probeDatabase;
CREATE TABLE $probeDatabase.analytics_rollups
(
    DefinitionVersion LowCardinality(String), ComputedAt DateTime64(3), MetricDate Date,
    MetricName LowCardinality(String), Dimension1 LowCardinality(String),
    Dimension2 LowCardinality(String), Dimension3 LowCardinality(String),
    Value Float64, SampleSize UInt64
)
ENGINE = ReplacingMergeTree(ComputedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate, MetricName, Dimension1, Dimension2, Dimension3)
TTL MetricDate + INTERVAL 13 MONTH DELETE;
INSERT INTO $probeDatabase.analytics_rollups
VALUES
    ('v1', now64(3, 'UTC'), today(), 'probe', '', '', '', 1, 5),
    ('v1', now64(3, 'UTC'), today(), 'retention_monthly_cohort', 'D30', '', '', 0.5, 6);
"@ | Out-Null
        $publicationUpgrade = (Get-Content -Raw (Join-Path $StackRoot 'clickhouse/upgrades/004-atomic-rollup-publication.sql')).Replace('beutl_analytics', $probeDatabase)
        Invoke-ClickHouse $publicationUpgrade | Out-Null
        Invoke-ClickHouse $publicationUpgrade | Out-Null
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.published_analytics WHERE RunId = ''") '2' 'Atomic publication upgrade did not preserve both legacy metric families.'
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.analytics_rollup_publications FINAL WHERE MetricFamily IN ('daily', 'monthly')") '2' 'Atomic publication upgrade did not split the legacy daily and monthly keys.'

        # Recreate the immediately preceding date-only atomic schema after the
        # bug has occurred: a newer monthly-only publication hides an older
        # daily run on the same month-start. Upgrade 004 must recover both from
        # non-FINAL publication history and remain repeatable.
        $legacyDailyRun = New-HexId
        $legacyMonthlyRun = New-HexId
        Invoke-ClickHouse "DROP DATABASE $probeDatabase" | Out-Null
        Invoke-ClickHouse @"
CREATE DATABASE $probeDatabase;
CREATE TABLE $probeDatabase.analytics_rollups
(
    DefinitionVersion LowCardinality(String), RunId String, ComputedAt DateTime64(3), MetricDate Date,
    MetricName LowCardinality(String), Dimension1 LowCardinality(String),
    Dimension2 LowCardinality(String), Dimension3 LowCardinality(String),
    Value Float64, SampleSize UInt64
)
ENGINE = ReplacingMergeTree(ComputedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate, MetricName, Dimension1, Dimension2, Dimension3, RunId)
TTL MetricDate + INTERVAL 13 MONTH DELETE;
CREATE TABLE $probeDatabase.analytics_rollup_publications
(
    DefinitionVersion LowCardinality(String), MetricDate Date,
    RunId String, CompletedAt DateTime64(3)
)
ENGINE = ReplacingMergeTree(CompletedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate)
TTL MetricDate + INTERVAL 13 MONTH DELETE;
INSERT INTO $probeDatabase.analytics_rollups VALUES
    ('v1', '$legacyDailyRun', now64(3, 'UTC') - INTERVAL 2 SECOND, today() - INTERVAL 60 DAY, 'daily-before-monthly', '', '', '', 1, 5),
    ('v1', '$legacyMonthlyRun', now64(3, 'UTC') - INTERVAL 1 SECOND, today() - INTERVAL 60 DAY, 'retention_monthly_cohort', 'D30', '', '', 0.5, 6);
INSERT INTO $probeDatabase.analytics_rollup_publications VALUES
    ('v1', today() - INTERVAL 60 DAY, '$legacyDailyRun', now64(3, 'UTC') - INTERVAL 2 SECOND),
    ('v1', today() - INTERVAL 60 DAY, '$legacyMonthlyRun', now64(3, 'UTC') - INTERVAL 1 SECOND),
    ('v1', today() - INTERVAL 59 DAY, '$legacyDailyRun', now64(3, 'UTC') - INTERVAL 2 SECOND);
"@ | Out-Null
        Assert-Equal (Invoke-ClickHouse "SELECT RunId FROM $probeDatabase.analytics_rollup_publications FINAL WHERE MetricDate = today() - INTERVAL 60 DAY") $legacyMonthlyRun 'Affected atomic-upgrade fixture did not reproduce the date-only replacement.'
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.analytics_rollup_publications FINAL WHERE RunId = '$legacyDailyRun'") '1' 'Affected atomic-upgrade fixture lost the successful daily RunId survivor used for recovery.'
        $publicationUpgrade = (Get-Content -Raw (Join-Path $StackRoot 'clickhouse/upgrades/004-atomic-rollup-publication.sql')).Replace('beutl_analytics', $probeDatabase)
        Invoke-ClickHouse $publicationUpgrade | Out-Null
        Invoke-ClickHouse $publicationUpgrade | Out-Null
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.published_analytics WHERE MetricName = 'daily-before-monthly' AND RunId = '$legacyDailyRun'") '1' 'Atomic publication upgrade did not recover the daily run hidden by a newer monthly publication.'
        Assert-Equal (Invoke-ClickHouse "SELECT count() FROM $probeDatabase.published_analytics WHERE MetricName = 'retention_monthly_cohort' AND RunId = '$legacyMonthlyRun'") '1' 'Atomic publication upgrade did not preserve the latest monthly run.'
    }
    finally {
        Invoke-ClickHouse "DROP DATABASE IF EXISTS $probeDatabase" | Out-Null
    }
}

function Test-GoldenDataset {
    $context = Ensure-RunFixtures
    Test-UpgradeMigrations -Context $context
    $prefix = $context.GoldenPrefix
    $golden = (Get-Content -Raw (Join-Path $Fixtures 'golden-dataset.sql')).Replace('golden-', $prefix)

    # The reserved golden-* namespace is synthetic. Remove prior interrupted
    # verifier rows so a retry cannot skew the current run's cohort denominator
    # or satisfy an assertion with persistent fixture state.
    Invoke-ClickHouse "ALTER TABLE beutl_analytics.product_spans DELETE WHERE startsWith(EventId, 'golden-') SETTINGS mutations_sync = 2" | Out-Null
    $baseline = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)duplicate-event'"
    Assert-Equal $baseline '0' 'Run-unique golden baseline was not empty.'
    Invoke-ClickHouse $golden | Out-Null

    $duplicate = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)duplicate-event'"
    Assert-Equal $duplicate '1' 'Event-id deduplication failed.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)late-open'") '1' 'Late-event fixture was not retained.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)single-event'") '1' 'Single-event fixture was not retained.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)huge-duration' AND Duration > 1000000000000000000") '1' 'Huge-duration fixture was not retained.'

    # A month-start can carry both a retained daily snapshot and a newly
    # recomputed monthly cohort. Publishing the monthly family must not hide
    # the daily row, even when it uses a different run ID.
    $boundaryDate = Invoke-ClickHouse "SELECT toString(addMonths(toStartOfMonth(toDate(now('UTC') - INTERVAL 40 DAY)), -1))"
    $dailyBoundaryRun = New-HexId
    $oldMonthlyBoundaryRun = New-HexId
    $newMonthlyBoundaryRun = New-HexId
    Invoke-ClickHouse @"
INSERT INTO beutl_analytics.analytics_rollups VALUES
    ('v1', '$dailyBoundaryRun', now64(3, 'UTC'), toDate('$boundaryDate'), 'publication_boundary_daily_probe', '', '', '', 7, 5),
    ('v1', '$oldMonthlyBoundaryRun', now64(3, 'UTC'), toDate('$boundaryDate'), 'retention_monthly_cohort', 'D30', '', '', 0.25, 8);
INSERT INTO beutl_analytics.analytics_rollup_publications VALUES
    ('v1', toDate('$boundaryDate'), 'daily', '$dailyBoundaryRun', now64(3, 'UTC')),
    ('v1', toDate('$boundaryDate'), 'monthly', '$oldMonthlyBoundaryRun', now64(3, 'UTC'));
"@ | Out-Null
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricDate = toDate('$boundaryDate') AND MetricName IN ('publication_boundary_daily_probe', 'retention_monthly_cohort')") '2' 'Initial independent daily/monthly publication was incomplete.'
    Start-Sleep -Milliseconds 5
    Invoke-ClickHouse @"
INSERT INTO beutl_analytics.analytics_rollups VALUES
    ('v1', '$newMonthlyBoundaryRun', now64(3, 'UTC'), toDate('$boundaryDate'), 'retention_monthly_cohort', 'D30', '', '', 0.75, 8);
INSERT INTO beutl_analytics.analytics_rollup_publications VALUES
    ('v1', toDate('$boundaryDate'), 'monthly', '$newMonthlyBoundaryRun', now64(3, 'UTC'));
"@ | Out-Null
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricDate = toDate('$boundaryDate') AND MetricName = 'publication_boundary_daily_probe' AND RunId = '$dailyBoundaryRun'") '1' 'A monthly publication hid the retained daily month-start snapshot.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricDate = toDate('$boundaryDate') AND MetricName = 'retention_monthly_cohort' AND RunId = '$newMonthlyBoundaryRun'") '1' 'The replacement monthly snapshot was not published.'

    $firstRun = Invoke-Rollup
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricName = 'quality_latency_ms' AND (Value < 0 OR Value > 86400000 OR NOT isFinite(Value))") '0' 'Exporter-native span duration poisoned the bounded semantic latency rollup.'
    $d30Before = Invoke-ClickHouse "SELECT toString(round(Value, 3)) FROM beutl_analytics.published_analytics WHERE DefinitionVersion = 'v1' AND MetricName = 'retention_rate' AND Dimension1 = 'D30' AND MetricDate = toDate(now('UTC') - INTERVAL 37 DAY)"
    Assert-Equal $d30Before '0' 'D30 cohort baseline should be zero before its delayed return event arrives.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricName = 'feature_adoption' AND Dimension1 = 'extension/com.example.editor/editor/old'") '1' 'Initial canonical feature dimension was not aggregated.'

    $funnelDepth = Invoke-ClickHouse @"
SELECT windowFunnel(604800)(
    toDateTime(Timestamp),
    SpanName = 'app.session.start' AND Outcome = 'success',
    (SpanName = 'project.open' OR SpanName = 'project.create') AND Outcome = 'success',
    SpanName = 'asset.add' AND Outcome = 'success',
    SpanName = 'editor.first_edit' AND Outcome = 'success',
    SpanName = 'preview.first_frame' AND Outcome = 'success',
    SpanName = 'project.save' AND Outcome = 'success',
    SpanName = 'media.export' AND Outcome = 'success')
FROM beutl_analytics.product_spans FINAL
WHERE SessionId = '$($prefix)session-funnel'
"@
    Assert-Equal $funnelDepth '7' 'Ordered funnel did not recover after an earlier out-of-order event.'
    $publishedExport = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricName = 'journey_same_session_funnel' AND Dimension1 = 'export' AND MetricDate = toDate(now('UTC') - INTERVAL 3 DAY)"
    if ([int64]$publishedExport -lt 1) { throw 'Ordered export stage was not published in the journey rollup.' }

    # The D30 event occurred seven days ago and arrives now. The second row
    # replaces an event ID with a new feature dimension.
    $lateAndReplacement = @"
INSERT INTO beutl_analytics.product_spans
(
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName, SpanKind,
    ServiceName, ResourceAttributes, ScopeName, ScopeVersion, SpanAttributes,
    Duration, StatusCode, StatusMessage
)
VALUES
(
    now64(9, 'UTC') - INTERVAL 7 DAY, now64(3, 'UTC') + INTERVAL 1 SECOND,
    '$($prefix)trace-8', '$($prefix)span-8', '', '', 'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', '$($prefix)installation-d30', 'beutl.session.id', '$($prefix)session-d30-return', 'beutl.first_seen_month', '2026-07', 'beutl.release.channel', 'golden', 'os.type', 'linux', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', '$($prefix)d30-return', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 1 DAY, now64(3, 'UTC') + INTERVAL 1 SECOND,
    '$($prefix)trace-9', '$($prefix)span-9', '', '', 'extension.load', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', '$($prefix)installation-dimension', 'beutl.session.id', '$($prefix)session-dimension', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', '$($prefix)dimension-event', 'beutl.outcome', 'success', 'beutl.feature.id', 'extension/com.example.editor/editor/new'), 1000000, 'Ok', ''
);
"@
    Invoke-ClickHouse $lateAndReplacement | Out-Null
    $secondRun = Invoke-Rollup

    $d30After = Invoke-ClickHouse "SELECT toString(round(Value, 3)) FROM beutl_analytics.published_analytics WHERE DefinitionVersion = 'v1' AND MetricName = 'retention_rate' AND Dimension1 = 'D30' AND MetricDate = toDate(now('UTC') - INTERVAL 37 DAY)"
    Assert-Equal $d30After '1' 'Forty-day recomputation did not incorporate the seven-day-late D30 event.'
    $monthlyD30 = Invoke-ClickHouse "SELECT toString(round(Value, 3)) FROM beutl_analytics.published_analytics WHERE DefinitionVersion = 'v1' AND MetricName = 'retention_monthly_cohort' AND Dimension1 = 'D30' AND MetricDate = toStartOfMonth(toDate(now('UTC') - INTERVAL 37 DAY))"
    Assert-Equal $monthlyD30 '1' 'The month containing the 40-day boundary did not receive the delayed D30 update.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricName = 'feature_adoption' AND Dimension1 = 'extension/com.example.editor/editor/old'") '0' 'A disappeared dimension remained visible after publication.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE MetricName = 'feature_adoption' AND Dimension1 = 'extension/com.example.editor/editor/new'") '1' 'Replacement feature dimension was not published.'

    # A staged run that fails before publication must leave the previous
    # complete snapshot and publication map unchanged.
    $publishedBefore = Invoke-ClickHouse "SELECT RunId FROM beutl_analytics.analytics_rollup_publications FINAL WHERE DefinitionVersion = 'v1' AND MetricDate = today() AND MetricFamily = 'daily'"
    $failedRun = New-HexId
    [void](Invoke-Rollup -RunId $failedRun -ExpectFailure)
    $publishedAfter = Invoke-ClickHouse "SELECT RunId FROM beutl_analytics.analytics_rollup_publications FINAL WHERE DefinitionVersion = 'v1' AND MetricDate = today() AND MetricFamily = 'daily'"
    Assert-Equal $publishedAfter $publishedBefore 'A failed staging run replaced the visible snapshot.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.analytics_rollup_publications WHERE RunId = '$failedRun'") '0' 'A failed run was marked complete.'

    Start-Sleep -Seconds 2
    $null = & docker compose -f $ComposeFile exec -T -e ROLLUP_MAX_AGE_SECONDS=0 analytics-rollup /bin/sh /opt/beutl/rollup-health.sh 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Rollup health probe accepted an intentionally stale completion.' }
    Invoke-Compose exec -T analytics-rollup /bin/sh /opt/beutl/rollup-health.sh

    # Materialize TTL to prove retention uses the exact server-arrival instant,
    # without truncating to midnight or trusting a forged future event time.
    Invoke-ClickHouse "ALTER TABLE beutl_analytics.product_spans MATERIALIZE TTL SETTINGS mutations_sync = 2" | Out-Null
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)retention-future'") '0' 'Future event time retained an identifier beyond ingest plus 90 days.'
    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.product_spans FINAL WHERE EventId = '$($prefix)retention-inside'") '1' 'Raw retention expired an identifier before its exact ingest plus 90-day boundary.'

    $schema = Invoke-ClickHouse "SHOW CREATE TABLE beutl_analytics.product_spans"
    Assert-Contains $schema 'PARTITION BY toYYYYMM(IngestedAt)' 'Raw partitions are not based on server ingestion time.'
    Assert-Contains $schema 'TTL toDateTime(IngestedAt) + toIntervalDay(90)' 'Raw TTL is not server ingestion time plus 90 days at ClickHouse TTL precision.'
    $aggregateSchema = Invoke-ClickHouse "SHOW CREATE TABLE beutl_analytics.analytics_rollups"
    Assert-Contains $aggregateSchema 'toIntervalMonth(13)' 'Aggregate table does not have the 13-month TTL.'

    Assert-Equal (Invoke-ClickHouse "SELECT count() FROM beutl_analytics.grafana_analytics WHERE MetricName = 'feature_adoption' AND Dimension1 = 'builtin/editor/small'") '0' 'A <5-installation feature cell escaped Grafana suppression.'
    $definition = Invoke-ClickHouse "SELECT count() FROM beutl_analytics.published_analytics WHERE DefinitionVersion = 'v1'"
    if ([int64]$definition -lt 1) { throw 'No published v1 aggregate rows were generated.' }

    $rawRead = & docker compose -f $ComposeFile exec -T clickhouse clickhouse-client --user beutl_grafana --query 'SELECT count() FROM beutl_analytics.product_spans' 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'Grafana database principal can read the raw product table.' }
    $aggregateRead = & docker compose -f $ComposeFile exec -T clickhouse clickhouse-client --user beutl_grafana --query 'SELECT count() FROM beutl_analytics.grafana_analytics'
    if ($LASTEXITCODE -ne 0) { throw "Grafana database principal cannot read the aggregate view: $aggregateRead" }

    Write-Host "Run-scoped golden dedupe, ordered funnel, monthly D30, atomic publication, ingest TTL, suppression, and least-privilege checks passed ($firstRun, $secondRun)."
}

function Invoke-GrafanaDatasourceQuery {
    param(
        [Parameter(Mandatory)][hashtable]$Headers,
        [Parameter(Mandatory)][string]$DatasourceUid,
        [Parameter(Mandatory)]$Model,
        [string]$RefId = 'A'
    )

    $query = @{}
    if ($Model -is [System.Collections.IDictionary]) {
        foreach ($key in $Model.Keys) {
            $query[$key] = $Model[$key]
        }
    }
    else {
        foreach ($property in $Model.PSObject.Properties) {
            $query[$property.Name] = $property.Value
        }
    }

    $query.refId = $RefId
    $query.datasource = @{ uid = $DatasourceUid }
    if (-not $query.ContainsKey('intervalMs')) { $query.intervalMs = 10000 }
    if (-not $query.ContainsKey('maxDataPoints')) { $query.maxDataPoints = 1000 }

    # Prometheus range queries need a fine enough step to sample the two
    # current-run delta changes; a 30-day/1000-point step can legitimately
    # skip a short-lived regression fixture. Other backends keep the broad
    # range needed to exercise dashboard date macros and Tempo's search limit.
    $queryFrom = if ($DatasourceUid -eq 'prometheus') { 'now-15m' } else { 'now-30d' }
    $body = @{
        from = $queryFrom
        to = 'now'
        queries = @($query)
    } | ConvertTo-Json -Depth 20
    $response = Invoke-RestMethod -Uri "$GrafanaUrl/api/ds/query" -Headers $Headers -Method Post -ContentType 'application/json' -Body $body
    $result = $response.results.($RefId)
    if ($null -eq $result -or ($null -ne $result.error -and -not [string]::IsNullOrWhiteSpace($result.error))) {
        throw "Grafana datasource '$DatasourceUid' query '$RefId' failed: $($result.error)"
    }
    if ($null -ne $result.status -and [int]$result.status -ne 200) {
        throw "Grafana datasource '$DatasourceUid' query '$RefId' returned status $($result.status)."
    }

    return $result
}

function Test-GrafanaResultHasData {
    param(
        [Parameter(Mandatory)]$Result,
        [switch]$RequireNonZero
    )

    foreach ($frame in @($Result.frames)) {
        if ($null -eq $frame.data -or $null -eq $frame.data.values) {
            continue
        }

        $fields = @($frame.schema.fields)
        $valueColumns = @($frame.data.values)
        for ($columnIndex = 0; $columnIndex -lt $valueColumns.Count; $columnIndex++) {
            if ($columnIndex -lt $fields.Count -and $fields[$columnIndex].type -eq 'time') {
                continue
            }
            $values = $valueColumns[$columnIndex]
            foreach ($value in @($values)) {
                if ($null -eq $value -or "$value" -eq '' -or "$value" -match '^(NaN|[+-]Inf(?:inity)?)$') {
                    continue
                }
                $number = 0.0
                if ([double]::TryParse("$value", [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
                    if (-not [double]::IsNaN($number) -and -not [double]::IsInfinity($number) -and
                        (-not $RequireNonZero.IsPresent -or [Math]::Abs($number) -gt 0)) {
                        return $true
                    }
                }
                elseif (-not $RequireNonZero.IsPresent) {
                    return $true
                }
            }
        }
    }
    return $false
}

function Test-Dashboards {
    $context = Ensure-RunFixtures
    [void](Invoke-Rollup)
    $password = Get-GrafanaAdminPassword
    $credentialBytes = [System.Text.Encoding]::UTF8.GetBytes("admin:$password")
    $headers = @{ Authorization = "Basic $([Convert]::ToBase64String($credentialBytes))" }
    $datasources = Invoke-RestMethod -Uri "$GrafanaUrl/api/datasources" -Headers $headers -Method Get
    $dashboardSearch = Invoke-RestMethod -Uri "$GrafanaUrl/api/search?type=dash-db" -Headers $headers -Method Get

    foreach ($uid in @('clickhouse', 'prometheus', 'loki', 'tempo')) {
        if (-not ($datasources.uid -contains $uid)) {
            throw "Grafana datasource '$uid' was not provisioned."
        }
    }

    $lokiDatasource = $datasources | Where-Object uid -eq 'loki' | Select-Object -First 1
    $traceField = @($lokiDatasource.jsonData.derivedFields) | Where-Object name -eq 'TraceID' | Select-Object -First 1
    if ($null -eq $traceField -or $traceField.matcherType -ne 'label' -or $traceField.matcherRegex -ne 'trace_id' -or
        $traceField.datasourceUid -ne 'tempo' -or $traceField.url -ne '${__value.raw}' -or $traceField.urlDisplayLabel -ne 'View trace') {
        throw 'Provisioned Loki trace_id structured-metadata link does not target Tempo correctly.'
    }

    $dashboardUids = @('beutl-overview', 'beutl-journey', 'beutl-retention', 'beutl-feature-adoption', 'beutl-quality', 'beutl-pipeline-health', 'beutl-diagnostics')
    foreach ($uid in $dashboardUids) {
        if (-not ($dashboardSearch.uid -contains $uid)) {
            throw "Grafana dashboard '$uid' was not provisioned."
        }
    }

    # Execute every real panel target through Grafana, including Prometheus,
    # Loki, and Tempo. This verifies datasource reachability, macro expansion,
    # query syntax, and the exact provisioned models rather than detached SQL.
    $panelQueries = @()
    $queryCounts = @{}
    foreach ($dashboardUid in $dashboardUids) {
        $provisioned = Invoke-RestMethod -Uri "$GrafanaUrl/api/dashboards/uid/$dashboardUid" -Headers $headers -Method Get
        $dashboard = $provisioned.dashboard
        foreach ($panel in $dashboard.panels) {
            if ($null -eq $panel.datasource -or [string]::IsNullOrWhiteSpace($panel.datasource.uid) -or $null -eq $panel.targets) {
                continue
            }

            $datasourceUid = [string]$panel.datasource.uid
            foreach ($target in $panel.targets) {
                $refId = if ([string]::IsNullOrWhiteSpace($target.refId)) { 'A' } else { [string]$target.refId }
                $targetModel = $target
                if ($datasourceUid -eq 'tempo' -and $target.query -eq '$traceId') {
                    # Dashboard textbox variables are normally expanded by the
                    # frontend. Substitute the known sanitized fixture trace ID
                    # before exercising the same target through /api/ds/query.
                    $targetModel = @{}
                    foreach ($property in $target.PSObject.Properties) {
                        $targetModel[$property.Name] = $property.Value
                    }
                    $targetModel.query = $context.DiagnosticTraceId
                }
                try {
                    $result = Invoke-GrafanaDatasourceQuery -Headers $headers -DatasourceUid $datasourceUid -Model $targetModel -RefId $refId
                }
                catch {
                    throw "Grafana panel query failed for $dashboardUid, panel $($panel.id), datasource $datasourceUid, ref $refId. $($_.Exception.Message)"
                }
                $queryCounts[$datasourceUid] = 1 + [int]$queryCounts[$datasourceUid]
                if ($dashboardUid -eq 'beutl-quality' -and -not (Test-GrafanaResultHasData $result -RequireNonZero)) {
                    throw "Quality dashboard panel $($panel.id) returned no finite non-zero fixture value."
                }
                $panelQueries += [PSCustomObject]@{
                    Dashboard = $dashboard.uid
                    Panel = $panel.id
                    Datasource = $datasourceUid
                    RefId = $refId
                    Status = $result.status
                    Result = $result
                }
            }
        }
    }

    foreach ($uid in @('clickhouse', 'prometheus', 'loki', 'tempo')) {
        if ([int]$queryCounts[$uid] -lt 1) {
            throw "No dashboard panel query was executed for datasource '$uid'."
        }
    }

    # Each backend must also return its known fixture, not merely accept a
    # syntactically valid query with an empty frame.
    $fixtureModels = @{
        clickhouse = @{
            queryType = 'sql'; format = 1
            rawSql = "SELECT sum(Value) AS value FROM beutl_analytics.grafana_analytics WHERE MetricName = 'feature_adoption' AND Dimension1 = 'builtin/editor/trim'"
        }
        prometheus = @{
            expr = 'beutl_beutl_quality_operation_total'; range = $false; instant = $true
        }
        loki = @{
            expr = "{service_name=`"beutl.desktop`"} | trace_id=`"$($context.DiagnosticTraceId)`""
            queryType = 'range'; range = $true; maxLines = 100
        }
        tempo = @{
            query = $context.DiagnosticTraceId
            queryType = 'traceId'; limit = 20; tableType = 'traces'
        }
    }
    $fixtureResults = @()
    foreach ($uid in @('clickhouse', 'prometheus', 'loki', 'tempo')) {
        $result = Invoke-GrafanaDatasourceQuery -Headers $headers -DatasourceUid $uid -Model $fixtureModels[$uid] -RefId 'Fixture'
        if (-not (Test-GrafanaResultHasData $result)) {
            throw "Grafana datasource '$uid' did not return its known fixture."
        }
        $fixtureResults += [PSCustomObject]@{ Datasource = $uid; Result = $result }
    }

    $hostQuery = [uri]::EscapeDataString('sum by (beutl_host) (beutl_beutl_quality_operation_total)')
    $hostPayload = (Invoke-InternalHttp prometheus "http://localhost:9090/api/v1/query?query=$hostQuery") | ConvertFrom-Json
    $hosts = @($hostPayload.data.result | ForEach-Object { $_.metric.beutl_host })
    foreach ($expectedHost in @('beutl.desktop', 'beutl.package-tools')) {
        if ($hosts -notcontains $expectedHost) {
            throw "Quality aggregation did not retain the fixed multi-source host '$expectedHost'."
        }
    }

    $lokiFixture = $fixtureResults | Where-Object Datasource -eq 'loki' | Select-Object -First 1
    $lokiLabels = @($lokiFixture.Result.frames | ForEach-Object { $_.data.values[0] } | ForEach-Object { $_ })
    $linkedTraceIds = @($lokiLabels | ForEach-Object { $_.trace_id } | Where-Object { $_ })
    if ($linkedTraceIds -notcontains $context.DiagnosticTraceId) {
        throw 'Grafana Loki fixture did not expose current trace_id structured metadata for the Tempo link.'
    }
    $tempoFixture = $fixtureResults | Where-Object Datasource -eq 'tempo' | Select-Object -First 1
    if (-not (Test-GrafanaResultHasData $tempoFixture.Result)) {
        throw 'The trace ID produced by the Loki data link did not resolve in Tempo.'
    }

    New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null
    [PSCustomObject]@{
        GeneratedAt = (Get-Date).ToUniversalTime().ToString('o')
        Datasources = $datasources | Select-Object uid, name, type
        Dashboards = $dashboardSearch | Select-Object uid, title, url
        PanelQueries = $panelQueries
        FixtureResults = $fixtureResults
    } | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Artifacts 'dashboard-query-snapshot.json')

    Write-Host 'Grafana health, all-datasource panel queries, and fixture-result snapshots passed.'
}

function Test-BrokenIngestSelfTest {
    New-Item -ItemType Directory -Force -Path $Artifacts | Out-Null
    $log = Join-Path $Artifacts 'broken-ingest-negative.log'
    $pwsh = if ($IsWindows) { Join-Path $PSHOME 'pwsh.exe' } else { Join-Path $PSHOME 'pwsh' }
    & $pwsh -NoProfile -File $PSCommandPath -Mode Smoke -NoStart -OtlpHttpEndpoint 'http://127.0.0.1:1' -GrafanaUrl $GrafanaUrl *> $log
    if ($LASTEXITCODE -eq 0) {
        throw 'Broken-ingest negative self-test falsely reported success.'
    }
    $negativeOutput = Get-Content -Raw $log
    if ($negativeOutput -notmatch '127[.]0[.]0[.]1:1') {
        throw 'Broken-ingest negative self-test failed for an unexpected reason.'
    }
    Write-Host 'Broken-ingest negative self-test correctly failed with a run-unique empty baseline.'
}

if ($Mode -in @('All', 'Config')) {
    Test-Config
}

if ($Mode -ne 'Config') {
    Start-Stack
}

if ($Mode -in @('All', 'Health')) {
    Test-Health
}

if ($Mode -in @('All', 'Smoke')) {
    Test-Smoke
}

if ($Mode -in @('All', 'Privacy')) {
    Test-Privacy
}

if ($Mode -in @('All', 'Golden')) {
    Test-GoldenDataset
}

if ($Mode -in @('All', 'Dashboards')) {
    Test-Dashboards
}

if ($Mode -in @('All', 'Negative')) {
    Test-BrokenIngestSelfTest
}

Write-Host "Verification mode '$Mode' completed."
# Expected negative probes invoke native commands that return non-zero. Once
# every assertion above has completed, do not leak that stale native status to
# a caller that invokes this script with PowerShell's call operator.
$global:LASTEXITCODE = 0
