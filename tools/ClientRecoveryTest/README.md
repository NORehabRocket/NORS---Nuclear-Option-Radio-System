# Client Recovery Regression

Linux and .NET SDK 8 are required, plus `sudo`, util-linux, coreutils and iproute2.
Build from the repository root without privileges, then run in a fresh network namespace:

```sh
dotnet build tools/ClientRecoveryTest/ClientRecoveryTest.csproj -c Release
sudo -n unshare --net -- timeout 60 "$(readlink -f "$(command -v dotnet)")" \
  tools/ClientRecoveryTest/bin/Release/net8.0/ClientRecoveryTest.dll
```

The harness refuses PID 1's network namespace and enables only isolated loopback.
It links the actual `VoiceClient.cs` and references real Common/Core projects. No
game assemblies, doubles or public endpoints are used. UDP ports are ephemeral;
unique temporary ban directories and sockets are cleaned up on normal completion.

Six groups cover immediate/bounded Hello heartbeats while connected, real relay
restart, session expiry through the real maintenance loop, State-only admission
denial, Hello/State identity-room-faction admin revocation and reauthentication,
stale-authority denial recovery, kick suppression and explicit reconnect. Heartbeat
time is supplied directly; reflection only locates the bound port and ages session
state under the server lock. A stale public client flag models a lost notification.
All packet exchanges use the actual client and relay receive loops.

Expect six PASS groups and exit 0; test failures exit 1, setup failures exit 2.
Individual waits are bounded at 2.5 seconds; the command caps the whole run at 60.
This is not Unity tick-order, rendered Login controls, WAN or audible-voice evidence.
