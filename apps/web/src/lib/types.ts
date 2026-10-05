import type { Quantity } from './quantity';

/**
 * Shapes returned by the API, mirrored here so the client fails to compile rather
 * than failing at runtime when a response changes.
 */

export type Session = {
  accountId: string;
  households: { id: string; name: string }[];
};

export type Person = {
  id: string;
  name: string;
  isArchived: boolean;
};

export type PackageState = 'Sealed' | 'Opened' | 'Disposed' | 'Lost' | 'Archived';

/** One physical container. The interface shows its ordinal, never its identifier. */
export type MedicationPackage = {
  id: string;
  ordinal: number;
  /** The household's own name for the box, shown instead of the ordinal when set. */
  label?: string | null;
  /** Overrides the medicine's coverage for this box; null inherits it. */
  coverage?: string | null;
  state: PackageState;
  /** Derived from the ledger, never stored. */
  isEmpty: boolean;
  nominalCapacity: Quantity;
  remaining: Quantity;
  unit: string;
  openedAt: string | null;
  expiresOn: string | null;
  acquiredOn: string | null;
  lotNumber: string | null;
  barcode: string | null;
  source: string | null;
  storageLocation: string | null;
  note: string | null;
  ownerPersonId: string | null;
  holderPersonId: string | null;
  isPinned: boolean;
};

export type RefillPolicy = {
  lowStockThreshold: Quantity | null;
  lowStockDays: number | null;
  nextEligibleRefillOn: string | null;
  /** When all stock is expected to run out, as the household recorded it. */
  expectedDepletionOn: string | null;
  note: string | null;
};

/**
 * What the household was told about taking a medicine safely, in their own words.
 *
 * Null when they have recorded nothing, so the screen can decide whether to show the
 * block at all with one check. The software never derives any of this: it is prose
 * somebody copied off a box or heard from a pharmacist, and it is stored and shown
 * rather than evaluated.
 */
export type CautionNotes = {
  doNotTakeWith: string | null;
  foodsToAvoid: string | null;
  thingsToDo: string | null;
  thingsToAvoid: string | null;
  warning: string | null;
};

export type MedicationDefinition = {
  id: string;
  name: string;
  strength: string | null;
  brand: string | null;
  manufacturer: string | null;
  form: string;
  unit: string;
  activeIngredients: string[];
  defaultPackageCapacity: Quantity | null;
  category: string | null;
  tags: string[];
  /** Who paid: 'InsuranceCovered', 'SelfPaid' or 'Unspecified' (treated as covered). */
  coverage: string;
  /** Names and ingredients not to combine with, as the household tagged them. */
  doNotTakeWithTags: string[];
  notes: string | null;
  cautions: CautionNotes | null;
  isArchived: boolean;
  /** Package balances plus loose stock. */
  total: Quantity;
  packageCount: number;
  loose: Quantity;
  packages: { view: MedicationPackage; activeLoanId: string | null }[];
  refillPolicy: RefillPolicy | null;
};

export type TreatmentKind = 'Scheduled' | 'AsNeeded';
export type RecurrencePattern = 'Daily' | 'SelectedWeekdays' | 'EveryNDays' | 'DayOfMonth' | 'EveryNMonths';

export type TreatmentPlan = {
  id: string;
  versionId: string;
  versionNumber: number;
  personId: string;
  medicationDefinitionId: string;
  dose: Quantity;
  kind: TreatmentKind;
  pattern: RecurrencePattern;
  weekdayMask: number | null;
  intervalDays: number | null;
  dayOfMonth: number | null;
  intervalMonths: number | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  localTime: string | null;
  timeZoneId: string;
  dayPeriod: string | null;
  mealRelation: string | null;
  minimumIntervalMinutes: number | null;
  instructions: string | null;
  /** Set aside for now. The plan stays listed so it can be picked up again. */
  isPaused: boolean;
};

export type Workspace = {
  householdId: string;
  people: Person[];
  medications: MedicationDefinition[];
  plans: TreatmentPlan[];
};

export type AdministrationOutcome = 'Taken' | 'Skipped' | 'PartialDose' | 'ExtraDose';

/**
 * One "do not take with" warning on a dose row: the other medicine, the household's own
 * words that matched, and whose tag it came from. Never a block; see ADR 0016.
 */
export type DoseConflict = {
  medicationDefinitionId: string;
  medicationName: string;
  matched: string[];
  notedOnMedicationDefinitionId: string;
  notedOn: string;
};

export type DueDose = {
  planId: string;
  planVersionId: string;
  personId: string;
  medicationDefinitionId: string;
  dose: Quantity;
  kind: TreatmentKind;
  localTime: string | null;
  dayPeriod: string | null;
  mealRelation: string | null;
  /** The medicine's own safe-use notes, so the dose row can show them in place. */
  cautions: CautionNotes | null;
  conflicts: DoseConflict[];
  /** The household's own minimum gap between doses, in minutes. Advisory, never a block. */
  minimumIntervalMinutes: number | null;
  lastTakenAt: string | null;
  /** lastTakenAt plus the gap. Whether it has passed is for the screen to decide. */
  nextDoseAllowedFrom: string | null;
  scheduledFor: string | null;
  availableTotal: Quantity;
  /** Lets the interface warn before the user taps, without mentioning packages. */
  hasEnoughStock: boolean;
  recordedOutcome: AdministrationOutcome | null;
  recordedAdministrationId: string | null;
};

export type Today = {
  date: string;
  due: DueDose[];
};

