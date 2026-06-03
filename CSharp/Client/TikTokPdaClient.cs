#if CLIENT
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Barotrauma;

namespace TikTokPda
{
    public class TikTokPdaPlugin : IAssemblyPlugin
    {
        public static TikTokPdaPlugin Instance { get; private set; }
        private static Process pdaProcess;
        private static string modDir;
        private static DateTime lastToggleTime = DateTime.MinValue;

        public void Initialize()
        {
            Instance = this;
            
            // Find our mod directory from the enabled packages
            var package = ContentPackageManager.EnabledPackages.All
                .FirstOrDefault(p => p.Name.IndexOf("TikTok PDA", StringComparison.OrdinalIgnoreCase) >= 0);
            
            if (package != null)
            {
                modDir = Path.GetFullPath(package.Dir);
                LuaCsLogger.Log("TikTok PDA: Mod directory located at " + modDir);
            }
            else
            {
                LuaCsLogger.LogError("TikTok PDA: Could not find enabled package directory!");
            }

            // Apply Harmony patches
            try
            {
                var harmony = new Harmony("com.antigravity.tiktokpda");
                harmony.PatchAll();
                LuaCsLogger.Log("TikTok PDA: Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError("TikTok PDA: Failed to apply Harmony patches: " + ex.Message);
            }
        }

        public void PreInitPatching() { }

        public void OnLoadCompleted() { }

        public void Dispose()
        {
            ClosePdaProcess();
        }

        public static void TogglePda()
        {
            if ((DateTime.UtcNow - lastToggleTime).TotalMilliseconds < 500)
            {
                return; // Cooldown of 500ms to prevent double-clicks
            }
            lastToggleTime = DateTime.UtcNow;

            if (pdaProcess != null && !pdaProcess.HasExited)
            {
                ClosePdaProcess();
            }
            else
            {
                OpenPdaProcess();
            }
        }

        private static void OpenPdaProcess()
        {
            if (string.IsNullOrEmpty(modDir))
            {
                LuaCsLogger.LogError("TikTok PDA: Cannot start browser, mod directory not resolved.");
                return;
            }
            
            string exePath = Path.Combine(modDir, "Subprocess", "PdaBrowser.exe");
            if (!File.Exists(exePath))
            {
                LuaCsLogger.LogError("TikTok PDA: Browser executable not found at " + exePath);
                return;
            }

            try
            {
                ClosePdaProcess(); // Ensure any orphaned process is killed first
                
                pdaProcess = new Process();
                pdaProcess.StartInfo.FileName = exePath;
                pdaProcess.StartInfo.Arguments = Process.GetCurrentProcess().MainWindowHandle.ToString();
                pdaProcess.StartInfo.WorkingDirectory = Path.Combine(modDir, "Subprocess");
                pdaProcess.StartInfo.UseShellExecute = false;
                pdaProcess.Start();
                LuaCsLogger.Log("TikTok PDA: Started browser process (PID: " + pdaProcess.Id + ")");
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError("TikTok PDA: Failed to start browser process: " + ex.Message);
            }
        }

        public static void ClosePdaProcess()
        {
            if (pdaProcess != null)
            {
                try
                {
                    if (!pdaProcess.HasExited)
                    {
                        pdaProcess.Kill();
                        LuaCsLogger.Log("TikTok PDA: Terminated browser process.");
                    }
                }
                catch (Exception ex)
                {
                    LuaCsLogger.LogError("TikTok PDA: Error killing browser process: " + ex.Message);
                }
                pdaProcess = null;
            }
        }

        public static void Update()
        {
            // If process exited externally, clean reference
            if (pdaProcess != null && pdaProcess.HasExited)
            {
                pdaProcess = null;
            }

            if (pdaProcess == null) return;

            // Close PDA if character is dead, nonexistent, or does not have it in hand
            bool hasItInHand = false;
            
            if (Character.Controlled != null && !Character.Controlled.IsDead && Character.Controlled.Inventory != null)
            {
                var leftHandItem = Character.Controlled.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand);
                var rightHandItem = Character.Controlled.Inventory.GetItemInLimbSlot(InvSlotType.RightHand);

                if ((leftHandItem != null && leftHandItem.Prefab.Identifier.Value == "tiktokpda") ||
                    (rightHandItem != null && rightHandItem.Prefab.Identifier.Value == "tiktokpda"))
                {
                    hasItInHand = true;
                }
            }

            if (!hasItInHand)
            {
                ClosePdaProcess();
            }
        }
    }

    [HarmonyPatch(typeof(Item))]
    [HarmonyPatch("Use")]
    class ItemUsePatch
    {
        [HarmonyPrefix]
        static bool Prefix(Item __instance, float deltaTime, Character user)
        {
            if (__instance == null || user == null) return true;
            
            // Only trigger for local client player
            if (Character.Controlled != user) return true;

            if (__instance.Prefab.Identifier.Value == "tiktokpda")
            {
                LuaCsLogger.Log("Toggling TikTok PDA!");
                TikTokPdaPlugin.TogglePda();
                return false; // Prevent game default action
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GameMain), "Update")]
    class GameMainUpdatePatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            TikTokPdaPlugin.Update();
        }
    }

    // Logger wrapper to safely log to LuaCs or debug console
    public static class LuaCsLogger
    {
        public static void Log(string message)
        {
            DebugConsole.NewMessage("[TikTokPDA] " + message, Microsoft.Xna.Framework.Color.LightGreen);
        }

        public static void LogError(string message)
        {
            DebugConsole.ThrowError("[TikTokPDA Error] " + message);
        }
    }
}
#endif
