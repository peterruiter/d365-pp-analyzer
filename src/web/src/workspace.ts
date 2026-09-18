/** One piece of client work. */
export type Engagement = {
  engagementId: string;
  name: string;
  clientName: string | null;
  status: string;
  accessRole: string;
  createdUtc: string;
};

/** A configured way into one system. */
export type Connection = {
  sourceConnectionId: string;
  connectorId: string;
  name: string;
  direction: 'source' | 'target';
  lastTestedUtc: string | null;
  lastTestSucceeded: boolean | null;
  lastTestMessage: string | null;
};

/** One field on a connection form, described by the connector contract. */
export type ConnectorSetting = {
  name: string;
  type: string;
  required: boolean;
  default: string | null;
  choices: string[];
  description: string;
  isSecret: boolean;
};

/** One person admitted to the product. */
export type AdmittedUser = {
  userId: string;
  displayName: string;
  email: string | null;
  isGlobalAdmin: boolean;
  createdUtc: string;
  createdBy: string;
  /**
   * What they can reach. Sent with the user rather than fetched per row, because a
   * deployment with forty people would otherwise make forty requests to draw one screen.
   */
  engagementAccess: EngagementGrant[];
};

/** One engagement somebody has been given, and what they may do there. */
export type EngagementGrant = {
  engagementId: string;
  engagementName: string;
  role: string;
};

/** What one source system will give up, per canonical entity. */
export type ConnectorCapability = {
  id: string;
  name: string;
  status: string;
  deployment: string;
  description: string;
  verifiedAgainst: string | null;
  discovery: Record<string, string>;
  discoveryNotes: Record<string, string>;
  settings: ConnectorSetting[];
};

/** What happened the last time a connection's credentials were used. */
export type ConnectionTestResult = {
  succeeded: boolean;
  identity: string | null;
  message: string;
};

/** How one canonical entity is written to Dynamics, and how sure we are of the names. */
export type TargetMapping = {
  canonical: string;
  fidelity: string;
  verification: string;
  table: string | null;
  note: string | null;
  hasUnverifiedNames: boolean;
};

/** What was found for one entity, and what it will cost. */
export type EntityAssessment = {
  canonicalEntityId: string;
  name: string;
  domain: string;
  recordCount: number;
  sourceLevel: string;
  targetFidelity: string;
  effectiveFidelity: string;
  effortLowHours: number;
  effortHighHours: number;
  neverSupplied: string[];
  partiallySupplied: string[];
};

/** Something that changes the shape of a project rather than its size. */
export type Risk = {
  id: string;
  severity: string;
  detail: string;
  consequence: string;
};

/** What a discovery adds up to. */
export type Assessment = {
  totalRecords: number;
  entities: EntityAssessment[];
  risks: Risk[];
  totalLowHours: number;
  totalHighHours: number;
  targetVerified: boolean;
};

/** One record in a plan. */
export type PlanItem = {
  canonicalEntityId: string;
  sourceRecordId: string;
  displayName: string;
  outcome: string;
  table: string | null;
  changedColumns: string[];
  reason: string | null;
  isDeferredPass: boolean;
};

/** What an apply would change. */
export type Plan = {
  runId: string;
  environment: string;
  createdUtc: string;
  hash: string;
  canBeApproved: boolean;
  approvedBy: string | null;
  items: PlanItem[];
  creates: number;
  updates: number;
  unchanged: number;
  conflicts: number;
};

/** One pass through the pipeline. */
export type Run = {
  runId: string;
  mode: string;
  status: string;
  createdUtc: string;
  createdBy: string;
  startedUtc: string | null;
  completedUtc: string | null;
  error: string | null;
};

/** Something no tool can migrate, with the evidence. */
export type BacklogEntry = {
  canonicalEntityId: string;
  sourceRecordId: string | null;
  displayName: string;
  reason: string;
  evidence: string | null;
};

/**
 * Reads JSON from the API, turning a failure into something a page can render.
 *
 * Every screen in this product can be looking at a client's real engagement, and a blank
 * panel with no explanation is worse than a sentence saying what went wrong.
 */
export async function getJson<T>(path: string): Promise<{ data: T | null; error: string | null }> {
  try {
    const response = await fetch(path, { headers: { Accept: 'application/json' } });

    if (response.status === 401) {
      // The session expired while the page was open. Reloading sends the reader back
      // through sign-in rather than leaving them looking at an empty screen.
      window.location.reload();
      return { data: null, error: null };
    }

    if (!response.ok) {
      return { data: null, error: `${response.status} ${response.statusText}` };
    }

    return { data: (await response.json()) as T, error: null };
  } catch (failure) {
    return { data: null, error: failure instanceof Error ? failure.message : String(failure) };
  }
}

/** Sends JSON to the API. */
export async function sendJson<T>(
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: unknown
): Promise<{ data: T | null; error: string | null }> {
  try {
    const response = await fetch(path, {
      method,
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body)
    });

    if (!response.ok) {
      const text = await response.text();

      // The API refuses with {"error": "..."} written for a consultant to read. Showing the
      // raw JSON instead would put braces and quotes in front of somebody who needs to know
      // that their client secret is missing.
      try {
        const parsed = JSON.parse(text) as { error?: string };
        if (parsed.error) return { data: null, error: parsed.error };
      } catch {
        // Not JSON. The text itself is the best thing there is to show.
      }

      return { data: null, error: text || `${response.status} ${response.statusText}` };
    }

    const text = await response.text();
    return { data: text ? (JSON.parse(text) as T) : null, error: null };
  } catch (failure) {
    return { data: null, error: failure instanceof Error ? failure.message : String(failure) };
  }
}

/** Formats a range the way the whole product does: two numbers, never one. */
export function range(low: number, high: number): string {
  return `${Math.round(low)} to ${Math.round(high)}`;
}

/** Formats an instant in the reader's own locale, or says never. */
export function when(value: string | null, culture: string, never: string): string {
  if (!value) return never;
  return new Date(value).toLocaleString(culture, { dateStyle: 'medium', timeStyle: 'short' });
}
