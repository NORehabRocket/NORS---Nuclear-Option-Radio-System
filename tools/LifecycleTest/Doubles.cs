using System;
using System.Collections.Generic;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { }
    }

    public class BaseUnityPlugin : UnityEngine.Object
    {
        public readonly Logging.ManualLogSource Logger = new Logging.ManualLogSource();
        public readonly object Config = new object();
    }
}

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Info = new List<string>();
        public void LogInfo(object value) { Info.Add(value.ToString()); }
        public void LogWarning(object value) { }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static int PersistentObjects;
        public static void DontDestroyOnLoad(Object value) { PersistentObjects++; }
    }

    public enum HideFlags { HideAndDontSave }
    public enum KeyCode { None, F1 }

    public sealed class GameObject : Object
    {
        public static readonly List<GameObject> Created = new List<GameObject>();
        public readonly string name;
        public HideFlags hideFlags;
        public readonly List<Type> Components = new List<Type>();
        public GameObject(string name) { this.name = name; Created.Add(this); }
        public T AddComponent<T>() where T : new()
        {
            Components.Add(typeof(T));
            return new T();
        }
    }

    public static class Application
    {
        public static event Action quitting;
        public static int Subscribers => quitting == null ? 0 : quitting.GetInvocationList().Length;
        public static void Quit() { quitting?.Invoke(); }
    }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKey(KeyCode key) => false;
    }
}

namespace NORS.Plugin
{
    public sealed class Setting<T>
    {
        public T Value;
        public Setting(T value) { Value = value; }
    }

    public static class NorsConfig
    {
        public static readonly Setting<bool> ServerHostRelay = new Setting<bool>(false);
        public static readonly Setting<UnityEngine.KeyCode> PanelKey =
            new Setting<UnityEngine.KeyCode>(UnityEngine.KeyCode.F1);
        public static int Initializations;
        public static object LastConfig;
        public static void Init(object config) { Initializations++; LastConfig = config; }
    }

    public sealed class NorsHub { }
}

namespace NORS.Plugin.Server
{
    public static class ServerMode
    {
        public static bool IsDedicatedServer;
    }

    public sealed class ServerHost
    {
        public static readonly List<ServerHost> Created = new List<ServerHost>();
        public int Starts;
        public int Stops;
        public bool Running;
        public ServerHost() { Created.Add(this); }
        public void Start() { Starts++; Running = true; }
        public void Stop() { Stops++; Running = false; }
    }
}
