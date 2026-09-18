import { createContext, useContext, type ReactNode } from 'react';

/**
 * What the reader may do on the engagement they are looking at.
 *
 * The server decides this and refuses anything it disagrees with. This exists so the screen
 * does not offer a button that is going to come back forbidden, which reads to a consultant
 * as the product being broken rather than as them not having been given the role.
 *
 * It is deliberately not a security boundary. Anybody can change what their browser believes;
 * nobody can change what the API enforces. Every rule here has a matching check on the
 * endpoint behind it, and the endpoint is the one that counts.
 */

/** The three levels, weakest first, matching `EngagementRoles` on the server. */
const ranks: Record<string, number> = { viewer: 1, contributor: 2, admin: 3 };

/** The least somebody must hold to do something. */
export type Role = 'Viewer' | 'Contributor' | 'Admin';

type Access = {
  /** Their role on the current engagement, or null when they have none. */
  role: string | null;
  /** Whether they reach every engagement regardless of the grants they hold. */
  isGlobalAdmin: boolean;
};

const AccessContext = createContext<Access>({ role: null, isGlobalAdmin: false });

export function AccessProvider(
  { role, isGlobalAdmin, children }: Access & { children: ReactNode }
) {
  return (
    <AccessContext.Provider value={{ role, isGlobalAdmin }}>
      {children}
    </AccessContext.Provider>
  );
}

/**
 * Whether the reader holds at least the role named.
 *
 * Ranked rather than compared, so asking for Contributor is satisfied by an Admin. Comparing
 * strings would mean every caller listing the roles that count and one of them forgetting
 * Admin, which is the bug that hides until somebody senior cannot press a button.
 */
export function useCan(role: Role): boolean {
  const access = useContext(AccessContext);

  if (access.isGlobalAdmin) return true;
  if (!access.role) return false;

  return (ranks[access.role.toLowerCase()] ?? 0) >= ranks[role.toLowerCase()];
}

/** Their role on the current engagement, for showing rather than deciding. */
export function useRole(): string | null {
  return useContext(AccessContext).role;
}
