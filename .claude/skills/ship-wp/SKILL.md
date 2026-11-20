---
name: ship-wp
description: Implement a work package from the roadmap end to end, from plan to green CI
disable-model-invocation: true
---
1. Read the WP from the roadmap and write the plan. Wait for approval.
2. Implement core, UI, tests and docs (`CLAUDE.md` §6 "Fertig heißt").
3. Run E2E in a scratch instance (`docs/e2e-testing.md`): `--data-dir` in the scratchpad, test repositories made there
   (never fetch, pull, push or commit in the user's real repositories). CDP for state, real Windows clicks via
   `tools/e2e/Invoke-RealClick.ps1` for mouse interactions. Run `tools/e2e/Test-InjectedInput.ps1` first; if injected input
   is filtered (FACEIT), ask the user to click and read the effect via CDP.
4. Run `pwsh -NoProfile -File tools/linux/Test-Linux.ps1` (build and tests under Linux, like the CI's first job) and fix
   what fails there.
5. Push, open the PR and enable auto-fix.
6. Run `/branch-code-review` (separate agent, findings by severity, wait for approval, fix, second pass).
7. Push and watch CI to green.
