param([Parameter(Mandatory=$true)][string]$ManifestPath)
$ErrorActionPreference = 'Stop'
function Read-SharedText([string]$Path) {
    for ($attempt=0; ; $attempt++) {
        try {
            $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try { $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8); try { return $reader.ReadToEnd() } finally { $reader.Dispose() } } finally { $stream.Dispose() }
        } catch {
            $cause=$_.Exception.GetBaseException()
            if ($cause -isnot [IO.IOException] -or ($cause.HResult -band 0xffff) -notin @(32,33) -or $attempt -ge 5) { throw }
            Start-Sleep -Milliseconds 40
        }
    }
}
$utf8 = [Text.UTF8Encoding]::new($false)
$inspection = $null
try {
    $manifest = Get-Content -LiteralPath $ManifestPath -Encoding UTF8 | ConvertFrom-Json
    $root = [IO.Path]::GetFullPath($manifest.Root)
    $accountId = ([Guid]$manifest.AccountId).ToString('D')
    $marker = Get-Content -LiteralPath (Join-Path $root 'combat-flow-soak-isolated.json') -Encoding UTF8 | ConvertFrom-Json
    if ($marker.Purpose -ne 'CombatFlowSoak' -or $marker.InstanceId -ne $accountId) { throw 'Isolated account marker mismatch' }
    $start = [DateTimeOffset]::Parse($manifest.StartedAtUtc)
    $due = [DateTimeOffset]::Parse($manifest.CheckAtUtc)
    $now = [DateTimeOffset]::UtcNow
    if ($now -lt $due) { throw 'Inspection is not due yet' }
    $output = [IO.Path]::GetFullPath($manifest.OutputDirectory)
    [void](New-Item -ItemType Directory -Path $output -Force)
    $inspection = Join-Path $output ('inspection-' + $now.ToString('yyyyMMdd-HHmmss-ffff'))
    [void](New-Item -ItemType Directory -Path $inspection)
    $snapshot = Join-Path $inspection 'logs'
    [void](New-Item -ItemType Directory -Path $snapshot)
    $symbols = Get-Content -LiteralPath $manifest.SymbolsPath -Encoding UTF8 | ConvertFrom-Json
    $levels = @('info','warn','error')
    $entries = [Collections.Generic.List[object]]::new()
    $readIssues = [Collections.Generic.List[string]]::new()
    $incompleteTail = 0
    $logDirectory = Join-Path $root ('logs\accounts\' + $accountId)
    foreach ($log in Get-ChildItem -LiteralPath $logDirectory -Filter '*.log' -File) {
        $copy = Join-Path $snapshot $log.Name
        try { Copy-Item -LiteralPath $log.FullName -Destination $copy }
        catch { $readIssues.Add($log.Name + ': copy failed: ' + $_.Exception.GetType().Name); continue }
        $lines = [IO.File]::ReadAllLines($copy, [Text.Encoding]::UTF8)
        for ($i=0; $i -lt $lines.Length; $i++) {
            try {
                $row = $lines[$i] | ConvertFrom-Json
                if ($row -is [Array]) {
                    if ($row[0] -ne 'R1' -or $row.Count -lt 6) { throw 'Unsupported log record' }
                    $at = [DateTimeOffset]::new([long]$row[1], [TimeSpan]::FromMinutes([int]$row[2]))
                    if ($at -lt $start -or $at -gt $due) { continue }
                    $level = if ($row[3] -is [string]) { $row[3] } else { $levels[[int]$row[3]] }
                    $event = if ($row[4] -is [string]) { $row[4] } else { $symbols.Events[[int]$row[4]] }
                    $fields = [ordered]@{}
                    if ($row[5].Count % 2 -ne 0) { throw 'Unpaired fields' }
                    for ($j=0; $j -lt $row[5].Count; $j+=2) {
                        $key = if ($row[5][$j] -is [string]) { $row[5][$j] } else { $symbols.Fields[[int]$row[5][$j]] }
                        $fields[$key] = $row[5][$j+1]
                    }
                    $detail = if ($row.Count -gt 6) { $row[6] } else { $null }
                    $entries.Add([pscustomobject]@{Timestamp=$at;Level=$level;EventName=$event;Fields=[pscustomobject]$fields;Error=$detail})
                } else {
                    $at = [DateTimeOffset]::Parse($row.Timestamp)
                    if ($at -ge $start -and $at -le $due) {
                        $entries.Add([pscustomobject]@{Timestamp=$at;Level=$row.Level;EventName=$row.EventName;Fields=$row.Fields;Error=$row.Error})
                    }
                }
            } catch {
                if ($log.Name -eq 'latest.log' -and $i -eq $lines.Length-1 -and $lines[$i] -notmatch '[\]}]\s*$') {
                    $incompleteTail++
                } else { $readIssues.Add($log.Name + ':' + ($i+1) + ': ' + $_.Exception.GetType().Name) }
            }
        }
    }
    $ordered = @($entries | Sort-Object Timestamp)
    $kills = @($ordered | Where-Object EventName -in @('stationary_combat.target.kill_counted','semi_auto.target.kill_counted'))
    $deaths = @($ordered | Where-Object EventName -in @('player_life.death.detected','stationary_combat.death.detected'))
    $summonHpTurns = @($ordered | Where-Object EventName -eq 'combat_flow.summon.hp_turn')
    $summonConfirmed = @($ordered | Where-Object EventName -eq 'semi_auto.spiritmaster.summon_verified')
    $summonTimeouts = @($ordered | Where-Object EventName -eq 'semi_auto.spiritmaster.summon_verify_timeout')
    $healthObservations = @($ordered | Where-Object EventName -eq 'combat_flow.health.observed')
    $errors = @($ordered | Where-Object Level -eq 'error')
    $warnings = @($ordered | Where-Object Level -eq 'warn' | Group-Object EventName | Sort-Object Count -Descending | ForEach-Object {[pscustomobject]@{Event=$_.Name;Records=$_.Count}})
    $report = Read-SharedText (Join-Path $root 'combat-flow-soak-status.json') | ConvertFrom-Json
    # Log decoding takes time; the running harness may publish a sample after
    # inspection began. Compare freshness with the time this report was read.
    $statusObservedAt = [DateTimeOffset]::UtcNow
    $matchingSession = $report.SessionId -eq $manifest.SessionId
    $sample = $report.Sample
    $statusAge = if ($sample.timestampUtc) { ($statusObservedAt - [DateTimeOffset]::Parse($sample.timestampUtc)).TotalSeconds } else { [double]::PositiveInfinity }
    $liveVersionConfirmed = $matchingSession -and !$report.CompletedAtUtc -and $sample.isRunning -and $sample.state -eq 'running' -and $sample.actualVersion -eq 'CoordinatedV2' -and $statusAge -ge 0 -and $statusAge -lt 20
    $workerProcesses = @(Get-CimInstance Win32_Process -Filter "Name='Roadhog.exe'" | Where-Object ExecutablePath -eq (Join-Path $root 'Roadhog.exe') | Select-Object ProcessId,CreationDate)
    $samples = [Collections.Generic.List[object]]::new()
    $reportWriteFailures = [Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $manifest.ConsoleLog) {
        # The continuous harness still owns a write handle. Get-Content permits
        # shared writes; File.ReadAllLines would reject this live file on Windows.
        foreach ($line in Get-Content -LiteralPath $manifest.ConsoleLog -Encoding UTF8) {
            try {
                $item = $line | ConvertFrom-Json
                if ($item.kind -eq 'sample') {
                    $at = [DateTimeOffset]::Parse($item.value.timestampUtc)
                    if ($at -ge $start -and $at -le $due) { $samples.Add($item.value) }
                }
                elseif ($item.kind -eq 'report_write_failed') {
                    $at = [DateTimeOffset]::Parse($item.value.publication.LastFailureAtUtc)
                    if ($at -ge $start -and $at -le $due) { $reportWriteFailures.Add($item.value) }
                }
            } catch { } # A live console tail may be unfinished; the authoritative account log issues are listed separately.
        }
    }
    $unhealthy = @($samples | Where-Object {
        !$_.isRunning -or $_.state -ne 'running' -or $_.actualVersion -ne 'CoordinatedV2' -or
        $_.managerErrorPresent -or $_.workerErrorPresent -or
        ([DateTimeOffset]::Parse($_.timestampUtc)-[DateTimeOffset]::Parse($_.workerReportedAtUtc)).TotalSeconds -ge 10
    })
    $pids = @($samples | Where-Object workerPid | Select-Object -ExpandProperty workerPid -Unique)
    $gaps = [Collections.Generic.List[object]]::new()
    $previous = $start
    foreach ($next in @($kills | ForEach-Object Timestamp) + @($due)) {
        $seconds = ($next - $previous).TotalSeconds
        if ($seconds -ge 120) {
            $contextEvents = @($ordered | Where-Object {$_.Timestamp -ge $previous -and $_.Timestamp -le $next -and $_.EventName -match 'death|revive|recovery|cleanup|path.*(arriv|point)|maintenance|summon'} | Group-Object EventName | Sort-Object Count -Descending | Select-Object -First 10 | ForEach-Object {$_.Name})
            $gaps.Add([pscustomobject]@{From=$previous;To=$next;Seconds=[Math]::Round($seconds);ContextEvents=$contextEvents})
        }
        $previous = $next
    }
    $coverageStart = @($ordered | Where-Object EventName -eq 'worker.loop.enter').Count -gt 0
    $findings = [Collections.Generic.List[string]]::new()
    if (!$liveVersionConfirmed) { $findings.Add('检查时未确认同一会话的新版本仍在正常运行。') }
    if ($workerProcesses.Count -ne 1) { $findings.Add('独立目录当前worker数量不是1。') }
    if ($readIssues.Count -or !$coverageStart) { $findings.Add('日志读取或起始窗口覆盖不完整，不能作完整三小时结论。') }
    if (!$samples.Count) { $findings.Add('缺少本轮监测采样。') }
    if ($errors.Count) { $findings.Add('存在error记录，须区分运行故障和停止时取消。') }
    if ($deaths.Count) { $findings.Add('发生角色死亡；不能将进程仍运行视为全程正常。') }
    if ($reportWriteFailures.Count) { $findings.Add('出现监测报告发布失败；请结合report-conflict-live-test.json核对是否为主动故障注入，不能据此判定账号停止。') }
    if ($unhealthy.Count -or $pids.Count -gt 1) { $findings.Add('存在异常状态采样或worker更换，需要复核。') }
    if (!$kills.Count) { $findings.Add('没有确认击杀，需要结合移动、恢复或其他任务判断实际进展。') }
    if ($gaps.Count) { $findings.Add('存在超过120秒的击杀间隔；可能是返程、清包或恢复，需要按上下文判断，不能直接称为卡死。') }
    $summary = [ordered]@{
        SessionId=$manifest.SessionId; WindowStart=$start; WindowEnd=$due; InspectedAtUtc=$now
        WindowMinutes=[Math]::Round(($due-$start).TotalMinutes,2)
        LiveVersionConfirmed=$liveVersionConfirmed; StatusAgeSeconds=[Math]::Round($statusAge,1); StatusObservedAtUtc=$statusObservedAt
        WorkerProcesses=$workerProcesses; Samples=$samples.Count; UnhealthySamples=$unhealthy.Count; ObservedWorkerPids=$pids
        LogRecords=$ordered.Count; LogStartCovered=$coverageStart; IncompleteLiveTail=$incompleteTail; ReadIssues=@($readIssues)
        ConfirmedKills=$kills.Count; Deaths=$deaths.Count; ErrorRecords=$errors.Count; WarningGroups=$warnings
        SummonHpTurns=$summonHpTurns.Count; SummonConfirmations=$summonConfirmed.Count; SummonTimeouts=$summonTimeouts.Count
        HealthObservations=$healthObservations.Count
        ReportWriteFailures=@($reportWriteFailures)
        KillGaps=@($gaps); Findings=@($findings)
    }
    [IO.File]::WriteAllText((Join-Path $inspection 'summary.json'),($summary | ConvertTo-Json -Depth 12),$utf8)
    $details = @($ordered | Where-Object {$_.Level -eq 'error' -or $_.EventName -match 'death|revive|summon_verify_timeout|summon_verified|combat_flow\.summon\.hp_turn|status_unconfirmed|worker\.read_budget\.yield|release\.unconfirmed'})
    [IO.File]::WriteAllText((Join-Path $inspection 'attention-events.json'),(ConvertTo-Json -InputObject $details -Depth 12),$utf8)
    $cn = [TimeSpan]::FromHours(8)
    $body = [Collections.Generic.List[string]]::new()
    $body.Add('# 脚本6新版日志检查')
    $body.Add('')
    $body.Add('窗口：' + $start.ToOffset($cn).ToString('yyyy-MM-dd HH:mm:ss') + ' 至 ' + $due.ToOffset($cn).ToString('yyyy-MM-dd HH:mm:ss') + '（北京时间）')
    $body.Add('实际检查：' + $now.ToOffset($cn).ToString('yyyy-MM-dd HH:mm:ss'))
    $body.Add('观察时长：' + [Math]::Round(($due-$start).TotalMinutes,1) + '分钟')
    $body.Add('')
    $body.Add($(if($findings.Count){'结论：发现需要进一步复核的情况，见下方。'}else{'结论：自动检查未发现死亡、error或运行状态异常；这不等于全部业务无缺陷。'}))
    $body.Add('')
    $body.Add('- 检查时确认新版仍在运行：' + $liveVersionConfirmed)
    $body.Add('- 日志确认击杀：' + $kills.Count + '；死亡：' + $deaths.Count + '；error记录：' + $errors.Count)
    $body.Add('- 监测采样：' + $samples.Count + '；异常采样：' + $unhealthy.Count + '；观察到的worker数量：' + $pids.Count)
    $body.Add('- 超过120秒的击杀间隔：' + $gaps.Count)
    $body.Add('- 重召前HP交接：' + $summonHpTurns.Count + '；召唤确认：' + $summonConfirmed.Count + '；召唤确认超时：' + $summonTimeouts.Count)
    $body.Add('- 血量与流程阶段诊断记录：' + $healthObservations.Count + '（可能复用快照，以playerCapturedAt为准）')
    $body.Add('- 监测报告发布失败记录：' + $reportWriteFailures.Count + '；主动故障注入另有独立记录。')
    if (!$summonHpTurns.Count) { $body.Add('- 本轮未自然触发重召前HP交接，不能把持续运行正常写成该分支已通过实机验证。') }
    $body.Add('- 日志起始覆盖：' + $coverageStart + '；读取问题：' + $readIssues.Count)
    $body.Add('')
    foreach($finding in $findings){$body.Add('- ' + $finding)}
    $body.Add('')
    $body.Add('警告记录统计（含限频摘要，不是底层失败总次数）：')
    foreach($warning in $warnings){$body.Add('- ' + $warning.Event + '：' + $warning.Records)}
    $body.Add('')
    $body.Add('本任务只读取日志、状态和进程信息，不停止脚本、不改配置、不操作1–5。')
    $body.Add('技能未确认、宠物重召和死亡的因果需要结合attention-events.json及日志原件复核；不得只凭计数推断。')
    $reportName = if (($due-$start).TotalMinutes -ge 180) { '三小时检查报告.md' } else { '阶段检查报告.md' }
    [IO.File]::WriteAllLines((Join-Path $output $reportName),$body,$utf8)
    [IO.File]::WriteAllText((Join-Path $output 'review-completed.json'),([pscustomobject]@{CompletedAtUtc=$now;SessionId=$manifest.SessionId;InspectionDirectory=$inspection;Findings=$findings.Count;Success=$true}|ConvertTo-Json),$utf8)
    Write-Output ('Inspection completed: ' + $inspection)
    exit 0
} catch {
    if($inspection){[IO.File]::WriteAllText((Join-Path $inspection 'review-failed.txt'),$_.Exception.ToString(),$utf8)}
    Write-Error $_
    exit 1
}
