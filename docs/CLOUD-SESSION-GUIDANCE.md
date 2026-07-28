# Working productively in a Claude Code cloud session

Project-agnostic. Copy this into any repo you drive from the cloud.

A cloud session is a disposable Linux container with a **policy-restricted network**
and **no hardware**. Most lost time comes from discovering that late. Spend the
first three minutes proving what you have, then plan around it.

---

## 1. Probe before you plan

Run this first. Every line answers a question that otherwise costs an hour.

```bash
# Am I in the cloud at all?
echo "${CLAUDE_CODE_REMOTE:-local}"

# Toolchains actually present (not what the README claims)
for t in dotnet node python3 java go cargo docker; do
  printf '%-8s %s\n' "$t" "$(command -v $t || echo MISSING)"; done

# Hardware virtualization -> can I run an emulator/VM? Almost always no.
ls /dev/kvm 2>/dev/null || echo "no KVM"
grep -qom1 'vmx\|svm' /proc/cpuinfo || echo "no CPU virt flags"

# Disk. The allowance is fixed; "Avail" hitting 0 with low "Used" means spent, not broken.
df -h / | tail -1
```

**Interpretation:** no `/dev/kvm` *and* no `vmx`/`svm` means no Android emulator, no
nested VM, no hardware-accelerated anything. That is not fixable from inside.

---

## 2. The network is an allowlist, and it fails in a confusing way

Outbound HTTPS goes through a policy proxy. Blocked hosts return **403** or a bare
connection failure — *not* a DNS error, which is why it reads like a broken tool.

**Probe the hosts you actually need**, before writing a setup script around them:

```bash
for u in https://registry.npmjs.org https://pypi.org https://api.github.com \
         https://builds.dotnet.microsoft.com https://dl.google.com ; do
  printf '%-45s %s\n' "$u" "$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 "$u" 2>/dev/null)"
done
```

`000` or `403` = blocked by policy.

### The redirect trap — this one is genuinely sneaky

A host can be reachable while the thing it *redirects to* is blocked. Real example:
`dot.net/v1/dotnet-install.sh` returns **301** (fine), redirects to
`builds.dotnet.microsoft.com` (**blocked**), and `curl -fsSL` reports a 403 that
looks like it came from the first host. **Always probe the final CDN, not the
vanity URL.**

### Rules

- **Never route around a 403.** If host A is blocked and host B (allowed) serves the
  same payload, fetching from B is circumventing the policy, not a clever fix.
  Report the blocked host and ask.
- **Never disable TLS verification and never unset `HTTPS_PROXY`.** A cert error
  means a tool isn't reading the CA bundle; point that tool at it instead.
- Check the platform's own diagnostics before theorising. There is usually a
  README next to the CA bundle and a status endpoint that names the real reason.

---

## 3. Setup scripts run at container **creation** — not on resume

The single highest-value fact in this document.

- Editing the environment's Setup script does **nothing** to a container that is
  already running. Resuming reuses the old container and skips provisioning.
- To pick up a changed setup script you must start a **brand-new session**.
  "Continue" or "resume" will not do it.
- Verify what actually happened rather than trusting the absence of errors —
  the environment manager logs whether an init script even exists, and whether
  the session was a fresh start or a resume.

### Make setup scripts fail loudly

Most templates use `set -u` (not `-e`) and end every install with `|| true`, so the
script **exits 0 having installed nothing**. Add a verification tail:

```bash
fail=""
command -v dotnet >/dev/null || fail="$fail dotnet"
[ -d /opt/android-sdk/platforms ] || fail="$fail android-sdk"
[ -n "$fail" ] && echo "::SETUP INCOMPLETE::$fail"
exit 0   # still 0: a non-zero exit can block the session from starting
```

Now a broken environment announces itself in the first thirty seconds.

---

## 4. When you cannot build locally, make CI the compiler

This is the workaround that rescues an otherwise blocked session. CI runners have
open network and full toolchains.

- Add a workflow triggered on push to your working branch. Every push becomes a
  type-check, and for mobile/desktop apps, a real installable artifact.
