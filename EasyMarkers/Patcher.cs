using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using Nautilus.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Story;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace EasyMarkersMod
{
    [BepInPlugin(Guid, Name, Version)]
    public class EasyMarkers : BaseUnityPlugin
    {
        public const string Guid = "com.appendixis.easymarkers";
        public const string Name = "EasyMarkers";
        public const string Version = "1.4";

        public static string Prefix = "[EasyMarkers] ";
        public static EasyMarkers Instance;

        public static ManualLogSource logger;

        public static Sprite AddMarkerSprite;
        public static Sprite RenameMarkerSprite;
        public static Sprite DeleteMarkerSprite;
        public static Sprite SaveMarkersSprite;
        public static Sprite LoadMarkersSprite;

        private void Awake()
        {
            Instance = this;
            logger = Logger;

            LanguageHandler.RegisterLocalizationFolder();

            logger.LogInfo($"EasyMarkers v{Version} loaded!");

            AddMarkerSprite = LoadSpriteFromFile("AddPingButton.png");
            RenameMarkerSprite = LoadSpriteFromFile("RenamePingButton.png");
            DeleteMarkerSprite = LoadSpriteFromFile("DeletePingButton.png");
            SaveMarkersSprite = LoadSpriteFromFile("SaveMarkersButton.png");
            LoadMarkersSprite = LoadSpriteFromFile("LoadMarkersButton.png");

            Harmony.CreateAndPatchAll(typeof(PingEntryPatch)); // rename markers in list / add marker controls
            Harmony.CreateAndPatchAll(typeof(PingPatch)); // rename markers on GUI
            Harmony.CreateAndPatchAll(typeof(SignalPingPatch)); // disable visit trigger
            Harmony.CreateAndPatchAll(typeof(PingTabPatch)); // add UI / sort markers
        }

        public static Sprite LoadSpriteFromFile(string filename)
        {
            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", filename);
            if (!File.Exists(path))
            {
                logger.LogError($"file not found: {path}");
                return null;
            }

            return ImageUtils.LoadSpriteFromFile(path);
        }

        public static IEnumerator SetUserInputText(string text)
        {
            yield return new WaitForSecondsRealtime(0.05f);
            GUIUtility.systemCopyBuffer = text;
            uGUI.main.userInput.inputField.MoveTextStart(shift: false);
            uGUI.main.userInput.inputField.text = text;
            uGUI.main.userInput.inputField.MoveTextEnd(shift: true);
        }

        public static void Create(string newMarkerName = null, Vector3 position = default, int colorIndex = 4)
        {
            if (position == default || position == null || position == Vector3.zero)
            {
                position = Player.main.transform.position;  
            }

            if (!string.IsNullOrEmpty(newMarkerName))
            {
                PlaceMarker(newMarkerName, position, colorIndex);
                return;
            }

            int depth = (int)(Ocean.GetOceanLevel() - Player.main.transform.position.y);

            uGUI.main.userInput.RequestString(Language.main.Get("InputMarkerNameHeader"), Language.main.Get("EM_OK"), string.Format(Language.main.Get("InputDefaultMarkerName"), (depth < 0 ? "+" + (depth * -1).ToString() : depth.ToString())), 256, (string inputMarkerNameResult) => {
                if (!string.IsNullOrEmpty(inputMarkerNameResult))
                {
                    PlaceMarker(inputMarkerNameResult, position, colorIndex);
                }
            });
        }

        public static void Export()
        {

            var signalsToExport = new List<object>();

            var gameSignals = UnityEngine.Object.FindObjectsOfType<SignalPing>();
            foreach (var gameSignal in gameSignals)
            {
                if (!gameSignal.descriptionKey.StartsWith(EasyMarkers.Prefix))
                {
                    continue;
                }
                var ping = gameSignal.GetComponent<PingInstance>();
                if (ping != null && ping.visible)
                {
                    signalsToExport.Add(new {
                        n = gameSignal.descriptionKey.Substring(EasyMarkers.Prefix.Length),
                        d = new List<float>() { (float) Math.Round(gameSignal.transform.position.x, 2), (float)Math.Round(gameSignal.transform.position.y, 2) , (float)Math.Round(gameSignal.transform.position.z, 2), (float) ping.colorIndex },
                    });
                }
            }

            if (signalsToExport.Count == 0)
            {
                ErrorMessage.AddDebug(Language.main.Get("ExportMarkersNoMarkersMessage"));
                return;
            }

            string json = JsonConvert.SerializeObject(signalsToExport, Formatting.None);
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            try
            {
                GUIUtility.systemCopyBuffer = base64;
                ErrorMessage.AddDebug(Language.main.Get("ExportMarkersCopiedToClipboard"));
            }
            catch (System.Exception e)
            {
                logger.LogError($"Copy to buffer error: {e.Message}");
                uGUI.main.userInput.RequestString(Language.main.Get("ExportMarkersLabel"), Language.main.Get("Close"), "", 0, (string inputResult) => { });
                Instance.StartCoroutine(SetUserInputText(base64));
                ErrorMessage.AddDebug(Language.main.Get("ExportMarkersErrorClipboard"));
            }
            
        }

        public static void Import()
        {
            uGUI.main.userInput.RequestString(Language.main.Get("ImportMarkersLabel"), Language.main.Get("EM_OK"), "", 0, (string inputMarkersString) => {
                if (string.IsNullOrEmpty(inputMarkersString))
                {
                    return;
                }

                byte[] bytes = System.Convert.FromBase64String(inputMarkersString);
                string json = System.Text.Encoding.UTF8.GetString(bytes);

                if (string.IsNullOrEmpty(json))
                {
                    ErrorMessage.AddDebug(Language.main.Get("ImportMarkersInvalidCode"));
                    return;
                }

                List<object> signalsFromImport = null;

                try
                {
                    signalsFromImport = JsonConvert.DeserializeObject<List<object>>(json);
                    if (signalsFromImport == null)
                    {
                        ErrorMessage.AddDebug(Language.main.Get("ImportMarkersInvalidCode"));
                        return;
                    }
                }
                catch (JsonException e)
                {
                    logger.LogError($"Parse JSON error: {e.Message}");
                    ErrorMessage.AddDebug(Language.main.Get("ImportMarkersInvalidCode"));
                    return;
                }
                catch (Exception e)
                {
                    logger.LogError($"Parse JSON error: {e.Message}");
                    ErrorMessage.AddDebug(Language.main.Get("ImportMarkersInvalidCode"));
                    return;
                }

                var existsMarkersPos = new List<Vector3>();

                var gameSignals = UnityEngine.Object.FindObjectsOfType<SignalPing>();
                foreach (var gameSignal in gameSignals)
                {
                    if (!gameSignal.descriptionKey.StartsWith(EasyMarkers.Prefix))
                    {
                        continue;
                    }
                    existsMarkersPos.Add(new Vector3((float)Math.Round(gameSignal.transform.position.x, 2), (float)Math.Round(gameSignal.transform.position.y, 2), (float)Math.Round(gameSignal.transform.position.z, 2)));
                }

                int signalIndex = 0;

                foreach (var obj in signalsFromImport)
                {
                    signalIndex++;
                    try
                    {
                        JObject jObj = JObject.FromObject(obj);
                        string name = jObj["n"]?.ToString();
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }
                        JArray dArray = jObj["d"] as JArray;
                        if (dArray != null && dArray.Count == 4)
                        {
                            var position = new Vector3((float)dArray[0], (float)dArray[1], (float)dArray[2]);
                            if (position == default || position == null || position == Vector3.zero)
                            {
                                continue;
                            }
                            if (existsMarkersPos.Contains(position))
                            {
                                ErrorMessage.AddDebug(string.Format(Language.main.Get("ImportMarkersAlreadyExists"), signalIndex));
                                continue;
                            }
                            PlaceMarker(name, position, (int)dArray[3]);
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError($"Parse marker error: {e.Message}");
                    }
                }

            });
        }

        private static void PlaceMarker(string newMarkerName, Vector3 position, int colorIndex)
        {
            logger.LogInfo("place marker at " + position);

            OnGoalUnlockTracker goalTracker = UnityEngine.Object.FindObjectOfType<OnGoalUnlockTracker>();
            if (goalTracker == null || goalTracker.signalPrefab == null)
            {
                logger.LogError("Signal prefab not found!");
                return;
            }

            GameObject signalObject = UnityEngine.Object.Instantiate(goalTracker.signalPrefab, position, Quaternion.identity);

            SignalPing signal = signalObject.GetComponent<SignalPing>();
            if (signal != null)
            {
                signal.pos = position;
                signal.descriptionKey = EasyMarkers.Prefix + newMarkerName;

                PingInstance pingInstance = signalObject.GetComponent<PingInstance>();
                pingInstance.visitable = false;
                pingInstance.SetColor(colorIndex);
            }
        }
    }

    [HarmonyPatch(typeof(uGUI_PingEntry))]
    public class PingEntryPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("UpdateLabel", new[] { typeof(PingType), typeof(string) })]
        public static bool RenameMarkersInListPatch(uGUI_PingEntry __instance, PingType type, string name)
        {
            if (!string.IsNullOrEmpty(name) && name.StartsWith(EasyMarkers.Prefix))
            {
                __instance.label.text = name.Substring(EasyMarkers.Prefix.Length);
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch("Initialize", new[] { typeof(string), typeof(bool), typeof(PingType), typeof(string), typeof(int) })]
        public static void AddMarkerControlsPatch(uGUI_PingEntry __instance, string id, bool visible, PingType type, string name, int colorIndex)
        {
            if (__instance.transform == null || string.IsNullOrEmpty(id))
            {
                return;
            }

            RectTransform visibilityButtonRect = __instance.visibility.GetComponent<RectTransform>();

            if (visibilityButtonRect == null)
            {
                EasyMarkers.logger.LogError("Signal visibility button not found! " + id);
                return;
            }

            __instance.visibility.transform.localScale = 0.8f * Vector3.one;

            visibilityButtonRect.sizeDelta = new Vector2(visibilityButtonRect.sizeDelta.y, visibilityButtonRect.sizeDelta.y);

            bool isModMarker = name != null && name.StartsWith(EasyMarkers.Prefix);
            bool isBroken = type == PingType.None || (type == PingType.Signal && string.IsNullOrEmpty(name));

            if (!isModMarker && !isBroken)
            {
                return;
            }

            GameObject deleteMarkerButtonObj = GameObject.Instantiate(__instance.visibility.gameObject, __instance.visibility.transform.parent);
            deleteMarkerButtonObj.name = "DeleteButton_" + id;

            GameObject.DestroyImmediate(deleteMarkerButtonObj.GetComponent<Toggle>());
            Image[] allImagesOnDeleteButton = deleteMarkerButtonObj.GetComponentsInChildren<Image>();
            foreach (Image img in allImagesOnDeleteButton)
            {
                if (img.name == "Eye")
                {
                    GameObject.DestroyImmediate(img);
                }
            }

            Button deleteMarkerButton = deleteMarkerButtonObj.AddComponent<Button>();

            if (EasyMarkers.DeleteMarkerSprite != null)
            {
                Image newImage = deleteMarkerButton.GetComponent<Image>();
                newImage.sprite = EasyMarkers.DeleteMarkerSprite;
                newImage.preserveAspect = true;
                newImage.color = Color.white;
            }

            RectTransform deleteButtonRect = deleteMarkerButtonObj.GetComponent<RectTransform>();
            if (deleteButtonRect != null)
            {
                deleteButtonRect.anchoredPosition = new Vector2(
                    visibilityButtonRect.anchoredPosition.x - 55,
                    visibilityButtonRect.anchoredPosition.y
                );

                deleteButtonRect.sizeDelta = new Vector2(visibilityButtonRect.sizeDelta.y, visibilityButtonRect.sizeDelta.y);
            }

            deleteMarkerButton.onClick.AddListener(() => {
                PingInstance pingInstance = PingManager.Get(id);
                if (pingInstance == null)
                {
                    return;
                }
                uGUI_PDA.main.dialog.Show(Language.main.Get("DeleteMarkerConfirmDialogHeader"), delegate (int option)
                {
                    if (option == 1)
                    {
                        PingManager.Unregister(pingInstance);

                        PrefabIdentifier prefab = pingInstance.GetComponent<PrefabIdentifier>();
                        if (prefab != null)
                            UnityEngine.Object.Destroy(prefab);
                        SignalPing signal = pingInstance.GetComponent<SignalPing>();
                        if (signal != null)
                            UnityEngine.Object.Destroy(signal);
                        UnityEngine.Object.Destroy(pingInstance);
                        if (pingInstance.gameObject != null)
                            UnityEngine.Object.Destroy(pingInstance.gameObject);
                    }
                }, new string[]
                {
                    Language.main.Get("No"),
                    Language.main.Get("Yes")
                });
            });

            if (isModMarker)
            {
                GameObject renameMarkerButtonObj = GameObject.Instantiate(__instance.visibility.gameObject, __instance.visibility.transform.parent);
                renameMarkerButtonObj.name = "RenameButton_" + id;

                GameObject.DestroyImmediate(renameMarkerButtonObj.GetComponent<Toggle>());
                Image[] allImagesOnRenameButton = renameMarkerButtonObj.GetComponentsInChildren<Image>();
                foreach (Image img in allImagesOnRenameButton)
                {
                    if (img.name == "Eye")
                    {
                        GameObject.DestroyImmediate(img);
                    }
                }

                Button renameMarkerButton = renameMarkerButtonObj.AddComponent<Button>();

                if (EasyMarkers.RenameMarkerSprite != null)
                {
                    Image newImage = renameMarkerButton.GetComponent<Image>();
                    newImage.sprite = EasyMarkers.RenameMarkerSprite;
                    newImage.preserveAspect = true;
                    newImage.color = Color.white;
                }

                RectTransform renameButtonRect = renameMarkerButtonObj.GetComponent<RectTransform>();
                if (renameButtonRect != null)
                {
                    renameButtonRect.anchoredPosition = new Vector2(
                        visibilityButtonRect.anchoredPosition.x + 55,
                        visibilityButtonRect.anchoredPosition.y
                    );

                    renameButtonRect.sizeDelta = new Vector2(visibilityButtonRect.sizeDelta.y, visibilityButtonRect.sizeDelta.y);
                }

                renameMarkerButton.onClick.AddListener(() =>
                {
                    PingInstance pingInstance = PingManager.Get(id);

                    if (pingInstance == null)
                    {
                        return;
                    }

                    SignalPing signal = pingInstance.GetComponent<SignalPing>();
                    if (signal == null)
                    {
                        return;
                    }

                    uGUI.main.userInput.RequestString(Language.main.Get("InputMarkerNameHeader"), Language.main.Get("OK"), signal.descriptionKey.Substring(EasyMarkers.Prefix.Length), 256, (string markerLabel) =>
                    {
                        if (string.IsNullOrEmpty(markerLabel))
                        {
                            return;
                        }
                        signal.descriptionKey = EasyMarkers.Prefix + markerLabel;
                        pingInstance.SetLabel(EasyMarkers.Prefix + markerLabel);
                    });
                });
            }
        }
    }

    [HarmonyPatch(typeof(uGUI_Ping))]
    public class PingPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("UpdateText")]
        public static void RenameMarkersOnGUIPatch(uGUI_Ping __instance)
        {
            var field = typeof(uGUI_Ping).GetField("_label",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            string label = (string) field.GetValue(__instance);

            if (label.StartsWith(EasyMarkers.Prefix))
            {
                __instance.infoText.text = label.Substring(EasyMarkers.Prefix.Length);
            }
        }
    }

    [HarmonyPatch(typeof(SignalPing))]
    public class SignalPingPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnTriggerEnter", new[] { typeof(Collider) })]
        public static bool DisableVisitTriggerPatch(SignalPing __instance, Collider other)
        {
            if (__instance.descriptionKey.StartsWith(EasyMarkers.Prefix))
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(uGUI_PingTab))]
    public class PingTabPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch("Open")]
        public static void CreateAddMarkerButtonsPatch(uGUI_PingTab __instance)
        {
            if (__instance.visibilityToggle?.transform?.parent == null || __instance.visibilityToggle.transform.parent.Find("EasyMarkers_AddMarkerButton") != null)
            {
                return;
            }

            CreateAddMarkerButtons(__instance);
        }

        private static void CreateAddMarkerButtons(uGUI_PingTab __instance)
        {
            Toggle visibilityToggle = __instance.visibilityToggle;
            if (visibilityToggle == null)
            {
                return;
            }

            Transform parentTransform = visibilityToggle.transform.parent;
            if (parentTransform == null)
            {
                return;
            }

            // Add button

            RectTransform toggleVisibilityRect = visibilityToggle.GetComponent<RectTransform>();

            GameObject addMarkerButtonObj = GameObject.Instantiate(visibilityToggle.gameObject, parentTransform);
            addMarkerButtonObj.name = "EasyMarkers_AddMarkerButton";

            GameObject.DestroyImmediate(addMarkerButtonObj.GetComponent<Toggle>());
            Image[] allImagesOnButton = addMarkerButtonObj.GetComponentsInChildren<Image>();
            foreach (Image img in allImagesOnButton)
            {
                if (img.name == "Eye")
                {
                    GameObject.DestroyImmediate(img);
                }
            }

            Button addMarkerButton = addMarkerButtonObj.AddComponent<Button>();

            if (EasyMarkers.AddMarkerSprite != null)
            {
                Image newImage = addMarkerButton.GetComponent<Image>();
                newImage.sprite = EasyMarkers.AddMarkerSprite;
                newImage.preserveAspect = true;
                newImage.color = Color.white;
            }

            RectTransform rectTransform = addMarkerButtonObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                if (toggleVisibilityRect != null)
                {
                    rectTransform.anchoredPosition = new Vector2(
                        toggleVisibilityRect.anchoredPosition.x - 80,
                        toggleVisibilityRect.anchoredPosition.y
                    );

                    rectTransform.sizeDelta = new Vector2(toggleVisibilityRect.sizeDelta.y, toggleVisibilityRect.sizeDelta.y);
                }
            }

            SimpleTooltip tooltip = addMarkerButtonObj.AddComponent<SimpleTooltip>();
            tooltip.text = Language.main.Get("AddMarkerButtonTooltip");

            addMarkerButton.onClick.AddListener(() => {
                EasyMarkers.Create();
            });

            // Export button

            GameObject exportMarkersButtonObj = GameObject.Instantiate(visibilityToggle.gameObject, parentTransform);
            exportMarkersButtonObj.name = "EasyMarkers_ExportMarkersButton";

            GameObject.DestroyImmediate(exportMarkersButtonObj.GetComponent<Toggle>());
            Image[] allImagesOnExportButton = exportMarkersButtonObj.GetComponentsInChildren<Image>();
            foreach (Image img in allImagesOnExportButton)
                if (img.name == "Eye")
                    GameObject.DestroyImmediate(img);

            Button exportMarkersButton = exportMarkersButtonObj.AddComponent<Button>();

            if (EasyMarkers.SaveMarkersSprite != null)
            {
                Image newImage = exportMarkersButton.GetComponent<Image>();
                newImage.sprite = EasyMarkers.SaveMarkersSprite;
                newImage.preserveAspect = true;
                newImage.color = Color.white;
            }

            RectTransform exportRectTransform = exportMarkersButtonObj.GetComponent<RectTransform>();
            if (exportRectTransform != null)
            {
                if (toggleVisibilityRect != null)
                {
                    exportRectTransform.anchoredPosition = new Vector2(
                        toggleVisibilityRect.anchoredPosition.x,
                        toggleVisibilityRect.anchoredPosition.y + 80
                    );

                    exportRectTransform.sizeDelta = new Vector2(toggleVisibilityRect.sizeDelta.y, toggleVisibilityRect.sizeDelta.y);
                }
            }

            SimpleTooltip exportTooltip = exportMarkersButtonObj.AddComponent<SimpleTooltip>();
            exportTooltip.text = Language.main.Get("ExportMarkersTooltip");

            exportMarkersButton.onClick.AddListener(() => {
                EasyMarkers.Export();
            });

            // Import button

            GameObject importMarkersButtonObj = GameObject.Instantiate(visibilityToggle.gameObject, parentTransform);
            importMarkersButtonObj.name = "EasyMarkers_ImportMarkersButton";

            GameObject.DestroyImmediate(importMarkersButtonObj.GetComponent<Toggle>());
            Image[] allImagesOnImportButton = importMarkersButtonObj.GetComponentsInChildren<Image>();
            foreach (Image img in allImagesOnImportButton)
                if (img.name == "Eye")
                    GameObject.DestroyImmediate(img);

            Button importMarkersButton = importMarkersButtonObj.AddComponent<Button>();

            if (EasyMarkers.LoadMarkersSprite != null)
            {
                Image newImage = importMarkersButton.GetComponent<Image>();
                newImage.sprite = EasyMarkers.LoadMarkersSprite;
                newImage.preserveAspect = true;
                newImage.color = Color.white;
            }

            RectTransform importRectTransform = importMarkersButtonObj.GetComponent<RectTransform>();
            if (importRectTransform != null)
            {
                if (toggleVisibilityRect != null)
                {
                    importRectTransform.anchoredPosition = new Vector2(
                        toggleVisibilityRect.anchoredPosition.x - 80,
                        toggleVisibilityRect.anchoredPosition.y + 80
                    );

                    importRectTransform.sizeDelta = new Vector2(toggleVisibilityRect.sizeDelta.y, toggleVisibilityRect.sizeDelta.y);
                }
            }

            SimpleTooltip importTooltip = importMarkersButtonObj.AddComponent<SimpleTooltip>();
            importTooltip.text = Language.main.Get("ImportMarkersTooltip");

            importMarkersButton.onClick.AddListener(() => {
                EasyMarkers.Import();
            });
        }

        [HarmonyPostfix]
        [HarmonyPatch("UpdateEntries")]
        public static void SortMarkersPatch(uGUI_PingTab __instance)
        {
            var entries = Traverse.Create(__instance).Field("entries").GetValue<Dictionary<string, uGUI_PingEntry>>();

            var sortedEntries = new List<KeyValuePair<string, uGUI_PingEntry>>();

            foreach (var entry in entries)
            {
                var pingInstance = PingManager.Get(entry.Key);
                if (pingInstance != null)
                {
                    sortedEntries.Add(entry);
                }
            }

            sortedEntries = sortedEntries
                .OrderBy(x => (int)PingManager.Get(x.Key).pingType)
                .ThenBy(x => !PingManager.Get(x.Key).GetLabel()?.StartsWith("["))
                .ToList();

            for (int i = 0; i < sortedEntries.Count; i++)
            {
                sortedEntries[i].Value.rectTransform.SetSiblingIndex(i);
            }
        }
    }  
}
