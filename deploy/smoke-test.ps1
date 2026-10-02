# Post-deployment smoke test against the live site.
#
# Runs the owner's mandatory acceptance scenario end to end over HTTPS: three physical
# packages totalling 48, an automatic dose that draws from the opened box, an explicit
# dose from a chosen box, and a correction that restores the wrong box while leaving the
# medication total untouched.
#
# Creates one synthetic household with invented data. No real person, prescription or
# medication record is used, and nothing here reads existing household data.

param([string]$BaseUrl = 'https://medicationtracker.rapidconfigs.com')

$ErrorActionPreference = 'Stop'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

# The API refuses a mutation that does not carry this header, so a cross-site form post
# cannot reach an endpoint at all.
$headers = @{ 'X-Medication-Client' = '1' }

function Send-Command([string]$Path, $Body) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Post -WebSession $session -Headers $headers `
        -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}

function Send-Update([string]$Path, $Body) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -Method Put -WebSession $session -Headers $headers `
        -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}

function Read-Data([string]$Path) {
    Invoke-RestMethod -Uri "$BaseUrl/api$Path" -WebSession $session
}

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "SMOKE FAILED: $Message" }
}

$suffix = [guid]::NewGuid().ToString('N')
$email = "synthetic-smoke-$suffix@example.invalid"
$password = 'synthetic-smoke-password-1'

Write-Host "Smoke test against $BaseUrl"

# --- the site itself serves ----------------------------------------------------------
$home = Invoke-WebRequest -Uri $BaseUrl -UseBasicParsing
Assert ($home.StatusCode -eq 200) 'The site did not return 200.'

# --- registration and session --------------------------------------------------------
$account = Send-Command '/auth/register' @{ email = $email; password = $password; confirmPassword = $password }
$household = $account.householdId
Assert ($null -ne $household) 'Registration did not create a household.'

$profile = Read-Data '/auth/session'
Assert ($profile.households.Count -ge 1) 'The session reported no household.'

# --- the catalog is separate from stock ----------------------------------------------
$definition = Send-Command "/households/$household/medication-definitions" @{
    name                            = 'Parol 500 mg'
    form                            = 'Tablet'
    strength                        = '500 mg'
    activeIngredients               = @('paracetamol')
    defaultPackageCapacityNumerator = 20
}

# --- two full boxes and one opened box become THREE packages -------------------------
$stock = Send-Command "/households/$household/inventory/$($definition.id)/stock" @{
    capacityNumerator = 20
    fullPackages      = 2
    openedPackages    = @(@{ remainingNumerator = 8 })
}

Assert ($stock.packages.Count -eq 3) "Expected three packages, got $($stock.packages.Count)."

$inventory = Read-Data "/households/$household/inventory/$($definition.id)"
Assert ($inventory.total.display -eq '48') "Expected a total of 48, got $($inventory.total.display)."
Assert ($inventory.packageCount -eq 3) 'Expected three packages in the inventory read.'

$ordinals = @($inventory.packages | ForEach-Object { $_.ordinal })
Assert (($ordinals | Sort-Object) -join ',' -eq '1,2,3') 'Package ordinals are not 1, 2, 3.'

$boxA = $inventory.packages | Where-Object ordinal -eq 1 | Select-Object -First 1
$boxB = $inventory.packages | Where-Object ordinal -eq 2 | Select-Object -First 1
$boxC = $inventory.packages | Where-Object ordinal -eq 3 | Select-Object -First 1

Assert ($boxC.state -eq 'Opened') 'The partly-used box was not recorded as opened.'
Assert ($boxA.state -eq 'Sealed') 'A full box was not recorded as sealed.'
Assert ($boxC.remaining.display -eq '8') 'The opened box does not hold 8.'

# --- a person and a daily plan -------------------------------------------------------
$person = Send-Command "/households/$household/people" @{ name = 'Synthetic person' }

$plan = Send-Command "/households/$household/plans" @{
    personId                = $person.id
    medicationDefinitionId  = $definition.id
    doseNumerator           = 1
    doseDenominator         = 1
    timeZoneId              = 'UTC'
    kind                    = 'Scheduled'
    pattern                 = 'Daily'
    localTime               = '08:00:00'
}

# --- one tap: no package named, the policy chooses the opened box --------------------
$today = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
$slot = "$($today)T08:00:00.0000000+00:00"

