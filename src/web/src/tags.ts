/**
 * Which tag variant each of the product's gradings wears.
 *
 * One table rather than one per screen, because a consultant reading Sources and then Target
 * is comparing two gradings, and "assisted" rendered green on one screen and amber on the
 * other would be the product disagreeing with itself in front of a client.
 */

/** How much of an entity a source system will give up. */
export const statusTag: Record<string, string> = {
  pending: 'muted',
  running: 'review',
  awaitingApproval: 'warning',

  // Waiting for a person, like awaitingApproval, and wearing the same colour for the same
  // reason: the product is not stuck, somebody is holding it, and amber is what says that
  // without reading as a fault.
  awaitingSelection: 'warning',
  succeeded: 'complete',

  // Not green. A run that reached three quarters of an estate and one that reached all of
  // it must not look the same on a screen, because the report from the first is missing
  // things nobody will notice.
  partial: 'warning',
  skipped: 'muted',
  failed: 'danger',
  cancelled: 'muted'
};

/** How much a risk changes the shape of a project. */
export const severityTag: Record<string, string> = {
  high: 'danger',
  medium: 'warning',
  low: 'muted'
};
