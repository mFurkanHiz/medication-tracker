param([string]$BaseUrl = 'https://medicationtracker.rapidconfigs.com')
$ErrorActionPreference = 'Stop'
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$headers = @{ 'X-Medication-Client' = '1' }
function Send-Command([string]$Path, [object]$Body) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}
function Read-Data([string]$Path) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -WebSession $session
}
$suffix = [Guid]::NewGuid().ToString('N')
$password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$account = Send-Command '/auth/register' @{ email = "qa-$suffix@example.invalid"; password = $password }
$household = $account.householdId
if (-not $household) { throw 'Registration did not create a household.' }
$person = Send-Command "/households/$household/people" @{ name = 'Synthetic QA person' }
$medication = Send-Command "/households/$household/medications" @{ personId = $person.id; name = 'Synthetic QA tablet'; form = 'tablet'; stockNumerator = 15; stockDenominator = 2 }
$day = Get-Date -Format 'yyyy-MM-dd'
$plan = Send-Command "/households/$household/regimens" @{ personId = $person.id; medicationId = $medication.id; validFrom = $day; validTo = $null; doseNumerator = 1; doseDenominator = 2; localTime = '09:00:00'; timeZoneId = 'Europe/Istanbul' }
$doses = @(Read-Data "/households/$household/today?date=$day")
if ($doses.Count -ne 1 -or $doses[0].status -ne 'due') { throw 'Expected one due dose.' }
$command = @{ idempotencyKey = "qa-$suffix"; regimenVersionId = $plan.regimenVersionId; scheduledFor = $doses[0].scheduledFor; takenAt = [DateTimeOffset]::UtcNow.ToString('O'); outcome = 'taken' }
$first = Send-Command "/households/$household/sync/administrations" $command
$replay = Send-Command "/households/$household/sync/administrations" $command
if ($first.replayed -or -not $replay.replayed) { throw 'Idempotency failed.' }
$forecast = Read-Data "/households/$household/medications/$($medication.id)/forecast?date=$day"
if ($forecast.remainingNumerator -ne 7 -or $forecast.remainingDenominator -ne 1 -or $forecast.fullDaysRemaining -ne 14) { throw 'Fractional inventory projection failed.' }
$count = Send-Command "/households/$household/inventory/$($medication.id)" @{ idempotencyKey = "count-$suffix"; kind = 'count'; numerator = 5; denominator = 2 }
$workspace = Read-Data "/households/$household/workspace"
if ($workspace.medications[0].stockNumerator -ne 5 -or $workspace.medications[0].stockDenominator -ne 2) { throw 'Count reconciliation failed.' }
Send-Command '/auth/logout' @{} | Out-Null
Write-Output 'PASS: registration, household, medication, schedule, administration replay, exact forecast, count reconciliation, logout.'