$dose = Send-Command "/households/$household/administrations" @{
    planVersionId = $plan.versionId
    scheduledFor  = $slot
}

Assert ($dose.allocations.Count -eq 1) 'The dose did not produce exactly one allocation.'
Assert ($dose.allocations[0].packageId -eq $boxC.id) 'The dose did not draw from the opened box.'

$inventory = Read-Data "/households/$household/inventory/$($definition.id)"
Assert ($inventory.total.display -eq '47') "Expected 47 after one dose, got $($inventory.total.display)."

# --- the correction: "I actually used Box 2" -----------------------------------------
$allocationId = $dose.allocations[0].allocationId
$totalBefore = $inventory.total.display

Send-Command "/households/$household/administrations/$($dose.administrationEventId)/allocations/$allocationId/correction" @{
    target    = 'SpecificPackage'
    packageId = $boxB.id
    reason    = 'smoke test correction'
} | Out-Null

$inventory = Read-Data "/households/$household/inventory/$($definition.id)"
$afterC = $inventory.packages | Where-Object id -eq $boxC.id | Select-Object -First 1
$afterB = $inventory.packages | Where-Object id -eq $boxB.id | Select-Object -First 1

Assert ($afterC.remaining.display -eq '8') 'The wrongly-charged box was not restored to 8.'
Assert ($afterB.remaining.display -eq '19') 'The corrected box was not debited to 19.'
Assert ($inventory.total.display -eq $totalBefore) 'The correction changed the medication total.'

# The superseded allocation is retained, not deleted.
$allocations = Read-Data "/households/$household/administrations/$($dose.administrationEventId)/allocations"
Assert ($allocations.allocations.Count -eq 2) 'The superseded allocation was not retained.'
Assert (@($allocations.allocations | Where-Object isActive).Count -eq 1) 'More than one allocation is active.'
Assert ($allocations.corrections.Count -eq 1) 'The correction is not visible in history.'
Assert ($allocations.corrections[0].fromPackageLabel -eq 3) 'The correction does not name the box it came from.'
Assert ($allocations.corrections[0].toPackageLabel -eq 2) 'The correction does not name the box it moved to.'

# --- an untracked dose keeps the record without touching stock -----------------------
$untracked = Send-Command "/households/$household/administrations" @{
    personId                     = $person.id
    medicationDefinitionId       = $definition.id
    outcome                      = 'ExtraDose'
    source                       = 'UntrackedExternal'
    actualQuantityNumerator      = 1
}

Assert ($untracked.stockSource -eq 'UntrackedExternal') 'The untracked dose was not recorded as untracked.'
Assert ($untracked.allocations.Count -eq 0) 'The untracked dose allocated stock.'

$inventory = Read-Data "/households/$household/inventory/$($definition.id)"
Assert ($inventory.total.display -eq $totalBefore) 'The untracked dose changed the stock total.'

# --- refill: depletion and official eligibility are separate -------------------------
Send-Update "/households/$household/medication-definitions/$($definition.id)/refill-policy" @{
    lowStockDays         = 7
    nextEligibleRefillOn = (Get-Date).ToUniversalTime().AddDays(90).ToString('yyyy-MM-dd')
} | Out-Null

$forecast = Read-Data "/households/$household/medication-definitions/$($definition.id)/forecast"
Assert $forecast.isForecastable 'The forecast is not computable for a daily plan.'
Assert ($null -ne $forecast.projectedDepletionOn) 'The forecast produced no depletion date.'
Assert $forecast.hasRefillGap 'A 90-day refill date against 47 daily tablets should report a gap.'

# --- the aggregated read the web client renders from ---------------------------------
$workspace = Read-Data "/households/$household/workspace"
Assert ($workspace.medications.Count -eq 1) 'The workspace did not return the medication.'
Assert ($workspace.medications[0].packageCount -eq 3) 'The workspace lost a package.'
Assert ($workspace.plans.Count -eq 1) 'The workspace did not return the plan.'

$activity = Read-Data "/households/$household/activity"
Assert ($activity.allocationCorrections.Count -eq 1) 'The correction is missing from the activity surface.'
Assert ($activity.administrations.Count -eq 2) 'The activity surface lost an administration.'

# --- sign out ------------------------------------------------------------------------
Send-Command '/auth/logout' @{} | Out-Null

Write-Host 'SMOKE PASSED: packages, allocation, correction, untracked source, refill gap and activity all verified.'
