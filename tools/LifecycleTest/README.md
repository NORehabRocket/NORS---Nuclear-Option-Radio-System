# Plugin Lifecycle Regression

Run from the repository root with .NET SDK 8:

```sh
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project tools/LifecycleTest/LifecycleTest.csproj
```

This console harness links the actual `src/NORS.Plugin/NorsPlugin.cs` at compile
time, including its private lifecycle callbacks, which are invoked by reflection.
It follows SmokeTest's PASS/FAIL output and nonzero failure exit convention.
No production lifecycle logic is copied into the harness.

Minimal dependency doubles stand in for Unity, BepInEx, configuration, dedicated
mode detection, the client hub and the relay host. They record construction,
start/stop calls, hub setup and quit-event subscriptions. No game assemblies,
configuration files, network traffic or external packages are required.

Coverage: hosting disabled, ordinary client startup, dedicated startup, bootstrap
destruction, repeated Awake on the same and replacement components, quit cleanup,
idempotence, and a fresh lifecycle after cleanup. Production fields/events are
not reset by the tests. Mode switches only separate scenarios after cleanup;
real production mode detection is process-cached.

This is a source-level regression test using doubles, **not a Unity runtime
test**. It does not prove Unity callback ordering, actual game compatibility,
relay networking, audio, concurrent callbacks, startup failures or forced-process
termination cleanup. Build and runtime validation remain separate checks.

## Original-Source Negative Control

Use an isolated temporary copy so neither the working source nor its build
outputs are replaced. From the repository root:

```sh
tmp=$(mktemp -d)
cp tools/LifecycleTest/{LifecycleTest.csproj,Program.cs,Doubles.cs} "$tmp/"
git show b64f19a9a420290a5d5c0ba1353123aab7f60986:src/NORS.Plugin/NorsPlugin.cs > "$tmp/OriginalPlugin.source"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project "$tmp/LifecycleTest.csproj" -p:PluginSource="$tmp/OriginalPlugin.source"
```

The override deliberately uses a non-`.cs` extension to avoid SDK default source
globbing in the temporary project. Expect exit code 1 with lifecycle failures,
including `dedicated OnDestroy preserves running relay`. A compilation failure
is not a successful negative control. The current source should exit 0.
