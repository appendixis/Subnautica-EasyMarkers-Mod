using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Handlers;
using Nautilus.Utility;
using Story;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace EasyMarkers
{
    [BepInPlugin(Guid, Name, Version)]
    public class EasyMarkers : BaseUnityPlugin
    {
        public const string Guid = "com.appendixis.easymarkers";
        public const string Name = "EasyMarkers";
        public const string Version = "1.0.1";

        public static string Prefix = "[EasyMarkers] ";
        public static EasyMarkers Instance;

        public static ManualLogSource logger;

        public static Sprite AddMarkerSprite;
        public static Sprite RenameMarkerSprite;
        public static Sprite DeleteMarkerSprite;


        private void Awake()
        {
            Instance = this;
            logger = Logger;

            LanguageHandler.RegisterLocalizationFolder();

            logger.LogInfo("EasyMarkers Mod loaded!");

            string AddMarkerIconPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "AddPingButton.png");
            if (!File.Exists(AddMarkerIconPath))
            {
                EasyMarkers.logger.LogError($"file not found: {AddMarkerIconPath}");
            }
            else
            {
                AddMarkerSprite = ImageUtils.LoadSpriteFromFile(AddMarkerIconPath);
            }

            string RenameMarkerIconPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "RenamePingButton.png");
            if (!File.Exists(RenameMarkerIconPath))
            {
                EasyMarkers.logger.LogError($"file not found: {RenameMarkerIconPath}");
            }
            else
            {
                RenameMarkerSprite = ImageUtils.LoadSpriteFromFile(RenameMarkerIconPath);
            }

            string DeleteMarkerIconPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", "DeletePingButton.png");
            if (!File.Exists(DeleteMarkerIconPath))
            {
                EasyMarkers.logger.LogError($"file not found: {DeleteMarkerIconPath}");
            }
            else
            {
                DeleteMarkerSprite = ImageUtils.LoadSpriteFromFile(DeleteMarkerIconPath);
            }

            Harmony.CreateAndPatchAll(typeof(PingEntryPatch)); // rename markers in list / add marker controls
            Harmony.CreateAndPatchAll(typeof(PingPatch)); // rename markers on GUI
            Harmony.CreateAndPatchAll(typeof(SignalPingPatch)); // disable visit trigger
            Harmony.CreateAndPatchAll(typeof(PingTabPatch)); // add UI / sort markers
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

            bool isModMarkerOrBroken = type == PingType.None || (type == PingType.Signal && string.IsNullOrEmpty(name)) || (name != null && name.StartsWith(EasyMarkers.Prefix));

            if (!isModMarkerOrBroken)
            {
                return;
            }

            RectTransform visibilityButtonRect = __instance.visibility.GetComponent<RectTransform>();

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
                if (visibilityButtonRect != null)
                {
                    deleteButtonRect.anchoredPosition = new Vector2(
                        visibilityButtonRect.anchoredPosition.x - 64,
                        visibilityButtonRect.anchoredPosition.y
                    );

                    deleteButtonRect.sizeDelta = new Vector2(visibilityButtonRect.sizeDelta.y - 10, visibilityButtonRect.sizeDelta.y - 10);
                }
            }

            deleteMarkerButton.onClick.AddListener(() => {
                PingInstance pingInstance = PingManager.Get(id);
                if (pingInstance == null)
                {
                    return;
                }
                uGUI.main.confirmation.Show(
                    Language.main.Get("DeleteConfirmDialogHeader"),
                    (bool confirmed) => {
                        if (confirmed)
                        {
                            PingManager.Unregister(pingInstance);
                            PrefabIdentifier prefab = pingInstance.GetComponent<PrefabIdentifier>();
                            if (prefab != null)
                            {
                                UnityEngine.Object.Destroy(prefab);
                            }
                            UnityEngine.Object.Destroy(pingInstance);
                        }
                    }
                );
            });

            if (name == null || type == PingType.None)
            {
                return;
            }

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
                if (visibilityButtonRect != null)
                {
                    renameButtonRect.anchoredPosition = new Vector2(
                        visibilityButtonRect.anchoredPosition.x + 64,
                        visibilityButtonRect.anchoredPosition.y
                    );

                    renameButtonRect.sizeDelta = new Vector2(visibilityButtonRect.sizeDelta.y - 10, visibilityButtonRect.sizeDelta.y - 10);
                }
            }

            renameMarkerButton.onClick.AddListener(() =>
            {
                PingInstance pingInstance = PingManager.Get(id);

                if (pingInstance == null)
                {
                    return;
                }

                uGUI.main.userInput.RequestString(Language.main.Get("InputMarkerNameHeader"), "OK", name.Substring(EasyMarkers.Prefix.Length), 25, (string markerLabel) =>
                {
                    if (string.IsNullOrEmpty(markerLabel))
                    {
                        return;
                    }
                    SignalPing signal = pingInstance.GetComponent<SignalPing>();
                    if (signal != null)
                    {
                        signal.descriptionKey = EasyMarkers.Prefix + markerLabel;
                        pingInstance.SetLabel(EasyMarkers.Prefix + markerLabel);
                    }
                });
            });
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
        public static void CreateAddMarkerButtonPatch(uGUI_PingTab __instance)
        {
            if (__instance.visibilityToggle?.transform?.parent == null || __instance.visibilityToggle.transform.parent.Find("EasyMarkers_AddMarkerButton") != null)
            {
                return;
            }

            CreateAddMarkerButton(__instance);
        }

        private static void CreateAddMarkerButton(uGUI_PingTab __instance)
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

            if (EasyMarkers.AddMarkerSprite != null) {
                Image newImage = addMarkerButton.GetComponent<Image>();
                newImage.sprite = EasyMarkers.AddMarkerSprite;
                newImage.preserveAspect = true;
                newImage.color = Color.white;
            }

            RectTransform rectTransform = addMarkerButtonObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                RectTransform toggleRect = visibilityToggle.GetComponent<RectTransform>();
                if (toggleRect != null)
                {
                    rectTransform.anchoredPosition = new Vector2(
                        toggleRect.anchoredPosition.x - 80,
                        toggleRect.anchoredPosition.y
                    );

                    rectTransform.sizeDelta = new Vector2(toggleRect.sizeDelta.y, toggleRect.sizeDelta.y);
                }
            }

            addMarkerButton.onClick.AddListener(CreateNewMarkerAtPlayer);
        }

        private static void CreateNewMarkerAtPlayer()
        {
            if (Player.main == null)
            {
                return;
            }

            OnGoalUnlockTracker goalTracker = UnityEngine.Object.FindObjectOfType<OnGoalUnlockTracker>();
            if (goalTracker == null || goalTracker.signalPrefab == null)
            {
                EasyMarkers.logger.LogError("Signal prefab not found!");
                return;
            }

            int depth = (int) (Ocean.GetOceanLevel() - Player.main.transform.position.y);

            uGUI.main.userInput.RequestString(Language.main.Get("InputMarkerNameHeader"), "OK", string.Format(Language.main.Get("InputDefaultName"), (depth < 0 ? "+" + (depth * -1).ToString() : depth.ToString())), 25, (string markerLabel) => {
                if (string.IsNullOrEmpty(markerLabel))
                {
                    return;
                }

                GameObject signalObject = UnityEngine.Object.Instantiate(goalTracker.signalPrefab, Player.main.transform.position, Quaternion.identity);

                SignalPing signal = signalObject.GetComponent<SignalPing>();
                if (signal != null)
                {
                    signal.pos = Player.main.transform.position;
                    signal.descriptionKey = EasyMarkers.Prefix + markerLabel;

                    PingInstance pingInstance = signalObject.GetComponent<PingInstance>();
                    pingInstance.visitable = false;
                    pingInstance.SetColor(4);
                }
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
