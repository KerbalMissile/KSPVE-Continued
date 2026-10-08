using UnityEngine;

namespace VolumetricBlackholeFx.Core;

/// <summary>KSP entry point. Runs once when the main menu first loads, after ModuleManager has patched configs.</summary>
[KSPAddon(KSPAddon.Startup.MainMenu, true)]
public sealed class Bootstrap : MonoBehaviour
{
    private void Awake()
    {
        Engine.Create();
    }
}
