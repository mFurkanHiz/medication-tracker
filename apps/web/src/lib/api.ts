import type {
  Activity,
  AdherenceReport,
  AllocationDetail,
  CountSession,
  DoseSource,
  Forecast,
  InventoryReport,
  RecordedDose,
  Session,
  Today,
  Workspace,
} from './types';

/**
 * One counted target. `packageId` is absent for the everyday medication-level count and
 * set only when an advanced user reconciles one physical box.
 */
export type CountLineInput = {
  medicationDefinitionId: string;
  observedNumerator: number;
  observedDenominator?: number;
  packageId?: string | null;
};

/**
 * The API rejects a mutation that does not carry this header. A cross-site form post
 * cannot set it, so it is a cheap CSRF defence alongside the strict-same-site cookie.
 */
const CLIENT_HEADER = { 'X-Medication-Client': '1' } as const;

/**
 * A refusal the server expressed as a stable code, so the interface can translate it
 * rather than displaying English prose from the server.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly field?: string,
  ) {
    super(`${status} ${code}`);
    this.name = 'ApiError';
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: {
      ...CLIENT_HEADER,
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw await toError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text === '' ? undefined : JSON.parse(text)) as T;
}

async function toError(response: Response): Promise<ApiError> {
  let code = 'request_failed';
  let field: string | undefined;

  try {
    const body = (await response.json()) as {
      code?: string;
      error?: string;
      errors?: Record<string, string[]>;
    };

    if (body.code) {
      code = body.code;
    } else if (body.error) {
      code = body.error;
    } else if (body.errors) {
      // Validation problems arrive keyed by field; surface the first one.
      const [firstField, messages] = Object.entries(body.errors)[0] ?? [];
      field = firstField;
      code = messages?.[0] ?? code;
    }
  } catch {
    // A response with no JSON body leaves the generic code in place.
  }

  if (response.status === 401) {
    code = 'unauthenticated';
  } else if (response.status === 403) {
    code = 'forbidden';
  }

  return new ApiError(response.status, code, field);
}

/**
 * The name the server gave the download, falling back to a sensible one.
 *
 * Only an unquoted or double-quoted `filename` is read, and anything with a path
 * separator is rejected: a filename is a hint from the server, and a download must not
 * be able to suggest a path to the user's browser.
 */
function filenameFrom(disposition: string | null): string {
  const fallback = 'medication-tracker-export.json';
  const match = disposition?.match(/filename\*?=(?:"([^"]+)"|([^;]+))/i);
  const name = (match?.[1] ?? match?.[2])?.trim();

  return name && !name.includes('/') && !name.includes('\\') ? name : fallback;
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });

const put = <T>(path: string, body: unknown) =>
  request<T>(path, { method: 'PUT', body: JSON.stringify(body) });

const del = <T>(path: string) => request<T>(path, { method: 'DELETE' });

export type MedicationDefinitionInput = {
  name: string;
  form: string;
  unit?: string;
  strength?: string | null;
  brand?: string | null;
  manufacturer?: string | null;
  activeIngredients?: string[];
  defaultPackageCapacityNumerator?: number | null;
  defaultPackageCapacityDenominator?: number | null;
  category?: string | null;
  tags?: string[];
  notes?: string | null;
  cautionDoNotTakeWith?: string | null;
  cautionFoodsToAvoid?: string | null;
  cautionThingsToDo?: string | null;
  cautionThingsToAvoid?: string | null;
  cautionWarning?: string | null;
  coverage?: string | null;
};

export type AddStockInput = {
  coverage?: string | null;
  capacityNumerator?: number | null;
  capacityDenominator?: number | null;
  fullPackages?: number;
  openedPackages?: { remainingNumerator: number; remainingDenominator?: number }[];
  looseNumerator?: number | null;
  looseDenominator?: number | null;
  ownerPersonId?: string | null;
  expiresOn?: string | null;
  acquiredOn?: string | null;
  lotNumber?: string | null;
  storageLocation?: string | null;
  note?: string | null;
};

/** Every detail of a box the household may change later. PUT replaces them all. */
export type UpdatePackageInput = {
  label?: string | null;
  coverage?: string | null;
  expiresOn?: string | null;
  acquiredOn?: string | null;
  lotNumber?: string | null;
  barcode?: string | null;
  source?: string | null;
  storageLocation?: string | null;
  note?: string | null;
};

