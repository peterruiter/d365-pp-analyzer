# Getting access

## Signing in

Sign in with your Microsoft work account. Any Microsoft work account, in any organisation:
you do not need to be in the same tenant as the product, and nobody has to invite you as a
guest first. That on its own gets you nothing.

That is deliberate. A consultancy's clients are not in the consultancy's tenant, and a
client who wants to read their own assessment should not need a second account to do it.

A tool that reads a client's entire solution estate is not one that everybody in the tenant
should be able to wander into, so being signed in and being admitted are two separate things.
Somebody who already holds the product has to admit you.

If you sign in and see a page saying you have not been admitted, it will name who to ask.

## What you can see once you are admitted

Every admitted person can read the **Demonstration estate**, always, without anybody granting
it. Everything else you see because somebody gave you a role on it.

## The three roles

Roles are per engagement, not global. You can be an Admin on one engagement and have nothing
on another.

| Role | Can |
|---|---|
| **Viewer** | Read everything on the engagement: inventory, findings, estimates, backlog, reports. |
| **Contributor** | Everything a Viewer can, plus configure connections, start runs, and adjust estimates. |
| **Admin** | Everything a Contributor can, plus grant access to other people and approve a backlog for publishing. |

Approving a backlog is deliberately an Admin action and deliberately separate from starting a
run. It is the gate between an assessment and somebody's Azure DevOps project.

## Global administrators

A global administrator sees every engagement in the product and can create new ones. This is
for whoever runs the product, not for whoever runs an engagement.

## Switching engagements

The engagement picker is at the top of the sidebar. Everything below it — overview,
inventory, findings, backlog, reports — is about the engagement named there. If the
navigation is greyed out, it is because no engagement is selected yet.
