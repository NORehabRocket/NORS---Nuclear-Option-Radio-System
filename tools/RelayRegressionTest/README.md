# Relay Regression Test

Linux, .NET SDK 8, `sudo`, util-linux (`unshare`), coreutils and iproute2 are
required. From the repository root, build without privileges, then run only
inside a fresh network namespace:

```sh
dotnet build tools/RelayRegressionTest/RelayRegressionTest.csproj -c Release
sudo -n unshare --net -- timeout 60 "$(readlink -f "$(command -v dotnet)")" \
  tools/RelayRegressionTest/bin/Release/net8.0/RelayRegressionTest.dll
```

The harness refuses the PID 1 network namespace and brings up only its isolated
loopback interface. Do not remove that guard or run against a live relay. It uses
ephemeral UDP ports and unique temporary ban files, cleaned up on normal completion.
No game, microphone, Steam client, game assemblies or public endpoint is needed.

The console references the actual Common/Core projects, not copied logic or doubles.
Reflection exercises private packet handling, sweep and limiter state; a separate
group uses the real UDP receive loop. A full successful run reports **13 groups,
1034 assertions**, assembly SHA256 values and exit code 0. Test failures exit 1;
harness/setup errors exit 2. An optional final argument filters group names, such
as `membership`; filtered runs do not establish full-suite acceptance.

Coverage includes untrusted host denial, password-admin authority, endpoint binding,
voice routing, enabled/disabled voting and membership changes, truncation/malformed
packets, bounded roster/admission/rates/egress, timeout reconnect and receive-loop
survival. This is offline relay verification, not Unity, WAN or audible-voice proof.
