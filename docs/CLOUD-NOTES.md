# Cloud environment setup for pkhex-mobile

> ## Network access MUST be set to Custom, or none of this works
>
> Measured from inside a real cloud session on 2026-07-28. The default
> ("Trusted") allowlist **blocks the .NET SDK and Android SDK downloads**, so
> the setup script below runs to completion, exits 0, and installs nothing —
> every install line ends in `|| true` and `set -e` is deliberately off, so
> there is no visible failure. `dotnet` simply is not there afterwards.
>
> | Host | Needed for | Default policy |
> |---|---|---|
> | `dot.net` | redirects to the CDN below | reachable (301) |
> | `dotnet.microsoft.com` | landing page only | reachable (302) |
> | **`builds.dotnet.microsoft.com`** | **the actual .NET SDK payload** | **BLOCKED** |
> | `dotnetcli.azureedge.net` | legacy SDK CDN | BLOCKED |
> | **`dl.google.com`** | **Android cmdline-tools, platform 36, build-tools** | **BLOCKED** |
> | `api.nuget.org` | NuGet restore | reachable (200) |
> | `api.github.com` | Releases API, `gh` | reachable (200) |
>
> An earlier version of this file claimed "dotnet.microsoft.com is on the
> default Trusted allowlist so this download works." That is wrong in a way
> that is easy to miss: the *landing page* is allowed, the *download CDN* is
> not. `dot.net/v1/dotnet-install.sh` returns 301, and following that redirect
> is what 403s.
>
> **Set network access to Custom, tick "include default list", and add:**
> `builds.dotnet.microsoft.com`, `dl.google.com`, `dotnetcli.azureedge.net`.
>
> **Then start a NEW session.** Setup scripts run at container *creation*.
> Resuming or continuing an existing session skips provisioning entirely — the
> env-manager log shows `session_mode: resume` and
> `Fast resume: Languages already installed`, and your updated script never
> executes. Editing the script in the UI does not retroactively apply to a
> container that is already running.

The Claude Code cloud **Setup script** cannot live in the repo - it is attached
to the environment, not the clone. Paste the block below into the environment's
"Setup script" field at claude.ai/code (environment selector -> settings icon).

Everything that *can* live in the repo already does: `.claude/settings.json`
(permissions + SessionStart hook) and `scripts/claude-setup.sh` (dependency
install, gated on `CLAUDE_CODE_REMOTE`), so local and cloud behave the same.

Recommended environment name: `pkhex-mobile`
Network access: Custom (see notes inside the script)

```bash
#!/bin/bash
set -u   # deliberately NOT -e: a non-zero exit blocks the session from starting

# .NET SDK (not preinstalled in cloud sessions; dotnet.microsoft.com is on the
# default Trusted allowlist so this download works without a Custom allowlist)
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel STS --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

# JDK 21 is already the cloud image's default (OpenJDK 21). Do NOT install 25 --
# it is incompatible with this Android toolchain.
java -version

# Android cmdline-tools + platform 36 + build-tools 36.1.0.
# developer.android.com is on the default Trusted allowlist.
apt update && apt install -y unzip || true
mkdir -p /opt/android-sdk/cmdline-tools
curl -fsSL https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip -o /tmp/cmdline.zip
unzip -q /tmp/cmdline.zip -d /opt/android-sdk/cmdline-tools
mv /opt/android-sdk/cmdline-tools/cmdline-tools /opt/android-sdk/cmdline-tools/latest
yes | /opt/android-sdk/cmdline-tools/latest/bin/sdkmanager --licenses >/dev/null || true
/opt/android-sdk/cmdline-tools/latest/bin/sdkmanager \
  "platform-tools" "platforms;android-36" "build-tools;36.1.0" || true
# NOTE: dl.google.com is NOT on the default Trusted allowlist. Set the
# environment's network access to Custom, tick "include default list", and add:
#   dl.google.com

exit 0
```

Keep total setup-script runtime under about five minutes so the environment
cache can build. Append `|| true` to anything non-critical - a non-zero exit
stops the session from starting at all.
