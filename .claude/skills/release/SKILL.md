---
name: release
description: Prepare and publish a RepoBoard release
disable-model-invocation: true
---
1. Pull `main` first (other sessions merge meanwhile). Summarize unreleased commits since the last tag. Propose the version
   number and wait for my OK.
2. Bump the version in `Directory.Build.props` and update CHANGELOG.md, keeping its line endings (no BOM). Group the new
   version's entries in the sections and order `docs/release.md` gives (Security, Added, Changed, Fixed, Internal). Leave
   out empty sections. In `docs/work-packages/README.md` set the release column of every package in it; in each of their
   protocols replace "noch ohne Release" in the head. Run `git diff --stat` to verify.
3. Build with `-warnaserror` and run the full test suite.
4. Create the PR using --body-file. Run `gh pr checks --watch` until green. Do NOT use --auto.
5. Merge, pull main, tag vX.Y.Z and push the tag. The tag starts `release.yml`: watch it (`gh run watch`) and check that
   the release is published (not a draft) with `Setup.exe`, the portable zip and `releases.win.json` – a failed run leaves
   every installed RepoBoard without the update.
6. Delete merged branches and worktrees (show the list first).
7. Count the work packages released since the last `docs/review-*.md` and name those whose protocol has no code review.
   Five or more packages, or two or more without a review: suggest `/complete-code-review` in the final message.
