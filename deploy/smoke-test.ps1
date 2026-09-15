param([string]$BaseUrl = 'https://medicationtracker.rapidconfigs.com')
$ErrorActionPreference = 'Stop'
$session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
$headers = @{ 'X-Medication-Client' = '1' }
function Send-Command([string]$Path, [object]$Body) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Post -WebSession $session -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}
function Send-Update([string]$Path, [object]$Body) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Put -WebSession $session -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}
function Remove-Data([string]$Path) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Delete -WebSession $session -Headers $headers
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
$doses = @((Read-Data "/households/$household/today?date=$day") | ForEach-Object { $_ })
if ($doses.Count -ne 1 -or $doses[0].status -ne 'available' -or $null -ne $doses[0].scheduledFor) { throw 'Expected one available as-needed plan.' }
$command = @{ idempotencyKey = "qa-$suffix"; regimenVersionId = $plan.regimenVersionId; scheduledFor = $null; takenAt = [DateTimeOffset]::UtcNow.ToString('O'); outcome = 'taken' }
$first = Send-Command "/households/$household/sync/administrations" $command
$replay = Send-Command "/households/$household/sync/administrations" $command
if ($first.replayed -or -not $replay.replayed) { throw 'Idempotency failed.' }
$workspace = Read-Data "/households/$household/workspace"
if ($workspace.medications[0].strength -ne '10 mg' -or $workspace.medications[0].activeIngredient -ne 'Synthetic ingredient' -or $workspace.medications[0].notes -ne 'Synthetic package note') { throw 'Medication details did not persist.' }
if ($workspace.medications[0].stockNumerator -ne 27 -or ($workspace.medications[0].packages | Where-Object id -eq $partialPackage.id).remainingNumerator -ne 7) { throw 'Package-aware consumption failed.' }
$named = Send-Command "/households/$household/regimens" @{ personId = $person.id; medicationId = $medication.id; validFrom = $null; validTo = $null; doseNumerator = 1; doseDenominator = 2; localTime = $null; timeZoneId = 'Europe/Istanbul'; scheduleType = 'scheduled'; dayPeriod = 'morning'; mealRelation = 'with_food'; minimumIntervalMinutes = $null }
$doses = @((Read-Data "/households/$household/today?date=$day") | ForEach-Object { $_ })
$namedPeriodRows = @($doses | Where-Object { $_.dayPeriod -eq 'morning' -and $_.status -eq 'due' })
if ($doses.Count -ne 2 -or $namedPeriodRows.Count -ne 1) { throw "Named-period schedule failed (doses=$($doses.Count), named=$($namedPeriodRows.Count)): $($doses | ConvertTo-Json -Depth 6 -Compress)" }
Send-Update "/households/$household/medications/$($medication.id)" @{ personId = $person.id; name = 'Synthetic QA tablet updated'; form = 'tablet'; strength = '5 mg'; activeIngredient = 'Synthetic updated ingredient'; notes = 'Synthetic updated note'; category = 'Synthetic updated category'; tags = @('qa', 'crud'); isActive = $true } | Out-Null
$updatedPlan = Send-Update "/households/$household/regimens/$($plan.regimenId)" @{ personId = $person.id; medicationId = $medication.id; validFrom = $null; validTo = $null; doseNumerator = 1; doseDenominator = 2; localTime = $null; timeZoneId = 'Europe/Istanbul'; scheduleType = 'as_needed'; dayPeriod = 'night'; mealRelation = 'after_food'; minimumIntervalMinutes = 120 }
if ($updatedPlan.regimenVersionId -eq $plan.regimenVersionId) { throw 'Plan update did not append a new version.' }
Remove-Data "/households/$household/regimens/$($named.regimenId)" | Out-Null

$emptyMedication = Send-Command "/households/$household/medications" @{ personId = $null; name = 'Synthetic empty tablet'; form = 'tablet'; stockNumerator = 0; stockDenominator = 1; packages = @() }
$emptyPlan = Send-Command "/households/$household/regimens" @{ personId = $person.id; medicationId = $emptyMedication.id; validFrom = $null; validTo = $null; doseNumerator = 1; doseDenominator = 1; localTime = $null; timeZoneId = 'Europe/Istanbul'; scheduleType = 'as_needed'; dayPeriod = $null; mealRelation = $null; minimumIntervalMinutes = $null }
try {
    Send-Command "/households/$household/sync/administrations" @{ idempotencyKey = "empty-$suffix"; regimenVersionId = $emptyPlan.regimenVersionId; scheduledFor = $null; takenAt = [DateTimeOffset]::UtcNow.ToString('O'); outcome = 'taken' } | Out-Null
    throw 'Zero-stock administration unexpectedly succeeded.'
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 409) { throw }
}
$workspace = Read-Data "/households/$household/workspace"
$updatedMedication = @($workspace.medications | Where-Object id -eq $medication.id)
if ($updatedMedication.Count -ne 1 -or $updatedMedication[0].name -ne 'Synthetic QA tablet updated') { throw 'Full medication update failed.' }
$currentPlan = @($workspace.regimens | Where-Object id -eq $plan.regimenId)
if ($currentPlan.Count -ne 1 -or $currentPlan[0].versionId -ne $updatedPlan.regimenVersionId -or $currentPlan[0].doseDenominator -ne 2) { throw 'Current plan projection failed.' }
$activityKinds = @($workspace.activities | ForEach-Object kind)
foreach ($kind in @('medication_created', 'medication_updated', 'regimen_created', 'regimen_updated', 'regimen_deleted', 'administration_taken', 'package_assigned')) {
    if ($kind -notin $activityKinds) { throw "Activity entry missing: $kind" }
}
if (@($workspace.activities | Where-Object { $_.medicationId -eq $emptyMedication.id -and $_.kind -eq 'administration_taken' }).Count -ne 0) { throw 'Rejected administration was written to activity.' }
if (($workspace.medications | Where-Object id -eq $emptyMedication.id).stockNumerator -ne 0) { throw 'Rejected administration changed stock.' }
Remove-Data "/households/$household/medications/$($emptyMedication.id)" | Out-Null
$workspace = Read-Data "/households/$household/workspace"
if (@($workspace.medications | Where-Object id -eq $emptyMedication.id).Count -ne 0 -or @($workspace.regimens | Where-Object id -eq $emptyPlan.regimenId).Count -ne 0) { throw 'Medication soft delete or cascading plan removal failed.' }
Send-Command '/auth/logout' @{} | Out-Null
Write-Output 'PASS: package stock, assignment, full medication/plan CRUD, activity log, exact consumption, stock floor and logout.'