/** Where a dose came from. `Automatic` is the default and the only one shown by default. */
export type DoseSource = 'Automatic' | 'SpecificPackage' | 'LooseStock' | 'UntrackedExternal';

export type RecordedAllocation = {
  allocationId: string;
  packageId: string | null;
  /** The friendly ordinal, so the interface can say "Box 3". */
  packageLabel: number | null;
  quantity: Quantity;
};

export type RecordedDose = {
  administrationEventId: string;
  replayed: boolean;
  stockSource: 'TrackedInventory' | 'UntrackedExternal' | 'NotApplicable';
  allocations: RecordedAllocation[];
};

export type AllocationDetail = {
  administrationId: string;
  stockSource: string;
  actualQuantity: Quantity | null;
  allocations: {
    id: string;
    packageId: string | null;
    packageLabel: number | null;
    quantity: Quantity;
    /** False once a correction moved this draw elsewhere; the row is kept for history. */
    isActive: boolean;
    supersededByAllocationId: string | null;
  }[];
  corrections: {
    id: string;
    fromPackageLabel: number | null;
    toPackageLabel: number | null;
    quantity: Quantity;
    reason: string | null;
    recordedAt: string;
  }[];
};

export type Forecast = {
  medicationDefinitionId: string;
  balance: Quantity;
  /** False when nothing consumes this medication on a schedule. */
  isForecastable: boolean;
  projectedDepletionOn: string | null;
  daysOfStockRemaining: number | null;
  isLowStock: boolean;
  lowStockReason: 'None' | 'BelowThreshold' | 'WithinDayHorizon' | 'AlreadyDepleted';
  nextEligibleRefillOn: string | null;
  /** True when stock runs out before the prescription may be refilled. */
  hasRefillGap: boolean;
  refillGapDays: number | null;
  /** False when no plan exists to compute a suggestion from. */
  canSuggest: boolean;
  /** Covered stock only, an as-needed plan counted as one dose a day. */
  suggestedNextEligibleRefillOn: string | null;
  /** All stock, the same assumption. */
  suggestedDepletionOn: string | null;
  coveredBalance: Quantity;
  expectedDepletionOn: string | null;
};

/**
 * What happened to one person's doses of one medication over a period.
 *
 * Counts describe recorded behaviour and nothing more. `onScheduleRatio` is an exact
 * pair rather than a percentage so the interface chooses the rounding, and it is null
 * when nothing was scheduled — which is not the same as nought.
 */
export type AdherenceTally = {
  scheduledDoses: number;
  recordedSlots: number;
  onScheduleDoses: number;
  missedDoses: number;
  taken: number;
  skipped: number;
  partialDoses: number;
  extraDoses: number;
  recordedDoses: number;
  onScheduleRatio: { numerator: number; denominator: number } | null;
};

export type AdherenceReport = {
  from: string;
  to: string;
  timeZoneId: string;
  rows: {
    personId: string;
    medicationDefinitionId: string;
    tally: AdherenceTally;
  }[];
  total: AdherenceTally;
  /** Non-empty only when a plan's stored time zone is missing from the server. */
  unknownTimeZoneIds: string[];
};

export type InventoryReport = {
  asOf: string;
  rows: {
    medicationDefinitionId: string;
    name: string;
    strength: string | null;
    unit: string;
    isArchived: boolean;
    total: Quantity;
    packageCount: number;
    isForecastable: boolean;
    projectedDepletionOn: string | null;
    daysOfStockRemaining: number | null;
    isLowStock: boolean;
    lowStockReason: 'None' | 'BelowThreshold' | 'WithinDayHorizon' | 'AlreadyDepleted';
    nextEligibleRefillOn: string | null;
    hasRefillGap: boolean;
    refillGapDays: number | null;
  }[];
  lowStockCount: number;
  refillGapCount: number;
};

/**
 * One counted target inside an accepted count.
 *
 * `before` is what the ledger projected at the moment the count was accepted, so a
 * revision measures against the balance the count it corrects left behind — not
 * against the original.
 */
export type CountSessionLine = {
  id: string;
  medicationDefinitionId: string;
  packageId: string | null;
  /** The friendly ordinal, so the interface can say "Box 3". */
  packageLabel: number | null;
  before: Quantity;
  observed: Quantity;
  adjustment: Quantity;
};

export type CountSession = {
  id: string;
  revisionNumber: number;
  previousBatchId: string | null;
  acceptedAt: string;
  accountId: string;
  /** False once a later revision superseded it; the chain stays linear. */
  isRevisable: boolean;
  lines: CountSessionLine[];
};

export type Activity = {
  inventory: {
    id: string;
    medicationDefinitionId: string;
    entryType: string;
    packageLabel: number | null;
    quantity: Quantity;
    occurredAt: string;
    recordedAt: string;
    actorAccountId: string | null;
    reason: string | null;
  }[];
  administrations: {
    id: string;
    personId: string;
    medicationDefinitionId: string;
    outcome: AdministrationOutcome;
    stockSource: string;
    actualQuantity: Quantity | null;
    scheduledFor: string | null;
    occurredAt: string;
    recordedAt: string;
    latenessMinutes: number | null;
    actorAccountId: string;
  }[];
  allocationCorrections: {
    id: string;
    administrationEventId: string;
    fromPackageLabel: number | null;
    toPackageLabel: number | null;
    quantity: Quantity;
    reason: string | null;
    actorAccountId: string;
    recordedAt: string;
  }[];
};
