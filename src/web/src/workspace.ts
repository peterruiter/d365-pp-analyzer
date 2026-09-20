/** One piece of client work. */
export type Engagement = {
  engagementId: string;
  name: string;
  clientName: string | null;
  status: string;
  accessRole: string;
  createdUtc: string;

  /** The demonstration estate, which everybody can read and nobody can change. */
  isDemonstration?: boolean;

  /** Drives a multiplier on every estimate. */
  isRegulated?: boolean;

  /** The language the report is written in. */
  reportLanguage?: string;

  /** The language work items are written in, which is not always the same. */
  backlogLanguage?: string;
};

/** A configured way into one estate. */
export type Connection = {
  connectionId: string;
  mode: string;
  name: string;
  environmentRole: string;
  lastTestedUtc: string | null;
  lastTestSucceeded: boolean | null;
  lastTestMessage: string | null;
  direction: 'source' | 'target';

  /** What it points at. Never the credential, which stays in Key Vault. */
  settings: Record<string, string>;
  secretExpiresUtc: string | null;
};

/**
 * One way of reaching an estate, from the extraction sources contract.
 *
 * What a mode reaches decides which rules can run at all, so this travels with the mode
 * rather than being worked out per screen.
 */
export type ExtractionMode = {
  id: string;
  name: string;
  status: string;
  summary: string;
  settings: string[];
  needsSecret: boolean;

  /** clientCredentials, authorizationCode or none. Decides whether the wizard ends in a sign-in. */
  authType: string;
  reaches: Record<string, string>;
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

/**
 * What the latest finished analysis adds up to.
 *
 * A null runId is not an estate with nothing in it. It means nothing has been analysed yet,
 * and the overview draws a different screen for each, because they are different sentences.
 */
export type Assessment = {
  runId: string | null;
  componentCount: number;
  componentTypeCount: number;
  findingCount: number;
  notAssessedCount: number;
  ruleCount: number;
  lowCodeShare: number | null;
  totalLowHours: number;
  totalHighHours: number;
  topFindings: TopFinding[];
};

/** One of the worst few findings, for the panel under the figures. */
export type TopFinding = {
  id: string;
  severity: string;

  /** The rule's name. This used to be the component and the line below it the rule id. */
  title: string;

  /** Which component it is on, or that it is about the solution as a whole. */
  where: string;
};

/** One attempt to read one component type, and what became of it. */
export type EntityRead = {
  componentTypeId: string;
  evidenceSource: string;
  succeeded: boolean;
  recordCount: number | null;
  error: string | null;
};

/** One item of remediation, as the backlog builder produced it. */
export type BacklogItem = {
  backlogItemId: string;
  parentItemId: string | null;
  workItemType: string;
  title: string;
  acceptanceCriteria: string;
  testRequirement: string | null;
  priority: number;
  storyPoints: number | null;
  lowHours: number;
  highHours: number;
  deterministicKey: string;
};

/** One pass through the pipeline. */
export type Run = {
  runId: string;
  mode: string;
  writes: boolean;
  status: string;
  createdUtc: string;
  createdBy: string;
  startedUtc: string | null;
  completedUtc: string | null;
  error: string | null;
};

/** A run's backlog, as the API returns it. */
export type Backlog = {
  runId: string | null;
  items: BacklogItem[];

  /** Whether this exact backlog has been approved, and whether it has moved since. */
  approval?: { given: boolean; stale: boolean };
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
