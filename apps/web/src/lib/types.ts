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
  note: string | null;
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
  notes: string | null;
  isArchived: boolean;
  /** Package balances plus loose stock. */
  total: Quantity;
  packageCount: number;
  loose: Quantity;
  packages: { view: MedicationPackage; activeLoanId: string | null }[];
  refillPolicy: RefillPolicy | null;
};

export type TreatmentKind = 'Scheduled' | 'AsNeeded';
export type RecurrencePattern = 'Daily' | 'SelectedWeekdays' | 'EveryNDays';

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
  effectiveFrom: string | null;
  effectiveTo: string | null;
  localTime: string | null;
  timeZoneId: string;
  dayPeriod: string | null;
  mealRelation: string | null;
  minimumIntervalMinutes: number | null;
  instructions: string | null;
};

export type Workspace = {
  householdId: string;
  people: Person[];
  medications: MedicationDefinition[];
  plans: TreatmentPlan[];
};

export type AdministrationOutcome = 'Taken' | 'Skipped' | 'PartialDose' | 'ExtraDose';

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
