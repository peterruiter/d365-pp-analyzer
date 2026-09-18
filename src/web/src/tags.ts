/**
 * Which tag variant each of the product's gradings wears.
 *
 * One table rather than one per screen, because a consultant reading Sources and then Target
 * is comparing two gradings, and "assisted" rendered green on one screen and amber on the
 * other would be the product disagreeing with itself in front of a client.
 */

/** How much of an entity a source system will give up. */
export const levelTag: Record<string, string> = {
  Full: 'complete',
  Partial: 'warning',
  Derived: 'review',
  Manual: 'muted',
  None: 'danger'
};

/** How much of an entity can be written to Dynamics. */
export const fidelityTag: Record<string, string> = {
  Automatic: 'complete',
  Assisted: 'warning',
  Manual: 'muted',
  NotMigratable: 'danger'
};

/** Where a run has got to. */
export const statusTag: Record<string, string> = {
  pending: 'muted',
  running: 'review',
  awaitingApproval: 'warning',
  succeeded: 'complete',
  failed: 'danger',
  cancelled: 'muted'
};

/** How much a risk changes the shape of a project. */
export const severityTag: Record<string, string> = {
  high: 'danger',
  medium: 'warning',
  low: 'muted'
};
