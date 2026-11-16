<#
.SYNOPSIS
    Runs each AI test case N times against the MatMentor tutor model via Ollama
    and writes the replies in the same column order as the "Vurderinger" table
    in the AI-vurdering sheet.

.DESCRIPTION
    The system prompt is read directly from TutorInstructions.cs, and the first
    user message is built like OllamaTutorModelClient does:
    "Opgavetekst: ... Det har eleven prøvet: ...".

    Case fields (cases.json):
      opgave       - task ID (OP-01, ...) from the "opgaver" list
      forsoeg      - the student's attempt, sent with the task text
      beskeder     - extra student messages sent together with the first request
      opfoelgning  - student messages sent one at a time, each after the tutor
                     has answered (used for multi-turn cases such as TC-046)

    Output files in the results folder:
      vurderinger-<stamp>.csv  Dato;Test-ID;Opgave-ID;Elevens besked;Tutorens svar
                               -> paste into the Vurderinger table, columns A-E
      log-<stamp>.csv          run number, model, prompt version, time, status
      for-dommer-<stamp>.csv   what the judge model needs (no expected results)

.EXAMPLE
    .\Invoke-TutorRun.ps1 -Runs 5
#>
[CmdletBinding()]
param(
    [string]$CasesFile = ".\cases.json",
    [int]$Runs = 5,
    [string]$OllamaUrl = "http://localhost:11434",
    # Must match Ollama__Model in compose.yaml.
    [string]$Model = "gemma3n:e2b",
    [string]$PromptFile = "..\Application\Features\ChatTutor\AskTutor\TutorInstructions.cs",
    [string]$OutputDir = ".\results"
)

$ErrorActionPreference = "Stop"

