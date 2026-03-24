# Security Policy

## Scope

Use this document for vulnerability reporting and security-boundary expectations.

Repository facts:

- runtime data is expected to live outside the repository under `HELPER_DATA_ROOT`;
- local secrets must not be committed;
- `searxng/settings.yml` is operator-local and should stay out of normal tracking;
- auth bootstrap and browser/API boundaries are documented in the canonical docs under `doc/security/` and `doc/adr/`.

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

Before any public release, rerun the repo hygiene and secret-scan gates and perform a manual disclosure review.
