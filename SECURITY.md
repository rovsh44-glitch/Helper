# Security Policy

## Scope

Use this document for vulnerability reporting and security-boundary expectations.

Repository facts:

- local secrets must not be committed;
- this public repo is showcase-only and does not contain the full private implementation;
- implementation-level security details stay in the private core and are not mirrored here;
- public proof bundles are published separately from this repository.

## Reporting Rules

- Do not post exploit details, credentials, private endpoints, or sensitive logs in public issues.
- Do not attach customer data or personal data.
- If no dedicated private mailbox is published yet, first request a private disclosure channel from the maintainer without including exploit detail.
- Use the smallest reproducible description necessary until a private route is established.

## What To Include

- affected component or file
- impact summary
- reproduction conditions
- expected versus actual behavior
- any mitigation already known

## What Is Not Promised

- no public bug-bounty program is claimed here
- no response-time SLA is promised here
- no guarantee is made that every finding can be fixed immediately

## Operational Reminder

Before any public release, re-check the showcase surface boundary and perform a manual disclosure review so private-core details do not leak into the public branch.