export type PlanInput = {
  personId: string;
  medicationDefinitionId: string;
  doseNumerator: number;
  doseDenominator: number;
  timeZoneId: string;
  kind?: string;
  pattern?: string;
  weekdayMask?: number | null;
  intervalDays?: number | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  localTime?: string | null;
  dayPeriod?: string | null;
  mealRelation?: string | null;
  minimumIntervalMinutes?: number | null;
  instructions?: string | null;
};

export type RecordDoseInput = {
  planVersionId?: string | null;
  personId?: string | null;
  medicationDefinitionId?: string | null;
  outcome?: string;
  source?: DoseSource;
  packageId?: string | null;
  actualQuantityNumerator?: number | null;
  actualQuantityDenominator?: number | null;
  scheduledFor?: string | null;
  occurredAt?: string | null;
  idempotencyKey?: string | null;
  note?: string | null;
};

export const api = {
  session: () => request<Session>('/auth/session'),

  register: (email: string, password: string, confirmPassword: string) =>
    post<{ accountId: string; householdId: string }>('/auth/register', {
      email,
      password,
      confirmPassword,
    }),

  signIn: (email: string, password: string) =>
    post<{ accountId: string }>('/auth/login', { email, password }),

  signOut: () => post<void>('/auth/logout'),

  workspace: (household: string) => request<Workspace>(`/households/${household}/workspace`),

  activity: (household: string) => request<Activity>(`/households/${household}/activity`),

  today: (household: string, date?: string) =>
    request<Today>(`/households/${household}/today${date ? `?date=${date}` : ''}`),

  addPerson: (household: string, name: string) =>
    post<{ id: string }>(`/households/${household}/people`, { name }),

  renamePerson: (household: string, person: string, name: string) =>
    put<void>(`/households/${household}/people/${person}`, { name }),

  archivePerson: (household: string, person: string) =>
    del<void>(`/households/${household}/people/${person}`),

  createDefinition: (household: string, input: MedicationDefinitionInput) =>
    post<{ id: string }>(`/households/${household}/medication-definitions`, input),

  updateDefinition: (household: string, definition: string, input: MedicationDefinitionInput) =>
    put<void>(`/households/${household}/medication-definitions/${definition}`, input),

  archiveDefinition: (household: string, definition: string) =>
    del<void>(`/households/${household}/medication-definitions/${definition}`),

  restorePerson: (household: string, person: string) =>
    post<void>(`/households/${household}/people/${person}/restore`),

  restoreDefinition: (household: string, definition: string) =>
    post<void>(`/households/${household}/medication-definitions/${definition}/restore`),

  addStock: (household: string, definition: string, input: AddStockInput) =>
    post<{ packages: unknown[] }>(`/households/${household}/inventory/${definition}/stock`, input),

  pinPackage: (household: string, pkg: string) =>
    post<void>(`/households/${household}/inventory/packages/${pkg}/pin`),

  unpinPackage: (household: string, pkg: string) =>
    del<void>(`/households/${household}/inventory/packages/${pkg}/pin`),

  retirePackage: (household: string, pkg: string, state: string, reason?: string) =>
    post<void>(`/households/${household}/inventory/packages/${pkg}/retire`, { state, reason }),

  reinstatePackage: (household: string, pkg: string, reason?: string) =>
    post<void>(`/households/${household}/inventory/packages/${pkg}/reinstate`, { reason }),

  updatePackage: (household: string, pkg: string, input: UpdatePackageInput) =>
    put<void>(`/households/${household}/inventory/packages/${pkg}`, input),

  assignPackage: (household: string, pkg: string, personId: string | null) =>
    post<void>(`/households/${household}/inventory/packages/${pkg}/owner`, { personId }),

  lendPackage: (household: string, pkg: string, borrowerPersonId: string) =>
    post<{ id: string }>(`/households/${household}/inventory/packages/${pkg}/loans`, {
      borrowerPersonId,
    }),

  returnLoan: (household: string, loan: string) =>
    post<void>(`/households/${household}/inventory/loans/${loan}/return`),

  createPlan: (household: string, input: PlanInput) =>
    post<{ id: string; versionId: string }>(`/households/${household}/plans`, input),

  updatePlan: (household: string, plan: string, input: PlanInput) =>
    put<{ versionId: string; versionNumber: number }>(`/households/${household}/plans/${plan}`, input),

  setPlanPaused: (household: string, plan: string, isPaused: boolean) =>
    post<{ versionId: string; versionNumber: number; isPaused: boolean }>(
      `/households/${household}/plans/${plan}/paused`,
      { isPaused },
    ),

  endPlan: (household: string, plan: string, endsOn: string) =>
    post<{ versionId: string; versionNumber: number; effectiveTo: string }>(
      `/households/${household}/plans/${plan}/end`,
      { endsOn },
    ),

  restartPlan: (household: string, plan: string, startsOn: string) =>
    post<{ versionId: string; versionNumber: number; effectiveFrom: string }>(
      `/households/${household}/plans/${plan}/restart`,
      { startsOn },
    ),

  deletePlan: (household: string, plan: string) =>
    del<void>(`/households/${household}/plans/${plan}`),

  recordDose: (household: string, input: RecordDoseInput) =>
    post<RecordedDose>(`/households/${household}/administrations`, input),

  allocations: (household: string, administration: string) =>
    request<AllocationDetail>(`/households/${household}/administrations/${administration}/allocations`),

  /** Moves one allocation to the source the user says was really used. */
  correctAllocation: (
    household: string,
    administration: string,
    allocation: string,
    target: 'SpecificPackage' | 'LooseStock',
    packageId: string | null,
    reason?: string,
  ) =>
    post<{ allocationId: string; packageLabel: number | null }>(
      `/households/${household}/administrations/${administration}/allocations/${allocation}/correction`,
      { target, packageId, reason },
    ),

  forecast: (household: string, definition: string) =>
    request<Forecast>(`/households/${household}/medication-definitions/${definition}/forecast`),

  setRefillPolicy: (
    household: string,
    definition: string,
    input: {
      lowStockThresholdNumerator?: number | null;
      lowStockThresholdDenominator?: number | null;
      lowStockDays?: number | null;
      nextEligibleRefillOn?: string | null;
      expectedDepletionOn?: string | null;
      note?: string | null;
    },
  ) =>
    put<void>(`/households/${household}/medication-definitions/${definition}/refill-policy`, input),

  adherenceReport: (household: string, from: string, to: string, timeZoneId: string) =>
    request<AdherenceReport>(
      `/households/${household}/reports/adherence?from=${from}&to=${to}&timeZoneId=${encodeURIComponent(timeZoneId)}`,
    ),

  inventoryReport: (household: string) =>
    request<InventoryReport>(`/households/${household}/reports/inventory`),

  /**
   * Fetches the export and hands it to the browser as a download.
   *
   * Fetched rather than linked so a refusal arrives as an {@link ApiError} the interface
   * can translate, instead of navigating the user to a JSON error page.
   */
  exportHousehold: async (household: string) => {
    const response = await fetch(`/api/households/${household}/export`, {
      credentials: 'same-origin',
      headers: CLIENT_HEADER,
    });

    if (!response.ok) {
      throw await toError(response);
    }

    return {
      blob: await response.blob(),
      filename: filenameFrom(response.headers.get('Content-Disposition')),
    };
  },

  countSessions: (household: string) =>
    request<{ sessions: CountSession[] }>(`/households/${household}/inventory/count-sessions`),

  countStock: (
    household: string,
    idempotencyKey: string,
    lines: CountLineInput[],
    note?: string,
  ) =>
    post<{ batchId: string; revisionNumber: number }>(
      `/households/${household}/inventory/count-sessions`,
      { idempotencyKey, lines, note },
    ),

  /**
   * Corrects an accepted count by appending a revision to it.
   *
   * Never an edit: the original stays exactly as it was accepted, and only the newest
   * link in the chain may be revised, which the server refuses with `stale_revision`.
   */
  reviseCount: (
    household: string,
    batchId: string,
    idempotencyKey: string,
    lines: CountLineInput[],
    note?: string,
  ) =>
    post<{ batchId: string; revisionNumber: number }>(
      `/households/${household}/inventory/count-sessions/${batchId}/revisions`,
      { idempotencyKey, lines, note },
    ),
};
