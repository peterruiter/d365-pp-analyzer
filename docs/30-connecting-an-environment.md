# Connecting an environment

There are three ways in, and they reach different amounts. Pick the one you can actually get
approved this week, not the one that reaches most.

## Application user, read only

An Entra app registration added to the environment as an application user with a read-only
security role. This is the mode for anything repeatable.

**Reaches:** the Dataverse Web API in full, an exported solution in full, the Power Apps
checker in full, and runtime evidence partially.

**Needs:** a tenant ID, a client ID, an environment URL, and a client secret. The secret goes
into Key Vault and is referenced by name. No credential is ever stored in the product
database.

The role definition ships with the product, so a client's security team reviews a file rather
than a description.

Flow run history and plug-in trace logs need more than a plain reader. Where that privilege
is absent, the product names the rules that went unassessed rather than reporting them clean.

## Delegated user

You, signed in, reading what you can already read.

**Reaches:** everything, including runtime evidence, to the extent your own account can reach
it.

**Needs:** nothing to be created. This is the fastest way to see something real.

The catch is that it is not repeatable: a scheduled run cannot borrow your session, and the
results depend on your privileges rather than on a declared role.

## Solution file

An exported unmanaged solution, unpacked and read offline.

**Reaches:** the solution file in full and the checker in full. Metadata partially. Runtime
not at all.

**Needs:** a `.zip` and nothing else. No connection, no credential, no security review.

This is not a degraded fallback. It is the mode that gets past a security review in week one
while the service principal request sits in a queue, and for a quality and debt assessment it
reaches most of what matters. What it cannot see is usage: which flows actually run, which
workflows are dormant, how often anything fails.

## Environment role

Whichever mode you use, you declare what the environment is **for**: development, test,
acceptance or production.

Several rules only fire against production. Guessing wrong makes a report either alarmist or
useless, so this is declared rather than inferred from the environment's name. If you leave
it as unknown, the production-scoped rules report themselves as not assessed — they do not
quietly assume.

## Testing a connection

Test it before you run anything. The test reports the identity it authenticated as and what
it could reach, per evidence source.

A connection that succeeds with too few privileges fails later in a way that looks exactly
like an estate with nothing in it. The reach panel is there so you find out now.
