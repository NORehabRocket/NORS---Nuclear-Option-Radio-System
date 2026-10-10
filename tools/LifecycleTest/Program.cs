using System;
using System.Reflection;
using NORS.Plugin;
using NORS.Plugin.Server;
using UnityEngine;

int pass = 0, fail = 0;
void Check(string label, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
    if (ok) pass++; else fail++;
}
void Invoke(NorsPlugin plugin, string method)
{
    typeof(NorsPlugin).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(plugin, null);
}
object Host(NorsPlugin plugin)
{
    var field = typeof(NorsPlugin).GetField("_serverHost",
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
    return field.GetValue(field.IsStatic ? null : plugin);
}

// No test-side resets of production fields or the quit event: cleanup must be real.
ServerMode.IsDedicatedServer = true;
var disabled = new NorsPlugin();
Invoke(disabled, "Awake");
Check("hosting disabled creates no relay or event", ServerHost.Created.Count == 0 && Application.Subscribers == 0);
Check("dedicated disabled creates no client hub", GameObject.Created.Count == 0);
Check("hosting disabled retains operator notice", disabled.Logger.Info.Exists(s => s.Contains("voice hosting is off")));
Invoke(disabled, "OnDestroy");
Application.Quit();
Check("disabled destruction and quit remain inert", ServerHost.Created.Count == 0 && Application.Subscribers == 0);

ServerMode.IsDedicatedServer = false;
NorsConfig.ServerHostRelay.Value = true;
var client = new NorsPlugin();
Invoke(client, "Awake");
Check("client creates one persistent hub", GameObject.Created.Count == 1 && UnityEngine.Object.PersistentObjects == 1);
var hub = GameObject.Created.Count == 1 ? GameObject.Created[0] : null;
Check("client hub identity and component preserved", hub != null && hub.name == "NorsHub" &&
    hub.hideFlags == HideFlags.HideAndDontSave && hub.Components.Count == 1 && hub.Components[0] == typeof(NorsHub));
Check("client ignores dedicated hosting option", ServerHost.Created.Count == 0 && Application.Subscribers == 0);
Check("client startup log preserved", client.Logger.Info.Exists(s => s.Contains("online. Press F1")));
Check("config and plugin references initialized", NorsConfig.Initializations == 2 &&
    ReferenceEquals(NorsConfig.LastConfig, client.Config) && ReferenceEquals(NorsPlugin.Instance, client) &&
    ReferenceEquals(NorsPlugin.Log, client.Logger));
Invoke(client, "OnDestroy");
Invoke(client, "OnDestroy");
Application.Quit();
Check("client cleanup remains inert", ServerHost.Created.Count == 0 && Application.Subscribers == 0);

ServerMode.IsDedicatedServer = true;
var first = new NorsPlugin();
Invoke(first, "Awake");
Check("dedicated Awake constructs exactly one relay", ServerHost.Created.Count == 1);
var relay = ServerHost.Created[0];
Check("dedicated Awake starts relay once", relay.Starts == 1 && relay.Running);
Check("dedicated Awake subscribes once", Application.Subscribers == 1);
Check("dedicated Awake creates no client hub", GameObject.Created.Count == 1);
Invoke(first, "OnDestroy");
Check("dedicated OnDestroy preserves running relay", relay.Stops == 0 && relay.Running);
Check("dedicated OnDestroy retains quit subscription", Application.Subscribers == 1);
Invoke(first, "Awake");
var second = new NorsPlugin();
Invoke(second, "Awake");
Check("repeated Awake across instances never duplicates relay", ServerHost.Created.Count == 1 && relay.Starts == 1 &&
    ReferenceEquals(Host(first), relay) && ReferenceEquals(Host(second), relay));
Check("repeated Awake never duplicates quit subscription", Application.Subscribers == 1);
Invoke(second, "OnDestroy");
Check("replacement component destruction preserves relay", relay.Stops == 0 && relay.Running);
Application.Quit();
Check("quitting stops retained relay exactly once", relay.Stops == 1 && !relay.Running);
Check("quitting clears relay reference", Host(second) == null && Host(first) == null);
Check("quitting unsubscribes handler", Application.Subscribers == 0);
Application.Quit();
Invoke(first, "OnDestroy");
Invoke(second, "OnDestroy");
Check("repeated quit and destruction are idempotent", relay.Stops == 1 && Application.Subscribers == 0);

int beforeRestart = ServerHost.Created.Count;
var restarted = new NorsPlugin();
Invoke(restarted, "Awake");
var nextRelay = ServerHost.Created[ServerHost.Created.Count - 1];
Check("post-quit Awake can create a fresh relay", ServerHost.Created.Count == beforeRestart + 1 &&
    !ReferenceEquals(nextRelay, relay) && nextRelay.Starts == 1 && nextRelay.Running && ReferenceEquals(Host(restarted), nextRelay));
Check("post-quit Awake subscribes exactly once", Application.Subscribers == 1);
Application.Quit();
Application.Quit();
Check("fresh lifecycle also cleans up idempotently", nextRelay.Stops == 1 && !nextRelay.Running &&
    Host(restarted) == null && Application.Subscribers == 0);

Console.WriteLine($"\n{pass} passed, {fail} failed.");
Environment.ExitCode = fail == 0 ? 0 : 1;
