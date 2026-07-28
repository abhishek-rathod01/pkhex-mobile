# Working on pkhex-mobile: local PC vs cloud session

<!-- Managed by claude-cloud-kit. Edit the kit, re-run generate.py, re-run bootstrap. -->

**Cloud viability: Partial - you can build and test in the cloud, but final verification needs your PC.**

Trunk branch: `master`

## Where am I running?

Check `$CLAUDE_CODE_REMOTE`. It is `true` in a Claude Code cloud session and
unset locally. Never assume; the two environments differ in ways that matter.

| | Local (Windows PC) | Cloud session |
|---|---|---|
| OS | Windows | Ubuntu 24.04, root |
| Paths | `C:\Users\abhis\...` | `/home/...`, POSIX |
| Resources | your machine | ~4 vCPU, 16 GB RAM, 30 GB disk |
| Untracked local files | present | **absent** - only what is committed |
| GUI / emulators / devices | available | **not available** |
| Network | open | proxied allowlist (Trusted by default) |

Any command in this repo's docs written with a Windows path or a PowerShell
idiom needs translating before it runs in a cloud session. Translate it; do not
guess that it will work.

## Do this in a cloud session

- CAPABILITY-GAPS.md display-only items (final stats, type chips, Hidden Power)
- locked-slot guard in PokemonSlotMover.MoveOrSwap
- doc cleanup (delete/supersede CAPABILITY-AUDIT.md), CI edits, PR review
- verify/GenN harnesses that use PKHeX.Core-generated saves

## Do NOT attempt this in a cloud session

- anything needing the 256 real save files (untracked, PC-only)
- on-device / emulator verification -- no KVM, no AVD in a cloud session
- confirming the drag-and-drop box move (needs a human on a real device)

If a task lands in the second list, say so and stop rather than producing an
unverifiable result. "The harness passed" is not the same claim as "it works".

## Session hygiene (applies everywhere)

- Push after every commit, not just at the end of a session.
- Sequential work by default. Parallel subagents each cold-start and re-read
  shared context, and that redundancy is paid for. Only parallelise genuinely
  siloed tasks.
- Give unattended runs an explicit stop condition: same error 3x -> escalate
  once -> log it as blocked and move on. Never loop.
- Record which kind of verification actually happened (harness vs. real
  hardware) in the commit message, not just that "tests pass".

## Read order when starting cold

1. `WAKEUP.md`
2. `CLAUDE.md` - contains Windows-specific paths; translate them in a cloud session
3. `PROGRESS.md` + `PROGRESS-gen{N}.md`
4. `CAPABILITY-GAPS.md` - the current priority map
5. `CAPABILITY-AUDIT.md` is **stale**. Ignore it; deleting or marking it
   superseded is itself an open housekeeping item.

Then run `git log --oneline` and `git branch -a` before assuming anything about
what has landed - a Pokedex feature and a `3d-models-experimental` branch may or
may not have completed.

## Hard boundaries

- PkhexMobile is a **UI wrapper**, not a reimplementation. PKHeX.Core owns all
  save parsing, validation, and generation-specific logic. Respect that line.
- **Never overwrite an original save.** All edits export to a new file.
- Generation-aware field caps are real bugs waiting to happen: Gen1/2 use
  0-15 DVs and 0-65535 stat exp; Gen3+ use 0-31 IVs and 0-252 EVs.
- Auto-legalisation is **out of scope**. It lives in a separate project
  (`architdate/PKHeX-Plugins`), not in PKHeX.Core. Repeatedly declined.
- Never let an agent hand-write synthetic save bytes. Use PKHeX.Core's own
  save-creation methods, or you are validating a guess with a guess.

## Local-only build facts (do not run these in the cloud)

- JDK 21 required. JDK 25 breaks this Android toolchain - check `JAVA_HOME` first
  when a build fails.
- Deploy with `dotnet build PkhexMobile/PkhexMobile.csproj -f net10.0-android -t:Run`.
  A raw `adb install` of a Debug APK crashes (SIGABRT, "No assemblies found")
  because Debug builds use Fast Deployment.
- AVD `PkhexMobile_Emulator`, Pixel 6, API 36, WHPX on Windows.
- There is a documented bug (Shell navigation `InvalidCastException` from passing
  a non-`IConvertible` through `GoToAsync`'s query dictionary) that appeared
  **only on device** while every harness test passed. Harness-passing is not
  proof.