# --- 1. Read the system prompt from the repository -------------------------
$source = Get-Content -Path $PromptFile -Raw -Encoding UTF8
$match = [regex]::Match($source, '"""(.*?)"""', [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $match.Success) {
    throw "Could not find the raw string literal in $PromptFile"
}
# C# raw string literals strip the common leading whitespace of each line.
$lines = $match.Groups[1].Value -split "`r?`n"
$indent = ($lines | Where-Object { $_.Trim() -ne "" } |
    ForEach-Object { $_.Length - $_.TrimStart().Length } |
    Measure-Object -Minimum).Minimum
$systemPrompt = (($lines | ForEach-Object {
    if ($_.Length -ge $indent) { $_.Substring($indent) } else { $_ }
}) -join "`n").Trim()

# A short hash of the prompt file identifies the prompt version in the log.
$promptHash = (Get-FileHash -Path $PromptFile -Algorithm SHA256).Hash.Substring(0, 8)

# --- 2. Load tasks and cases -------------------------------------------------
$config = Get-Content -Path $CasesFile -Raw -Encoding UTF8 | ConvertFrom-Json
$tasks = @{}
foreach ($t in $config.opgaver) { $tasks[$t.id] = $t }

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$today = Get-Date -Format "yyyy-MM-dd"

$sheetRows = New-Object System.Collections.Generic.List[object]
$logRows   = New-Object System.Collections.Generic.List[object]
$judgeRows = New-Object System.Collections.Generic.List[object]

Write-Host "Model: $Model | Prompt version: $promptHash | Runs per case: $Runs | Cases: $(@($config.cases).Count)"
Write-Host ""

# --- 3. Call Ollama ------------------------------------------------------------
function Invoke-Tutor {
    param([object[]]$Messages)

    $payload = @{ model = $Model; stream = $false; messages = $Messages } | ConvertTo-Json -Depth 10
    # Send and read as UTF-8 so Danish characters survive on PowerShell 5.1 and 7.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $response = Invoke-WebRequest -Uri "$OllamaUrl/api/chat" -Method Post `
        -ContentType "application/json; charset=utf-8" -Body $bytes -UseBasicParsing
    $reader = New-Object System.IO.StreamReader($response.RawContentStream, [System.Text.Encoding]::UTF8)
    $json = $reader.ReadToEnd() | ConvertFrom-Json
    return $json.message.content
}

foreach ($case in $config.cases) {
    $task = $tasks[$case.opgave]
    if ($null -eq $task) { throw "Case $($case.id) refers to unknown task $($case.opgave)" }

    $extra    = @($case.beskeder    | Where-Object { $_ })
    $followUp = @($case.opfoelgning | Where-Object { $_ })

    # What the student wrote, in order, for the "Elevens besked" column.
    $studentText = (@($case.forsoeg) + $extra + $followUp) -join " / "

    for ($run = 1; $run -le $Runs; $run++) {
        # Same message layout as OllamaTutorModelClient.AskAsync.
        $messages = @(
            @{ role = "system"; content = $systemPrompt }
            @{ role = "user";   content = "Opgavetekst:`n$($task.tekst)`n`nDet har eleven prøvet:`n$($case.forsoeg)" }
        )
        foreach ($m in $extra) { $messages += @{ role = "user"; content = $m } }

        $started = Get-Date
        $status = "ok"
        $replies = New-Object System.Collections.Generic.List[string]
        try {
            $replies.Add((Invoke-Tutor -Messages $messages))
            # Multi-turn: send each follow-up only after the tutor has answered.
            foreach ($f in $followUp) {
                $messages += @{ role = "assistant"; content = $replies[$replies.Count - 1] }
                $messages += @{ role = "user"; content = $f }
                $replies.Add((Invoke-Tutor -Messages $messages))
            }
        }
        catch {
            $replies.Add("FEJL: $($_.Exception.Message)")
            $status = "error"
        }
        $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)

        # One reply: as is. Several replies: numbered, with the student's message between.
        if ($replies.Count -eq 1) {
            $replyText = $replies[0]
        }
        else {
            $parts = @()
            for ($i = 0; $i -lt $replies.Count; $i++) {
                if ($i -gt 0) { $parts += "[Eleven: $($followUp[$i - 1])]" }
                $parts += "[Svar $($i + 1)] $($replies[$i])"
            }
            $replyText = $parts -join "`n`n"
        }

        $sheetRows.Add([pscustomobject][ordered]@{
            "Dato"           = $today
            "Test-ID"        = $case.id
            "Opgave-ID"      = $case.opgave
            "Elevens besked" = $studentText
            "Tutorens svar"  = $replyText
        })
        $logRows.Add([pscustomobject][ordered]@{
            "Test-ID" = $case.id; "Koersel" = $run; "Model" = $Model
            "Promptversion" = $promptHash; "Tidspunkt" = $started.ToString("s")
            "Sekunder" = $seconds; "Status" = $status
        })
        $judgeRows.Add([pscustomobject][ordered]@{
            "Test-ID" = $case.id; "Koersel" = $run
            "Opgavetekst" = $task.tekst; "Facit" = $task.facit
            "Elevens besked" = $studentText; "Tutorens svar" = $replyText
        })

        Write-Host ("[{0} run {1}] {2}s {3}" -f $case.id, $run, $seconds, $status)
    }
}

# Semicolon-separated so Danish Excel and Google Sheets split the columns correctly.
$sheetRows | Export-Csv -Path (Join-Path $OutputDir "vurderinger-$stamp.csv") -Delimiter ";" -NoTypeInformation -Encoding UTF8
$logRows   | Export-Csv -Path (Join-Path $OutputDir "log-$stamp.csv")         -Delimiter ";" -NoTypeInformation -Encoding UTF8
$judgeRows | Export-Csv -Path (Join-Path $OutputDir "for-dommer-$stamp.csv")  -Delimiter ";" -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host "Done. $($sheetRows.Count) replies written to $OutputDir (vurderinger-, log- and for-dommer-$stamp.csv)"
