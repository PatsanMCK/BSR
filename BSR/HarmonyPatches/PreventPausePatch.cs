using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BSR.Managers;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows;

namespace BSR.HarmonyPatches
{
    // =========================================================================
    // 1. FPFC KEYBOARD PAUSE KEYBIND (Press P or Escape in FPFC to pop out menu!)
    // =========================================================================
    [HarmonyPatch(typeof(PauseController), "Start")]
    public class FpfcPauseKeybindPatch
    {
        private static void Postfix(PauseController __instance)
        {
            if (__instance != null && __instance.gameObject.GetComponent<FpfcPauseKeybindHelper>() == null)
            {
                __instance.gameObject.AddComponent<FpfcPauseKeybindHelper>();
            }
        }
    }

    public class FpfcPauseKeybindHelper : MonoBehaviour
    {
        private static readonly Func<KeyCode, bool> GetKeyDownDelegate = InitGetKeyDown();
        private PauseController _pauseController;

        private static Func<KeyCode, bool> InitGetKeyDown()
        {
            try
            {
                var inputType = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule")
                             ?? Type.GetType("UnityEngine.Input, UnityEngine.CoreModule")
                             ?? typeof(Input);

                var method = inputType?.GetMethod("GetKeyDown", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(KeyCode) }, null);
                if (method != null)
                {
                    return (Func<KeyCode, bool>)Delegate.CreateDelegate(typeof(Func<KeyCode, bool>), method);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Could not bind Input.GetKeyDown delegate: {ex.Message}");
            }
            return null;
        }

        private void Awake()
        {
            _pauseController = GetComponent<PauseController>();
        }

        private void Update()
        {
            if (GetKeyDownDelegate != null && (GetKeyDownDelegate(KeyCode.P) || GetKeyDownDelegate(KeyCode.Escape)))
            {
                if (_pauseController != null && _pauseController.enabled)
                {
                    _pauseController.Pause();
                }
            }
        }
    }

