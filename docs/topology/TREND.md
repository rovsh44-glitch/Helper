# Findings trend

Numeric architecture-drift series across published snapshots (oldest first). Cycle stats are strongly-connected
groups of the RAW file-level import graph (before the page’s project-graph validation pass), computed with the
same iterative Tarjan the page uses — the method is held constant across snapshots, so deltas are comparable even
where the absolute count differs slightly from the on-page figure.

| Snapshot | Commit | Files | Project edges | File edges | SCC groups | Largest SCC | Files in cycles |
|---|---|---:|---:|---:|---:|---:|---:|
| 2026-08-20 05:21 UTC | `9669cc5ac796` | 2917 | 63 | 7955 | 18 | 307 | 359 |
| 2026-08-25 08:56 UTC | `a4a90b65e7e9` | 2969 | 63 | 8075 | 18 | 316 | 368 |
| 2026-08-27 14:40 UTC | `be26bacf06fb` | 2978 | 63 | 8137 | 18 | 317 | 369 |
| 2026-08-27 15:35 UTC | `86b2be3aff52` | 2978 | 63 | 8137 | 18 | 317 | 369 |

Latest vs first: files 2917 -> 2978, SCC groups 18 -> 18, files in cycles 359 -> 369.
