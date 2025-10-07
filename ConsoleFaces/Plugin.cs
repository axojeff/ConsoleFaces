using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ConsoleFaces
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private void Init()
        {
            CosmeticsV2Spawner_Dirty.OnPostInstantiateAllPrefabs += (() =>
            {
                Debug.Log(consolefaces.Value);
            });
        }
        private void Awake()
        {
            consolefaces =
                ConsoleFaces.Bind("ConsoleFaces", "ConsoleFaces", ":3", "What the mod sends into the console");
        }
        private void Start()
        {
            GorillaTagger.OnPlayerSpawned(Init);
          
        }
        private ConfigFile ConsoleFaces = new ConfigFile(Path.Combine(Paths.ConfigPath, "axo.ConsoleFaces.cfg"), true);
        private ConfigEntry<string> consolefaces;
            
    }

}