    // =========================================================================
    // 2. BLOCK CONTROLLER RESUME ONCE PAUSE TIME IS EXHAUSTED
    // =========================================================================
    [HarmonyPatch(typeof(PauseMenuManager), "ContinueButtonPressed")]
    public class BlockContinueButtonPatch
    {
        private static bool Prefix()
        {
            // If the pause allowance is used up during a ranked match, disallow resuming!
            if (RankedMatchManager.IsInRankedMatch && PreventPausePatch.RemainingPauseTime <= 0f)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PauseMenuManager), "StartResumeAnimation")]
    public class BlockResumeAnimationPatch
    {
        private static bool Prefix()
        {
            if (RankedMatchManager.IsInRankedMatch && PreventPausePatch.RemainingPauseTime <= 0f)
            {
                return false;
            }
            return true;
        }
    }

    // =========================================================================
    // 3. ANTI-STALL PAUSE MENU (Restart hidden, timer removes Continue button)
    // =========================================================================
    [HarmonyPatch(typeof(PauseMenuManager), nameof(PauseMenuManager.ShowMenu))]
    public class PreventPausePatch
    {
        public const float MaxPauseSecondsPerMap = 3.0f;
        private const float AutoForfeitStallTimeout = 15.0f;

        private static PauseMenuManager _currentMapPauseManager;
        private static float _remainingPauseTime = MaxPauseSecondsPerMap;
        private static Coroutine _antiStallCoroutine;

        public static float RemainingPauseTime => _remainingPauseTime;

        private static void Postfix(PauseMenuManager __instance)
        {
            try
            {
                var restartButton = GetRestartButton(__instance);
                var continueButton = GetContinueButton(__instance);
                var backButton = GetBackButton(__instance);

                if (!RankedMatchManager.IsInRankedMatch)
                {
                    if (restartButton != null)
                    {
                        restartButton.gameObject.SetActive(true);
                        restartButton.interactable = true;
                        restartButton.transform.localScale = Vector3.one;
                    }
                    if (continueButton != null)
                    {
                        continueButton.gameObject.SetActive(true);
                        continueButton.interactable = true;
                        continueButton.transform.localScale = Vector3.one;
                    }
                    return;
                }

                // Reset the pause allowance whenever a new map starts
                if (_currentMapPauseManager != __instance)
                {
                    _currentMapPauseManager = __instance;
                    _remainingPauseTime = MaxPauseSecondsPerMap;
                }

                // Restart button is always removed in Ranked Matches
                if (restartButton != null)
                {
                    restartButton.gameObject.SetActive(false);
                    restartButton.interactable = false;
                    restartButton.transform.localScale = Vector3.zero;
                }

                if (_antiStallCoroutine != null)
                    __instance.StopCoroutine(_antiStallCoroutine);

                _antiStallCoroutine = __instance.StartCoroutine(AntiStallPauseCoroutine(__instance, continueButton, backButton, restartButton));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"[BS Ranked] Error in PreventPausePatch: {ex.Message}");
            }
        }

        private static IEnumerator AntiStallPauseCoroutine(PauseMenuManager pauseMenu, Button continueButton, Button backButton, Button restartButton)
        {
            var continueText = continueButton != null ? continueButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            string originalContinueLabel = continueText != null ? continueText.text : "CONTINUE";

            // PHASE 1: Count down the allowed pause time
            if (_remainingPauseTime > 0f)
            {
                if (continueButton != null)
                {
                    continueButton.gameObject.SetActive(true);
                    continueButton.interactable = true;
                    continueButton.transform.localScale = Vector3.one;
                }

                while (pauseMenu != null && pauseMenu.enabled && _remainingPauseTime > 0f)
                {
                    // Ensure restart button stays hidden even if an animator ticks
                    if (restartButton != null && (restartButton.gameObject.activeSelf || restartButton.transform.localScale != Vector3.zero))
                    {
                        restartButton.gameObject.SetActive(false);
                        restartButton.interactable = false;
                        restartButton.transform.localScale = Vector3.zero;
                    }

                    _remainingPauseTime -= Time.unscaledDeltaTime;

                    if (continueText != null)
                        continueText.text = $"CONTINUE ({Mathf.CeilToInt(Mathf.Max(0f, _remainingPauseTime))}s)";

                    yield return null;
                }

                if (continueText != null)
                    continueText.text = originalContinueLabel;
            }

            // PHASE 2: Timer reached 0s -> DO NOT UNPAUSE! Simply remove the Continue button completely!
            if (continueButton != null)
            {
                continueButton.gameObject.SetActive(false);
                continueButton.interactable = false;
                continueButton.transform.localScale = Vector3.zero;
            }

            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(false);
                restartButton.interactable = false;
                restartButton.transform.localScale = Vector3.zero;
            }

            Plugin.Log.Info($"[BS Ranked] {MaxPauseSecondsPerMap}s pause time expired -> Removed Continue button. Only Forfeit is available!");

            // PHASE 3: Stall protection (if the player leaves their headset on the floor, auto-forfeit after timeout)
            float stallTimer = AutoForfeitStallTimeout;
            while (pauseMenu != null && pauseMenu.enabled && stallTimer > 0f)
            {
                // Ensure continue and restart buttons remain hidden
                if (continueButton != null && continueButton.gameObject.activeSelf)
                    continueButton.gameObject.SetActive(false);

                if (restartButton != null && restartButton.gameObject.activeSelf)
                    restartButton.gameObject.SetActive(false);

                stallTimer -= Time.unscaledDeltaTime;
                yield return null;
            }

            // If stall timeout passes without clicking Forfeit, trigger forfeit
            if (pauseMenu != null && pauseMenu.enabled && stallTimer <= 0f)
            {
                Plugin.Log.Warn("[BS Ranked] Player stalled in pause menu without clicking Forfeit -> Auto-forfeiting!");
                backButton?.onClick.Invoke();
            }
        }

        private static Button GetRestartButton(PauseMenuManager pauseMenu)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var field = typeof(PauseMenuManager).GetField("_restartButton", flags)
                         ?? typeof(PauseMenuManager).GetField("restartButton", flags);
                if (field?.GetValue(pauseMenu) is Button btn && btn != null)
                    return btn;
            }
            catch { }

            var buttons = pauseMenu.GetComponentsInChildren<Button>(true);
            var foundByName = buttons.FirstOrDefault(b => b.name.IndexOf("Restart", StringComparison.OrdinalIgnoreCase) >= 0);
            if (foundByName != null) return foundByName;

            return buttons.FirstOrDefault(b =>
            {
                var text = b.GetComponentInChildren<TextMeshProUGUI>(true);
                return text != null && text.text.IndexOf("Restart", StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        private static Button GetContinueButton(PauseMenuManager pauseMenu)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var field = typeof(PauseMenuManager).GetField("_continueButton", flags)
                         ?? typeof(PauseMenuManager).GetField("continueButton", flags);
                if (field?.GetValue(pauseMenu) is Button btn && btn != null)
                    return btn;
            }
            catch { }

            var buttons = pauseMenu.GetComponentsInChildren<Button>(true);
            return buttons.FirstOrDefault(b => b.name.IndexOf("Continue", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Button GetBackButton(PauseMenuManager pauseMenu)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var field = typeof(PauseMenuManager).GetField("_backButton", flags)
                         ?? typeof(PauseMenuManager).GetField("_menuButton", flags)
                         ?? typeof(PauseMenuManager).GetField("backButton", flags);
                if (field?.GetValue(pauseMenu) is Button btn && btn != null)
                    return btn;
            }
            catch { }

            var buttons = pauseMenu.GetComponentsInChildren<Button>(true);
            return buttons.FirstOrDefault(b => b.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0 || b.name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}