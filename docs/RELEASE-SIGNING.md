# Release signing — commands you run, on your own machine

Claude does not create the keystore and never sees the passwords. Everything in
this file is for **you** to run locally. Nothing here should ever be committed.

There are two separate signing stories in this repo, and confusing them causes a
real, confusing install failure:

| Workflow | Key | Upgrades in place? |
|---|---|---|
| `.github/workflows/release.yml` | your real keystore, from repo secrets | yes, across releases |
| `.github/workflows/test-build.yml` | throwaway, regenerated every run | **no — never** |

An APK signed with key A cannot upgrade an installed APK signed with key B.
Android rejects it with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`. See
[Test builds](#test-builds-and-why-they-wont-upgrade) at the bottom.

---

## 1. Create the keystore (once, ever)

Losing this file means you can never ship an upgrade to anyone who installed a
previous release — they would have to uninstall and lose app data. Back it up
somewhere durable and private before you do anything else.

```bash
keytool -genkeypair \
  -keystore pkhexmobile-release.keystore \
  -alias pkhexmobile \
  -keyalg RSA -keysize 2048 -validity 10000 \
  -dname "CN=Your Name, OU=PkhexMobile, O=PkhexMobile, L=City, ST=State, C=IN"
```

`keytool` prompts for a store password, then a key password. Use a real password
manager; you need both again below. `-validity 10000` is ~27 years — Google Play
requires validity past 2033, and even for sideloading you do not want this
expiring.

Windows note: `keytool` ships with the JDK. If it is not on `PATH`, it is at
`%JAVA_HOME%\bin\keytool.exe`. Use JDK 21's copy — this project's toolchain
breaks on JDK 25.

**Verify it before trusting it:**

```bash
keytool -list -v -keystore pkhexmobile-release.keystore -alias pkhexmobile
```

---

## 2. Base64-encode it for GitHub Secrets

Secrets are text, so the binary keystore has to be encoded. `-w 0` matters —
without it, `base64` wraps lines and the workflow's `base64 -d` produces a
corrupt file.

Linux / macOS / Git Bash:

```bash
base64 -w 0 pkhexmobile-release.keystore > keystore.b64
```

macOS without GNU coreutils (`-w` is unsupported there):

```bash
base64 -i pkhexmobile-release.keystore | tr -d '\n' > keystore.b64
```

PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("pkhexmobile-release.keystore")) `
  | Set-Content -NoNewline keystore.b64
```

---

## 3. Upload the four secrets

```bash
gh secret set ANDROID_KEYSTORE_BASE64  --repo abhishek-rathod01/pkhex-mobile < keystore.b64
gh secret set ANDROID_KEYSTORE_PASSWORD --repo abhishek-rathod01/pkhex-mobile
gh secret set ANDROID_KEY_ALIAS         --repo abhishek-rathod01/pkhex-mobile
gh secret set ANDROID_KEY_PASSWORD      --repo abhishek-rathod01/pkhex-mobile
```

The last three prompt for the value on stdin, so it stays out of your shell
history. `ANDROID_KEY_ALIAS` is `pkhexmobile` if you used the command above.

Confirm all four landed:

```bash
gh secret list --repo abhishek-rathod01/pkhex-mobile
```

Then delete the intermediate file — it is your signing key in plain text:

```bash
rm keystore.b64
```

Keep `pkhexmobile-release.keystore` itself, backed up, outside the repo. It is
covered by `.gitignore`, but do not rely on that — keep it in another directory
entirely.

---

## 4. Cut a release

```bash
git tag v1.0.0
git push origin v1.0.0
```

`release.yml` then:

1. fails immediately, with a named list, if any of the four secrets is missing;
2. parses the tag into a display version and an Android `versionCode`;
3. builds and signs a Release APK;
4. writes release notes from the commits since the previous tag;
5. creates the GitHub Release and attaches the APK.

The in-app updater polls that Release. No Release, no update prompt.

### Tag format and versionCode

Tags must be `vMAJOR.MINOR.PATCH`, optionally with a pre-release suffix
(`v1.2.3-beta.1`, published as a GitHub pre-release).

`versionCode = major*10000 + minor*100 + patch`

| Tag | display version | versionCode |
|---|---|---|
| `v1.0.0` | 1.0.0 | 10000 |
| `v1.2.3` | 1.2.3 | 10203 |
| `v2.0.0` | 2.0.0 | 20000 |

Android **refuses to install an update whose `versionCode` is not higher than
the installed one**, so this must only ever increase. Two consequences:

- **MINOR and PATCH must each stay below 100.** The workflow hard-fails if not,
  rather than silently emitting a non-monotonic code.
- **A pre-release suffix does not get its own `versionCode`.** `v1.2.3-beta.1`
  and `v1.2.3` both produce 10203, so the second will not install over the
  first. Bump the patch instead of relying on the suffix.

---

## Test builds, and why they won't upgrade

`test-build.yml` is `workflow_dispatch` — run it from the Actions tab, download
the APK from that run's Artifacts section (14-day retention).

It generates a **fresh throwaway keystore on every run**, with a random password
that exists only inside that job. This is deliberate: it means a test APK is
installable without your real signing key ever touching a test workflow.

The cost is that signatures never match:

- test build from run #1 will not upgrade over test build from run #2;
- a real release will not upgrade over any test build;
- and vice versa.

Every one of those fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`. The fix is
always the same — uninstall first:

```bash
adb uninstall com.companyname.pkhexmobile
```

Uninstalling **erases the app's data**. This app never edits a save in place and
always exports to a new file, so there is nothing irreplaceable in app storage —
but anything you exported into app-private storage rather than shared storage
goes with it.

---

## Never commit

- `*.keystore`, `*.jks`, `*.p12` — signing keys
- `keystore.b64` or any base64 of the above
- passwords, aliases, tokens, in any file, including workflow YAML

The workflows read all of these from secrets, decode into `$RUNNER_TEMP`
(outside the workspace, so no artifact glob can catch them), and delete them in
an `if: always()` step.
