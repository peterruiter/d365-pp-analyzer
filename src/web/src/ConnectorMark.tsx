/**
 * A small mark for a platform, next to its name.
 *
 * It draws a monogram rather than loading a logo. The vendor marks lived in the microsite,
 * which is not in this repository, and a broken image beside a client's platform name reads
 * as a product that does not know what it is talking about. Two letters from the name always
 * render, need no asset and cannot 404.
 *
 * The colour is derived from the identifier rather than chosen per platform, so adding a
 * connector to the contract does not also mean adding it to a lookup table here, and the same
 * platform is the same colour on every screen and in every session.
 */
export function ConnectorMark({ connectorId, name }: { connectorId: string; name: string }) {
  const initials = name
    .split(/[\s-]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('') || '?';

  let hash = 0;
  for (let index = 0; index < connectorId.length; index += 1) {
    hash = (hash * 31 + connectorId.charCodeAt(index)) % 360;
  }

  return (
    <span
      className="connector-mark"
      style={{ background: `hsl(${hash} 42% 32%)` }}
      aria-hidden="true"
    >
      {initials}
    </span>
  );
}
