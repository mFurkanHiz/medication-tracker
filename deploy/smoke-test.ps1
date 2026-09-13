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
$account = Send-Command '/auth/register' @{ email = "qa-$suffix@example.invalid"; password = $password; confirmPassword = $password }
$household = $account.householdId
if (-not $household) { throw 'Registration did not create a household.' }
$person = Send-Command "/households/$household/people" @{ name = 'Synthetic QA person' }
$medication = Send-Command "/households/$household/medications" @{ personId = $null; name = 'Synthetic QA tablet'; form = 'tablet'; stockNumerator = 0; stockDenominator = 1; strength = '10 mg'; activeIngredient = 'Synthetic ingredient'; notes = 'Synthetic package note'; category = 'Synthetic category'; tags = @('qa', 'occasional'); packages = @(@{ capacityNumerator = 20; capacityDenominator = 1; remainingNumerator = 20; remainingDenominator = 1; personId = $null }, @{ capacityNumerator = 20; capacityDenominator = 1; remainingNumerator = 8; remainingDenominator = 1; personId = $null }) }
$workspace = Read-Data "/households/$household/workspace"
if ($workspace.medications[0].stockNumerator -ne 28 -or $workspace.medications[0].packages.Count -ne 2) { throw 'Package stock total failed.' }
$partialPackage = $workspace.medications[0].packages | Where-Object remainingNumerator -eq 8 | Select-Object -First 1
Send-Command "/households/$household/inventory/packages/$($partialPackage.id)/assignment" @{ idempotencyKey = "assign-$suffix"; personId = $person.id } | Out-Null
$day = Get-Date -Format 'yyyy-MM-dd'
$plan = Send-Command "/households/$household/regimens" @{ personId = $person.id; medicationId = $medication.id; validFrom = $null; validTo = $null; doseNumerator = 1; doseDenominator = 1; localTime = $null; timeZoneId = 'Europe/Istanbul'; scheduleType = 'as_needed'; dayPeriod = $null; mealRelation = 'fasting'; minimumIntervalMinutes = 240 }
$doses = @(Read-Data "/households/$household/today?date=$day")
if ($doses.Count -ne 1 -or $doses[0].status -ne 'available' -or $null -ne $doses[0].scheduledFor) { throw 'Expected one available as-needed plan.' }
$command = @{ idempotencyKey = "qa-$suffix"; regimenVersionId = $plan.regimenVersionId; scheduledFor = $null; takenAt = [DateTimeOffset]::UtcNow.ToString('O'); outcome = 'taken' }
$first = Send-Command "/households/$household/sync/administrations" $command
$replay = Send-Command "/households/$household/sync/administrations" $command
if ($first.replayed -or -not $replay.replayed) { throw 'Idempotency failed.' }
$workspace = Read-Data "/households/$household/workspace"
if ($workspace.medications[0].strength -ne '10 mg' -or $workspace.medications[0].activeIngredient -ne 'Synthetic ingredient' -or $workspace.medications[0].notes -ne 'Synthetic package note') { throw 'Medication details did not persist.' }
if ($workspace.medications[0].stockNumerator -ne 27 -or ($workspace.medications[0].packages | Where-Object id -eq $partialPackage.id).remainingNumerator -ne 7) { throw 'Package-aware consumption failed.' }
$named = Send-Command "/households/$household/regimens" @{ personId = $person.id; medicationId = $medication.id; validFrom = $null; validTo = $null; doseNumerator = 1; doseDenominator = 2; localTime = $null; timeZoneId = 'Europe/Istanbul'; scheduleType = 'scheduled'; dayPeriod = 'morning'; mealRelation = 'with_food'; minimumIntervalMinutes = $null }
$doses = @(Read-Data "/households/$household/today?date=$day")
if ($doses.Count -ne 2 -or @($doses | Where-Object { $_.dayPeriod -eq 'morning' -and $_.status -eq 'due' }).Count -ne 1) { throw 'Named-period schedule failed.' }
Send-Command '/auth/logout' @{} | Out-Null
Write-Output 'PASS: independent medication, 20+8 package stock, assignment, as-needed use, exact package consumption, named period, logout.'
