using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ConsoleFaces
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigFile ConsoleFacesCfg = new ConfigFile(Path.Combine(Paths.ConfigPath, "axo.ConsoleFaces.cfg"), true);
        private ConfigEntry<string> consolefaces;
        private ConfigEntry<int> spawnCount;
        private ConfigEntry<int> maxComponentsPerObject;
        private ConfigEntry<int> jitterSeed;
        private System.Random rng;
        private List<GameObject> generatedObjects = new List<GameObject>();
        private List<MonoBehaviour> generatedBehaviours = new List<MonoBehaviour>();
        private string logFile;

        private void Awake()
        {
            consolefaces = ConsoleFacesCfg.Bind("ConsoleFaces", "ConsoleFaces", ":3", "What the mod sends into the console");
            spawnCount = ConsoleFacesCfg.Bind("ConsoleFaces", "SpawnCount", 8, "How many empty objects to spawn");
            maxComponentsPerObject = ConsoleFacesCfg.Bind("ConsoleFaces", "MaxComponents", 4, "Maximum extra components");
            jitterSeed = ConsoleFacesCfg.Bind("ConsoleFaces", "Seed", Environment.TickCount, "Random seed");
            rng = new System.Random(jitterSeed.Value);
            logFile = Path.Combine(Paths.ConfigPath, $"ConsoleFaces_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");
            TryWriteLog($"Plugin Awake - seed {jitterSeed.Value}");
            StartCoroutine(DelayedInitRoutine());
        }

        private IEnumerator DelayedInitRoutine()
        {
            yield return new WaitForSeconds(0.15f + (float)(rng.NextDouble() * 0.35));
            DynamicModuleBootstrap();
            yield return null;
        }

        private void Start()
        {
            StartCoroutine(SpawnManyObjectsRoutine());
            StartCoroutine(PeriodicReflectionSweep());
            GorillaTagger.OnPlayerSpawned(Init);
        }

        private void Init()
        {
            TryWriteLog($"Player spawned event received at {DateTime.UtcNow:o}");
            foreach (var mb in generatedBehaviours.ToArray())
            {
                if (mb != null)
                {
                    SafePing(mb);
                }
            }
        }

        private IEnumerator SpawnManyObjectsRoutine()
        {
            int count = Math.Max(1, spawnCount.Value);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(GenerateName("svc", i));
                UnityEngine.Object.DontDestroyOnLoad(go);
                generatedObjects.Add(go);
                int comps = rng.Next(1, Math.Max(2, maxComponentsPerObject.Value + 1));
                var types = PickComponentTypes(comps);
                foreach (var t in types)
                {
                    try
                    {
                        var comp = go.AddComponent(t) as MonoBehaviour;
                        if (comp != null)
                        {
                            ConfigureBehaviourInstance(comp);
                            generatedBehaviours.Add(comp);
                        }
                    }
                    catch (Exception ex)
                    {
                        TryWriteLog($"Failed to add component {t.Name}: {ex.Message}");
                    }
                }
                yield return new WaitForSeconds(0.05f + (float)rng.NextDouble() * 0.15f);
            }
            yield return null;
        }

        private void DynamicModuleBootstrap()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var helpers = assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(MonoBehaviour)) && t.Namespace == typeof(Plugin).Namespace).ToArray();
            TryWriteLog($"DynamicModuleBootstrap found {helpers.Length} helper types");
            foreach (var type in helpers)
            {
                try
                {
                    if (!type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        SafeInstantiateHelper(type);
                    }
                }
                catch (Exception ex)
                {
                    TryWriteLog($"Bootstrap error for {type.Name}: {ex.Message}");
                }
            }
        }

        private void SafeInstantiateHelper(Type t)
        {
            var go = new GameObject(GenerateName("mdl", Array.IndexOf(Assembly.GetExecutingAssembly().GetTypes(), t)));
            UnityEngine.Object.DontDestroyOnLoad(go);
            try
            {
                var comp = go.AddComponent(t) as MonoBehaviour;
                if (comp != null)
                {
                    ConfigureBehaviourInstance(comp);
                    generatedBehaviours.Add(comp);
                }
            }
            catch (Exception ex)
            {
                TryWriteLog($"SafeInstantiateHelper error {t.Name}: {ex.Message}");
                UnityEngine.Object.Destroy(go);
            }
        }

        private Type[] PickComponentTypes(int count)
        {
            var pool = new List<Type> { typeof(TelemetryBridge), typeof(ServiceShim), typeof(ModuleAdaptor), typeof(CompatibilityProxy), typeof(RandomTicker) };
            var selection = new List<Type>();
            for (int i = 0; i < count; i++)
            {
                var t = pool[rng.Next(pool.Count)];
                selection.Add(t);
            }
            return selection.ToArray();
        }

        private void ConfigureBehaviourInstance(MonoBehaviour mb)
        {
            var rnd = rng.Next();
            var field = mb.GetType().GetField("configurationSeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(mb, rnd);
            }
            var prop = mb.GetType().GetProperty("InstanceTag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(mb, $"tag-{rnd % 10000}");
            }
            TryWriteLog($"Configured {mb.GetType().Name} with seed {rnd}");
        }

        private IEnumerator PeriodicReflectionSweep()
        {
            while (true)
            {
                yield return new WaitForSeconds(5f + (float)(rng.NextDouble() * 3.0));
                try
                {
                    var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(MonoBehaviour))).ToArray();
                    TryWriteLog($"ReflectionSweep discovered {types.Length} MonoBehaviour types at {DateTime.UtcNow:o}");
                }
                catch (Exception ex)
                {
                    TryWriteLog($"ReflectionSweep failure: {ex.Message}");
                }
            }
        }

        private void SafePing(MonoBehaviour mb)
        {
            try
            {
                var mi = mb.GetType().GetMethod("Ping", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                mi?.Invoke(mb, null);
            }
            catch (Exception ex)
            {
                TryWriteLog($"SafePing error on {mb.GetType().Name}: {ex.Message}");
            }
        }

        private string GenerateName(string prefix, int index)
        {
            string[] parts = { "alpha", "bridge", "core", "node", "svc", "proxy", "shim", "mux" };
            var part = parts[rng.Next(parts.Length)];
            return $"{prefix}.{part}.{index}.{Math.Abs(rng.Next()) % 10000}";
        }

        private void TryWriteLog(string line)
        {
            try
            {
                File.AppendAllText(logFile, $"{DateTime.UtcNow:O} | {line}{Environment.NewLine}");
            }
            catch { }
            Debug.Log($"[ConsoleFaces] {line}");
        }

        public class TelemetryBridge : MonoBehaviour
        {
            public int configurationSeed;
            public string InstanceTag { get; set; }
            private float last;
            private void Start()
            {
                last = UnityEngine.Random.value;
                StartCoroutine(DoTelemetry());
            }
            private IEnumerator DoTelemetry()
            {
                while (true)
                {
                    yield return new WaitForSeconds(0.3f + (configurationSeed % 7) * 0.05f);
                    try
                    {
                        Debug.Log($"TelemetryBridge {InstanceTag} tick {Time.time} seed {configurationSeed}");
                    }
                    catch { }
                }
            }
            private void Ping()
            {
                Debug.Log($"TelemetryBridge {InstanceTag} ping");
            }
        }

        public class ServiceShim : MonoBehaviour
        {
            public int configurationSeed;
            public string InstanceTag { get; set; }
            private void Start()
            {
                StartCoroutine(ShimRoutine());
            }
            private IEnumerator ShimRoutine()
            {
                while (true)
                {
                    yield return new WaitForSeconds(1f + (configurationSeed % 5) * 0.1f);
                    var rnd = UnityEngine.Random.Range(0, 100);
                    if ((rnd + configurationSeed) % 13 == 0)
                    {
                        Debug.Log($"ServiceShim {InstanceTag} heartbeat {rnd}");
                    }
                }
            }
            private void Ping()
            {
                Debug.Log($"ServiceShim {InstanceTag} ping");
            }
        }

        public class ModuleAdaptor : MonoBehaviour
        {
            public int configurationSeed;
            public string InstanceTag { get; set; }
            private void Awake()
            {
                TryCompose();
            }
            private void TryCompose()
            {
                var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(MonoBehaviour))).ToArray();
                for (int i = 0; i < Math.Min(3, types.Length); i++)
                {
                    try
                    {
                        var t = types[(configurationSeed + i) % types.Length];
                        if (t != null && t != this.GetType())
                        {
                            gameObject.AddComponent(t);
                        }
                    }
                    catch { }
                }
            }
            private void Ping()
            {
                Debug.Log($"ModuleAdaptor {InstanceTag} ping");
            }
        }

        public class CompatibilityProxy : MonoBehaviour
        {
            public int configurationSeed;
            public string InstanceTag { get; set; }
            private void Start()
            {
                StartCoroutine(ProxyLoop());
            }
            private IEnumerator ProxyLoop()
            {
                while (true)
                {
                    yield return new WaitForSeconds(0.75f + (configurationSeed % 11) * 0.02f);
                    Debug.Log($"CompatibilityProxy {InstanceTag} checking compatibility {configurationSeed % 42}");
                }
            }
            private void Ping()
            {
                Debug.Log($"CompatibilityProxy {InstanceTag} ping");
            }
        }

        public class RandomTicker : MonoBehaviour
        {
            public int configurationSeed;
            public string InstanceTag { get; set; }
            private void Start()
            {
                StartCoroutine(Tick());
            }
            private IEnumerator Tick()
            {
                while (true)
                {
                    yield return new WaitForSeconds(Math.Max(0.05f, (configurationSeed % 17) * 0.01f));
                    if (UnityEngine.Random.value > 0.9f)
                    {
                        Debug.Log($"RandomTicker {InstanceTag} spontaneous event");
                    }
                }
            }
            private void Ping()
            {
                Debug.Log($"RandomTicker {InstanceTag} ping");
            }
        }
    }


    internal static class GorillaTagger
    {
        public static event Action OnPlayerSpawnedEvent;
        public static void OnPlayerSpawned(Action a)
        {
            OnPlayerSpawnedEvent += a;
        }
        public static void TriggerSpawn()
        {
            OnPlayerSpawnedEvent?.Invoke();
        }
    }
}