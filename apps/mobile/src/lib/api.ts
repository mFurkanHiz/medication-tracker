import type { Quantity } from './quantity';

/**
 * The server contract, as the device sees it.
 *
 * Every call carries the bearer token the mobile sign-in returns, plus the custom
 * client header the API requires on mutations.
 */

export const DEFAULT_API_URL =
  process.env.EXPO_PUBLIC_API_URL || 'https://medicationtracker.rapidconfigs.com';

/** A refusal the server expressed as a stable code the client can translate. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
  ) {
    super(`${status} ${code}`);
    this.name = 'ApiError';
  }

  /**
   * True when the server made a decision and will make the same one again.
   *
   * A refused command must leave the outbox — retrying it forever would be a queue
   * that never drains. A network failure or a server fault is the opposite: the
   * command has not been decided and must be retried.
   */
  get isFinal(): boolean {
    return this.status >= 400 && this.status < 500 && this.status !== 408 && this.status !== 429;
  }
}

/** The network itself failed, so the command's fate is unknown and it must be retried. */
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('network_unavailable');
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

export type ApiQuantity = Quantity & { display: string };

export type SessionResponse = {
  accountId: string;
  households: { id: string; name: string }[];
};

export type AuthResponse = {
  accountId: string;
  householdId?: string;
  accessToken: string | null;
};

export type DueDose = {
  planId: string;
  planVersionId: string;
  personId: string;
  medicationDefinitionId: string;
  dose: ApiQuantity;
  kind: 'Scheduled' | 'AsNeeded';
  localTime: string | null;
  dayPeriod: string | null;
  mealRelation: string | null;
  scheduledFor: string | null;
  availableTotal: ApiQuantity;
  hasEnoughStock: boolean;
  recordedOutcome: 'Taken' | 'Skipped' | 'PartialDose' | 'ExtraDose' | null;
  recordedAdministrationId: string | null;
};

export type TodayResponse = {
  date: string;
  due: DueDose[];
};

export type WorkspacePackage = {
  id: string;
  ordinal: number;
  state: 'Sealed' | 'Opened' | 'Disposed' | 'Lost' | 'Archived';
  isEmpty: boolean;
  nominalCapacity: ApiQuantity;
  remaining: ApiQuantity;
  unit: string;
  ownerPersonId: string | null;
  holderPersonId: string | null;
  isPinned: boolean;
};

export type WorkspaceResponse = {
  householdId: string;
  people: { id: string; name: string; isArchived: boolean }[];
  medications: {
    id: string;
    name: string;
    strength: string | null;
    unit: string;
    isArchived: boolean;
    total: ApiQuantity;
    packageCount: number;
    loose: ApiQuantity;
    packages: { view: WorkspacePackage; activeLoanId: string | null }[];
  }[];
  plans: {
    id: string;
    versionId: string;
    personId: string;
    medicationDefinitionId: string;
    dose: ApiQuantity;
    kind: 'Scheduled' | 'AsNeeded';
    pattern: 'Daily' | 'SelectedWeekdays' | 'EveryNDays';
    weekdayMask: number | null;
    intervalDays: number | null;
    effectiveFrom: string | null;
    effectiveTo: string | null;
    localTime: string | null;
    timeZoneId: string;
    dayPeriod: string | null;
    /** Set aside by the household. The server still lists it, so it can be resumed. */
    isPaused: boolean;
  }[];
};

export type DoseSource = 'Automatic' | 'SpecificPackage' | 'LooseStock' | 'UntrackedExternal';

export type RecordDoseRequest = {
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
  /** Written to the outbox before the request, so a replay returns the same event. */
  idempotencyKey: string;
  note?: string | null;
};

export type RecordedDoseResponse = {
  administrationEventId: string;
  replayed: boolean;
  stockSource: 'TrackedInventory' | 'UntrackedExternal' | 'NotApplicable';
  allocations: {
    allocationId: string;
    packageId: string | null;
    packageLabel: number | null;
    quantity: ApiQuantity;
  }[];
};

export type ApiConfig = {
  apiUrl: string;
  accessToken: string;
};

const CLIENT_HEADER = 'X-Medication-Client';

async function request<T>(
  config: ApiConfig,
  path: string,
  init?: { method?: string; body?: unknown },
): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${config.apiUrl}/api${path}`, {
      method: init?.method ?? 'GET',
      headers: {
        Authorization: `Bearer ${config.accessToken}`,
        [CLIENT_HEADER]: '1',
        ...(init?.body === undefined ? {} : { 'Content-Type': 'application/json' }),
      },
      body: init?.body === undefined ? undefined : JSON.stringify(init.body),
    });
  } catch (caught) {
    // Reaching here means the request never got a verdict, so the caller must retry.
    throw new NetworkError(caught);
  }

  if (!response.ok) {
    throw new ApiError(response.status, await refusalCode(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text === '' ? undefined : JSON.parse(text)) as T;
}

async function refusalCode(response: Response): Promise<string> {
  if (response.status === 401) {
    return 'unauthenticated';
  }

  if (response.status === 403) {
    return 'forbidden';
  }

  try {
    const body = (await response.json()) as {
      code?: string;
      error?: string;
      errors?: Record<string, string[]>;
    };

    if (body.code) {
      return body.code;
    }

    if (body.error) {
      return body.error;
    }

    if (body.errors) {
      const first = Object.values(body.errors)[0];
      if (first?.[0]) {
        return first[0];
      }
    }
  } catch {
    // A refusal with no JSON body still has its status.
  }

  return 'request_failed';
}

export const api = {
  /** Sign-in and registration need no bearer token, and ask for one back. */
  async authenticate(
    apiUrl: string,
    mode: 'login' | 'register',
    email: string,
    password: string,
    confirmPassword?: string,
  ): Promise<AuthResponse> {
    let response: Response;

    try {
      response = await fetch(`${apiUrl}/api/auth/${mode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', [CLIENT_HEADER]: '1' },
        body: JSON.stringify({
          email,
          password,
          // Asks the server to return a bearer token instead of only setting a cookie.
          mobile: true,
          ...(mode === 'register' ? { confirmPassword } : {}),
        }),
      });
    } catch (caught) {
      throw new NetworkError(caught);
    }

    if (!response.ok) {
      throw new ApiError(response.status, await refusalCode(response));
    }

    return (await response.json()) as AuthResponse;
  },

  session: (config: ApiConfig) => request<SessionResponse>(config, '/auth/session'),

  signOut: (config: ApiConfig) => request<void>(config, '/auth/logout', { method: 'POST' }),

  workspace: (config: ApiConfig, household: string) =>
    request<WorkspaceResponse>(config, `/households/${household}/workspace`),

  today: (config: ApiConfig, household: string, date?: string) =>
    request<TodayResponse>(config, `/households/${household}/today${date ? `?date=${date}` : ''}`),

  recordDose: (config: ApiConfig, household: string, body: RecordDoseRequest) =>
    request<RecordedDoseResponse>(config, `/households/${household}/administrations`, {
      method: 'POST',
      body,
    }),
};
