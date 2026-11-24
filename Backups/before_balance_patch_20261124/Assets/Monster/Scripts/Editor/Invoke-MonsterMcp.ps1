param([string]$Type='get_available_tools',[string]$Code,[string]$ArgumentsJson='{}')
$ErrorActionPreference='Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$connection = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.unity/mcp/connections') -Filter 'bridge-*.json' |
    Sort-Object LastWriteTime -Descending | ForEach-Object {
        try {
            $candidate = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            if ($candidate.project_path -eq $project -and $candidate.connection_type -eq 'named_pipe' -and (Get-Process -Id $candidate.editor_pid -ErrorAction SilentlyContinue)) { $candidate }
        } catch { }
    } | Select-Object -First 1
if ($null -eq $connection) { throw 'No running Unity MCP connection for this project.' }
$pipeName = [IO.Path]::GetFileName($connection.connection_path)
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
try {
    $pipe.Connect(5000)
    $reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false))
    $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false)); $writer.AutoFlush=$true
    function Read-Message { $task=$reader.ReadLineAsync(); if(-not $task.Wait(55000)){throw 'Unity MCP timeout'}; if($null -eq $task.Result){throw 'Unity disconnected during reload'}; return ($task.Result | ConvertFrom-Json -Depth 100) }
    $hello=Read-Message
    if($hello.type -eq 'approval_denied'){throw ($hello | ConvertTo-Json -Compress)}
    if($Type -eq 'get_available_tools' -and $hello.tools){$hello.tools | ConvertTo-Json -Depth 100}
    else {
        $parameters=$ArgumentsJson | ConvertFrom-Json -AsHashtable
        if($Code){$parameters=@{code=$Code;title='VOID LAST Monster prototype'}}
        $writer.WriteLine((@{type=$Type;params=$parameters;requestId=[guid]::NewGuid().ToString()} | ConvertTo-Json -Depth 100 -Compress))
        do {$reply=Read-Message} while($reply.type -in @('command_in_progress','approval_pending','tools_changed'))
        if($Type -eq 'Unity_RunCommand' -and $reply.result.data){$reply.result.data | Select-Object isCompilationSuccessful,isExecutionSuccessful,compilationLogs,executionLogs,result | ConvertTo-Json -Depth 100}
        else {$reply | ConvertTo-Json -Depth 100}
    }
} finally {$pipe.Dispose()}