- `workflow_dispatch` alone is **not** enough: a workflow is only dispatchable by
  hand once it exists on the **default branch**. On a feature branch, use a `push`
  trigger.
- Upload build output with `actions/upload-artifact` so you can download and test
  on real hardware yourself.
- Push small and often. Each push is one compile; a 400-line commit that fails
  tells you far less than four 100-line ones.

**CI can also run hardware you don't have.** GitHub's Linux runners expose KVM, so
an Android emulator runs there even though it cannot run in a cloud session. That
converts "unverifiable" into "smoke-tested" for a whole class of runtime failures
(missing provider config, startup crashes, navigation faults) that no compiler catches.

---

## 5. State what kind of verification actually happened

The most damaging habit is letting "CI is green" stand in for "it works." Use three
distinct claims and never blur them:

| Claim | Means |
|---|---|
| **Compiles** | The build succeeded. Nothing was executed. |
| **Harness-verified** | Code ran and asserted real values, in a non-target environment. |
| **Device-verified** | It ran on the real target hardware. |

Put the level in the **commit message**, not just the chat. Anything you could not
verify goes in a written list in the repo, phrased as what a human must do.

Assume a bug exists that only appears on real hardware. Most mature projects have
one documented case where every harness passed and the device still failed.

---

## 6. Parallel agents: contracts first, disjoint files always

Parallelism helps only when the work is genuinely siloed.

- **Write the shared types/interfaces yourself, first, and commit them.** Agents
  that each invent their own type shapes produce code that cannot link — and with
  no local compiler you will not find out until CI.
- **One owner per file.** Name the exact files each agent may write in its prompt.
- **Shared files (routing, DI, manifests, docs) belong to the orchestrator.** Have
  agents *report* the wiring they need; apply it in one pass.
- **Never `git add -A` while agents are running.** You will commit half-written
  files. Stage explicit paths, or wait for the agent to report.
- **Verify self-reports before acting on them.** Agents are confidently wrong often
  enough that a 30-second check pays for itself. Re-read the diff; re-run the claim.
  Ask for findings split into *confirmed* vs *suspected*, and for "I found nothing"
  to be an acceptable answer — otherwise you get invented findings as padding.
- A refuted hypothesis is a **good** result. Give agents falsifiable questions
  rather than conclusions to confirm.

---

## 7. Nothing survives the container

- Commit and push after **every** logical unit, not at session end. Usage limits
  and restarts kill sessions mid-run; anything unpushed is gone.
- Untracked local files do not exist in the cloud. Anything gitignored — real test
  data, local configs, credentials — is simply absent. Plan tasks that don't need it.
- Use the session scratchpad for temp files, never the repo. Extract large or
  sensitive artifacts to scratch with `git show`, never into the working tree.

---

## 8. Repo integrity, especially with agents running

Run before every push into a valuable repository:

```bash
# Binaries and secrets that must never be tracked
git ls-files -- '*.keystore' '*.jks' '*.p12' '*.pem' '*.key' '*.glb' '*.zip' | head

# Did a heavy branch get merged? Its blobs are then permanent.
git merge-base --is-ancestor origin/<heavy-branch> HEAD && echo "MERGED - BAD"

git status --short   # anything unexpected staged?
```

**Merging a branch that carries large binaries is irreversible.** Once such a commit
is an ancestor, `git rm` does not remove the blobs from history. To adopt code from
such a branch, **port it by reading** (`git show branch:path`) and committing fresh.

---

## 9. Boundaries worth holding

- Do exactly the task asked. If it's blocked, say so and deliver everything else.
- Never generate or handle signing keys, passwords, or tokens on the user's behalf.
  Write the commands **they** should run, and read the values from secrets.
- Give unattended work an explicit stop condition: same error three times →
  escalate once → log it as blocked → stop. Never loop.
- If a hypothesis is disproven, say so plainly. A wrong theory that gets corrected
  costs minutes; one that gets quietly confirmed costs the whole investigation.
