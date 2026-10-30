param([string]$Type='get_available_tools',[string]$Code,[string]$ArgumentsJson='{}')
$ErrorActionPreference='Stop'
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','unity-mcp-acc4580f-8624',[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
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
        if($Code){$parameters=@{code=$Code;title='VOID LAST HUD and held item update'}}
        $writer.WriteLine((@{type=$Type;params=$parameters;requestId=[guid]::NewGuid().ToString()} | ConvertTo-Json -Depth 100 -Compress))
        do {$reply=Read-Message} while($reply.type -in @('command_in_progress','approval_pending','tools_changed'))
        if($Type -eq 'Unity_RunCommand' -and $reply.result.data){$reply.result.data | Select-Object isCompilationSuccessful,isExecutionSuccessful,compilationLogs,executionLogs,result | ConvertTo-Json -Depth 100}
        else {$reply | ConvertTo-Json -Depth 100}
    }
} finally {$pipe.Dispose()}
