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
        private void Awake()
        {
            consolefaces =
                ConsoleFaces.Bind("ConsoleFaces", "ConsoleFaces", ":3", "What the mod sends into the console");
            Debug.Log(consolefaces.Value);
        }
        private ConfigFile ConsoleFaces = new ConfigFile(Path.Combine(Paths.ConfigPath, "axo.ConsoleFaces.cfg"), true);
        private ConfigEntry<string> consolefaces;
            
    }

}
