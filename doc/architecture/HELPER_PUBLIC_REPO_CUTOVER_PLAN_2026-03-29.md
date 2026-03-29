# HELPER Public Repo Cutover Plan

Status: `active plan`
Updated: `2026-03-29`
Target repo: `https://github.com/rovsh44-glitch/Helper`

## Cutover Goal

Publish the existing GitHub repository URL only after the public tree is sanitized, the public history starts from a clean root commit, and remote publication surfaces no longer expose blocked historical product labels.

## Cutover Strategy

Use a fresh public history instead of exposing the private-core history directly.

Reason:

1. the private-core history contains internal-only material that does not belong in the public repo;
2. a clean root commit makes GitHub search, releases, and tags easier to audit;
3. the target URL can stay the same while the public branch history is replaced before the visibility switch.

## Branch Roles

1. `private/master-frozen-2026-03-29`: immutable recovery pointer for the private-core state.
2. `private/public-sanitize-2026-03-29`: staging branch for the exact public tree.
3. `public-main`: orphan branch that becomes the future default public branch.

## Execution Phases

### Phase 0. Backup Private State

1. create a frozen backup branch from current private work;
2. create a bundle backup of all refs;
3. create one offline mirror backup outside the working repo.

### Phase 1. Audit Remote Publication Surfaces

1. inventory the current GitHub default branch and all remote branches;
2. inventory remote tags;
3. inventory releases, release assets, packages, Actions artifacts, Pages, and any wiki/discussion surface;
4. classify each surface as `keep`, `replace`, or `delete`.

### Phase 2. Finalize The Sanitized Public Tree

1. complete zero-token cleanup in the sanitize branch;
2. run filename and content scans;
3. run build, tests, frontend build, and docs gates;
4. confirm that only public-safe docs and configs remain.

### Phase 3. Create Fresh Public History

1. switch to an orphan branch named `public-main` from the sanitized tree;
2. clear the index and add only sanitized public files;
3. create the first public-safe commit;
4. verify the orphan branch has exactly the intended public history.

### Phase 4. Replace The Remote Default Branch

1. push the orphan branch to the GitHub repository as the new default branch;
2. set `main` as the default branch if needed;
3. remove remote branches that should not become public;
4. keep private-only history only in the backups from phase 0.

### Phase 5. Reset Public Tags And Releases

1. remove remote tags that point into private history;
2. recreate public tags only from the new public branch;
3. delete old releases and release assets that were built from private history;
4. publish the first public release only after the cutover branch is final.

### Phase 6. Visibility Switch

1. confirm zero-token scans on the local public branch;
2. confirm GitHub branch, tag, and release surfaces are clean;
3. switch repository visibility from private to public;
4. run one final post-switch GitHub search check.

## Definition Of Done

1. the public working tree is helper-only;
2. the public default branch is rooted in a fresh public history;
3. remote branches, tags, and releases are public-safe;
4. post-switch GitHub search does not surface blocked historical product labels.
