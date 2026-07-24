using UnityEditor;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VRC.SDK3.Components;
using VRC.Udon;
using BenderDios.Idiomas;

/// <summary>
/// Inspector personalizado para configurar el InteractionLocalizer central.
/// </summary>
[CustomEditor(typeof(InteractionLocalizer))]
public class InteractionLocalizerEditor : Editor
{
    private enum EntryType
    {
        UdonInteraction,
        PickupInteraction,
        PickupUse
    }

    private class Entry
    {
        public Object target;
        public EntryType type;
        public string key;
        public bool enabled;
        public string text;
        public string path;
    }

    private SerializedProperty _manager;
    private SerializedProperty _baseLanguage;
    private SerializedProperty _interactTargets;
    private SerializedProperty _interactKeys;
    private SerializedProperty _interactEnabled;
    private SerializedProperty _pickupInteractionTargets;
    private SerializedProperty _pickupInteractionKeys;
    private SerializedProperty _pickupInteractionEnabled;
    private SerializedProperty _pickupUseTargets;
    private SerializedProperty _pickupUseKeys;
    private SerializedProperty _pickupUseEnabled;
    private string _filter = "";

    private static string S(string key) => IdiomasEditorStrings.Get(key);

    private void OnEnable()
    {
        _manager = serializedObject.FindProperty("manager");
        _baseLanguage = serializedObject.FindProperty("baseLanguage");
        _interactTargets = serializedObject.FindProperty("interactTargets");
        _interactKeys = serializedObject.FindProperty("interactKeys");
        _interactEnabled = serializedObject.FindProperty("interactEnabled");
        _pickupInteractionTargets =
            serializedObject.FindProperty("pickupInteractionTargets");
        _pickupInteractionKeys =
            serializedObject.FindProperty("pickupInteractionKeys");
        _pickupInteractionEnabled =
            serializedObject.FindProperty("pickupInteractionEnabled");
        _pickupUseTargets =
            serializedObject.FindProperty("pickupUseTargets");
        _pickupUseKeys = serializedObject.FindProperty("pickupUseKeys");
        _pickupUseEnabled =
            serializedObject.FindProperty("pickupUseEnabled");
    }

    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
        serializedObject.Update();
        EnsureEnabledArrays();

        EditorGUILayout.LabelField(
            "InteractionLocalizer", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _manager, new GUIContent(S("cl_manager")));
        DrawBaseLanguage();

        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.9f, 0.8f, 0.3f);
        if (GUILayout.Button(
            S("il_scan"), GUILayout.Height(30)))
        {
            ScanInteractionTexts();
        }
        GUI.backgroundColor = Color.white;

        List<Entry> entries = BuildEntries();
        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(S("il_export_help"), MessageType.Info);
        bool canExport = entries.Exists(
            entry => entry.enabled && !string.IsNullOrWhiteSpace(entry.key));
        EditorGUI.BeginDisabledGroup(!canExport);
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
        if (GUILayout.Button(S("cl_export_btn"), GUILayout.Height(30)))
            ExportToJsonAndApply(entries);
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        if (entries.Count == 0)
        {
            EditorGUILayout.HelpBox(S("il_no_texts"), MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        DrawActions(entries);
        DrawTable(entries);

        if (serializedObject.ApplyModifiedProperties())
            EditorUtility.SetDirty(target);
    }

    private void DrawBaseLanguage()
    {
        int index = IdiomasLanguages.IndexOf(_baseLanguage.stringValue);
        if (index < 0) index = IdiomasLanguages.IndexOf("en");
        int newIndex = EditorGUILayout.Popup(
            new GUIContent(S("cl_base_language")),
            index,
            IdiomasLanguages.PopupLabelsLatin);
        if (newIndex >= 0)
            _baseLanguage.stringValue = IdiomasLanguages.Codes[newIndex];
    }

    private void DrawActions(List<Entry> entries)
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(
            S("cl_include_all"),
            EditorStyles.miniButton,
            GUILayout.Width(90)))
        {
            SetAllEnabled(true);
            for (int i = 0; i < entries.Count; i++)
                entries[i].enabled = true;
        }
        if (GUILayout.Button(
            S("cl_exclude_all"),
            EditorStyles.miniButton,
            GUILayout.Width(90)))
        {
            SetAllEnabled(false);
            for (int i = 0; i < entries.Count; i++)
                entries[i].enabled = false;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(S("cl_filter"), GUILayout.Width(50));
        _filter = EditorGUILayout.TextField(_filter);
        EditorGUILayout.EndHorizontal();

        int visible = 0;
        int included = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (MatchesFilter(entries[i])) visible++;
            if (entries[i].enabled) included++;
        }
        EditorGUILayout.LabelField(
            $"{string.Format(S("cl_scan_results"), entries.Count)}  |  " +
            $"{S("cl_col_include")}: {included}  |  " +
            $"{S("cl_legend_excluded")}: {entries.Count - included}  |  " +
            $"{S("cl_filter")}: {visible}",
            EditorStyles.miniLabel);
    }

    private void DrawTable(List<Entry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (!MatchesFilter(entry)) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            bool enabled = EditorGUILayout.Toggle(
                entry.enabled, GUILayout.Width(30));
            if (enabled != entry.enabled)
            {
                entry.enabled = enabled;
                SetEntryEnabled(entry, enabled);
            }

            EditorGUILayout.LabelField(
                GetTypeLabel(entry.type),
                EditorStyles.miniBoldLabel,
                GUILayout.Width(155));
            EditorGUILayout.LabelField(
                entry.text, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(
                new GUIContent("◎", S("cl_ping_tooltip")),
                GUILayout.Width(24)))
            {
                EditorGUIUtility.PingObject(entry.target);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                S("cl_col_key"), GUILayout.Width(105));
            string key = EditorGUILayout.TextField(
                entry.key);
            if (key != entry.key)
            {
                entry.key = key;
                SetEntryKey(entry, key);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                S("cl_col_path"), GUILayout.Width(105));
            EditorGUILayout.SelectableLabel(
                entry.path,
                EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }

    private bool MatchesFilter(Entry entry)
    {
        if (string.IsNullOrWhiteSpace(_filter)) return true;
        string filter = _filter.ToLowerInvariant();
        Component component = entry.target as Component;
        return (component != null &&
                component.gameObject.name.ToLowerInvariant().Contains(filter)) ||
            entry.key.ToLowerInvariant().Contains(filter) ||
            entry.text.ToLowerInvariant().Contains(filter) ||
            entry.path.ToLowerInvariant().Contains(filter) ||
            GetTypeLabel(entry.type).ToLowerInvariant().Contains(filter);
    }

    private void ScanInteractionTexts()
    {
        serializedObject.ApplyModifiedProperties();
        Dictionary<string, Entry> existing = new Dictionary<string, Entry>();
        List<Entry> currentEntries = BuildEntries();
        for (int i = 0; i < currentEntries.Count; i++)
            existing[GetIdentity(currentEntries[i].target, currentEntries[i].type)] =
                currentEntries[i];

        List<Entry> scanned = new List<Entry>();
        HashSet<string> usedKeys = new HashSet<string>();
        for (int i = 0; i < currentEntries.Count; i++)
            if (!string.IsNullOrEmpty(currentEntries[i].key))
                usedKeys.Add(currentEntries[i].key);

        LocalizationManager manager =
            _manager.objectReferenceValue as LocalizationManager;
        bool includeDefaultUse = false;
        SerializedProperty excludedRoots = null;
        string[] excludedKeywords = new string[0];
        if (manager != null)
        {
            SerializedObject managerSO = new SerializedObject(manager);
            SerializedProperty option =
                managerSO.FindProperty("includeDefaultUseText");
            includeDefaultUse = option != null && option.boolValue;
            excludedRoots =
                managerSO.FindProperty("_excludedLocalizationRoots");
            excludedKeywords = GetExcludedKeywords(
                managerSO.FindProperty("_excludedLocalizationKeywords"));
            AddBaseLanguageJsonKeys(managerSO, usedKeys);
        }

        UdonBehaviour[] udonBehaviours =
            FindObjectsByType<UdonBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        System.Array.Sort(
            udonBehaviours,
            (a, b) => IdiomasEditorUtils.CompareStableComponents(a, b));
        for (int i = 0; i < udonBehaviours.Length; i++)
        {
            UdonBehaviour backing = udonBehaviours[i];
            UdonSharpBehaviour proxy =
                UdonSharpEditorUtility.GetProxyBehaviour(backing);
            if (proxy == null ||
                proxy is LocalizationManager ||
                proxy is CanvasLocalizer ||
                proxy is TextLocalizer ||
                proxy is InteractionLocalizer ||
                IsExcludedByGameObject(proxy.transform, excludedRoots) ||
                string.IsNullOrWhiteSpace(backing.InteractionText) ||
                (!includeDefaultUse && backing.InteractionText == "Use"))
            {
                continue;
            }
            AddScannedEntry(
                scanned,
                existing,
                usedKeys,
                proxy,
                EntryType.UdonInteraction,
                backing.InteractionText,
                ContainsExcludedKeyword(
                    backing.InteractionText, excludedKeywords));
        }

        VRCPickup[] pickups = FindObjectsByType<VRCPickup>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        System.Array.Sort(
            pickups,
            (a, b) => IdiomasEditorUtils.CompareStableComponents(a, b));
        for (int i = 0; i < pickups.Length; i++)
        {
            VRCPickup pickup = pickups[i];
            if (IsExcludedByGameObject(pickup.transform, excludedRoots))
                continue;
            if (!string.IsNullOrWhiteSpace(pickup.InteractionText))
            {
                AddScannedEntry(
                    scanned,
                    existing,
                    usedKeys,
                    pickup,
                    EntryType.PickupInteraction,
                    pickup.InteractionText,
                    ContainsExcludedKeyword(
                        pickup.InteractionText, excludedKeywords));
            }
            if (!string.IsNullOrWhiteSpace(pickup.UseText) &&
                (includeDefaultUse || pickup.UseText != "Use"))
            {
                AddScannedEntry(
                    scanned,
                    existing,
                    usedKeys,
                    pickup,
                    EntryType.PickupUse,
                    pickup.UseText,
                    ContainsExcludedKeyword(
                        pickup.UseText, excludedKeywords));
            }
        }

        scanned.Sort(
            (a, b) => string.CompareOrdinal(a.path, b.path));
        WriteEntries(scanned);
        serializedObject.ApplyModifiedProperties();
    }

    private void AddBaseLanguageJsonKeys(
        SerializedObject managerSO, HashSet<string> usedKeys)
    {
        SerializedProperty translationFile =
            managerSO.FindProperty("translationFile");
        TextAsset textAsset = translationFile != null
            ? translationFile.objectReferenceValue as TextAsset
            : null;
        if (textAsset == null) return;

        Dictionary<string, Dictionary<string, string>> translations =
            IdiomasEditorUtils.ParseJsonToDictionary(textAsset.text);
        string baseLang = _baseLanguage.stringValue;
        if (translations == null ||
            !translations.TryGetValue(
                baseLang, out Dictionary<string, string> baseTranslations))
        {
            return;
        }
        usedKeys.UnionWith(baseTranslations.Keys);
    }

    private static void AddScannedEntry(
        List<Entry> entries,
        Dictionary<string, Entry> existing,
        HashSet<string> usedKeys,
        Object targetObject,
        EntryType type,
        string text,
        bool excluded)
    {
        string identity = GetIdentity(targetObject, type);
        if (existing.TryGetValue(identity, out Entry previous))
        {
            previous.text = text;
            previous.path =
                GetAbsolutePath(((Component)targetObject).transform);
            entries.Add(previous);
            return;
        }

        Component component = (Component)targetObject;
        string suffix = type == EntryType.UdonInteraction
            ? "interaction"
            : type == EntryType.PickupInteraction
                ? "pickup_interaction"
                : "pickup_use";
        string baseKey =
            $"{suffix}_{IdiomasEditorUtils.NormalizeName(component.gameObject.name)}";
        string key = baseKey;
        int counter = 2;
        while (!usedKeys.Add(key))
            key = baseKey + "_" + counter++;

        entries.Add(new Entry
        {
            target = targetObject,
            type = type,
            key = key,
            enabled = !excluded,
            text = text,
            path = GetAbsolutePath(component.transform)
        });
    }

    private void ExportToJsonAndApply(List<Entry> entries)
    {
        LocalizationManager manager =
            _manager.objectReferenceValue as LocalizationManager;
        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                S("error_title"), S("cl_no_manager_msg"), S("ok"));
            return;
        }

        SerializedObject managerSO = new SerializedObject(manager);
        TextAsset textAsset = managerSO.FindProperty("translationFile")
            .objectReferenceValue as TextAsset;
        if (textAsset == null)
        {
            EditorUtility.DisplayDialog(
                S("no_file_title"), S("no_file_msg"), S("ok"));
            return;
        }

        string baseLang = _baseLanguage.stringValue;
        string assetPath = AssetDatabase.GetAssetPath(textAsset);
        string fullPath = Path.GetFullPath(assetPath);
        var translations = IdiomasEditorUtils.ParseJsonToDictionary(
            File.ReadAllText(fullPath, Encoding.UTF8));
        if (translations == null)
        {
            EditorUtility.DisplayDialog(
                S("error_title"), S("error_parse_json"), S("ok"));
            return;
        }
        if (!translations.ContainsKey(baseLang))
            translations[baseLang] = new Dictionary<string, string>();

        Dictionary<string, string> canonicalByText =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in translations[baseLang])
        {
            if (!string.IsNullOrEmpty(pair.Value) &&
                !canonicalByText.ContainsKey(pair.Value))
            {
                canonicalByText[pair.Value] = pair.Key;
            }
        }

        int exported = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (!entry.enabled || string.IsNullOrWhiteSpace(entry.key))
                continue;
            if (canonicalByText.TryGetValue(
                entry.text, out string canonicalKey))
            {
                entry.key = canonicalKey;
                SetEntryKey(entry, canonicalKey);
            }
            else
            {
                canonicalByText[entry.text] = entry.key;
                translations[baseLang][entry.key] = entry.text;
            }
            exported++;
        }

        File.WriteAllText(
            fullPath,
            IdiomasEditorUtils.WriteDictionaryToJson(translations),
            Encoding.UTF8);
        AssetDatabase.ImportAsset(
            assetPath, ImportAssetOptions.ForceUpdate);
        RegisterWithManager(managerSO);
        manager.ApplyToAll();
        Debug.Log(
            $"[Idiomas] Interaction Text exportado: {exported}, " +
            $"idioma base \"{baseLang}\".");
    }

    private void RegisterWithManager(SerializedObject managerSO)
    {
        SerializedProperty localizers =
            managerSO.FindProperty("interactionLocalizers");
        localizers.ClearArray();
        localizers.arraySize = 1;
        localizers.GetArrayElementAtIndex(0).objectReferenceValue = target;
        managerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(managerSO.targetObject);
    }

    private List<Entry> BuildEntries()
    {
        List<Entry> entries = new List<Entry>();
        AddEntries(
            entries,
            _interactTargets,
            _interactKeys,
            _interactEnabled,
            EntryType.UdonInteraction);
        AddEntries(
            entries,
            _pickupInteractionTargets,
            _pickupInteractionKeys,
            _pickupInteractionEnabled,
            EntryType.PickupInteraction);
        AddEntries(
            entries,
            _pickupUseTargets,
            _pickupUseKeys,
            _pickupUseEnabled,
            EntryType.PickupUse);
        return entries;
    }

    private static void AddEntries(
        List<Entry> entries,
        SerializedProperty targets,
        SerializedProperty keys,
        SerializedProperty enabled,
        EntryType type)
    {
        int count = Mathf.Min(targets.arraySize, keys.arraySize);
        for (int i = 0; i < count; i++)
        {
            Object targetObject =
                targets.GetArrayElementAtIndex(i).objectReferenceValue;
            Component component = targetObject as Component;
            if (component == null) continue;
            entries.Add(new Entry
            {
                target = targetObject,
                type = type,
                key = keys.GetArrayElementAtIndex(i).stringValue,
                enabled = i >= enabled.arraySize ||
                    enabled.GetArrayElementAtIndex(i).boolValue,
                text = GetCurrentText(targetObject, type),
                path = GetAbsolutePath(component.transform)
            });
        }
    }

    private static string GetCurrentText(
        Object targetObject, EntryType type)
    {
        if (type == EntryType.UdonInteraction)
        {
            UdonSharpBehaviour proxy = targetObject as UdonSharpBehaviour;
            UdonBehaviour backing = proxy != null
                ? IdiomasEditorUtils.FindUdonBehaviourFor(proxy)
                : null;
            return backing != null ? backing.InteractionText : "";
        }
        VRCPickup pickup = targetObject as VRCPickup;
        if (pickup == null) return "";
        return type == EntryType.PickupUse
            ? pickup.UseText
            : pickup.InteractionText;
    }

    private void WriteEntries(List<Entry> entries)
    {
        List<Entry> udon = entries.FindAll(
            entry => entry.type == EntryType.UdonInteraction);
        List<Entry> pickupInteraction = entries.FindAll(
            entry => entry.type == EntryType.PickupInteraction);
        List<Entry> pickupUse = entries.FindAll(
            entry => entry.type == EntryType.PickupUse);
        WriteEntryArray(
            udon, _interactTargets, _interactKeys, _interactEnabled);
        WriteEntryArray(
            pickupInteraction,
            _pickupInteractionTargets,
            _pickupInteractionKeys,
            _pickupInteractionEnabled);
        WriteEntryArray(
            pickupUse,
            _pickupUseTargets,
            _pickupUseKeys,
            _pickupUseEnabled);
    }

    private static void WriteEntryArray(
        List<Entry> entries,
        SerializedProperty targets,
        SerializedProperty keys,
        SerializedProperty enabled)
    {
        targets.arraySize = entries.Count;
        keys.arraySize = entries.Count;
        enabled.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            targets.GetArrayElementAtIndex(i).objectReferenceValue =
                entries[i].target;
            keys.GetArrayElementAtIndex(i).stringValue = entries[i].key;
            enabled.GetArrayElementAtIndex(i).boolValue =
                entries[i].enabled;
        }
    }

    private void SetAllEnabled(bool value)
    {
        SetEnabledArray(_interactEnabled, value);
        SetEnabledArray(_pickupInteractionEnabled, value);
        SetEnabledArray(_pickupUseEnabled, value);
    }

    private static void SetEnabledArray(
        SerializedProperty property, bool value)
    {
        for (int i = 0; i < property.arraySize; i++)
            property.GetArrayElementAtIndex(i).boolValue = value;
    }

    private void SetEntryEnabled(Entry entry, bool value)
    {
        FindEntryProperties(
            entry, out SerializedProperty key, out SerializedProperty enabled);
        if (enabled != null) enabled.boolValue = value;
    }

    private void SetEntryKey(Entry entry, string value)
    {
        FindEntryProperties(
            entry, out SerializedProperty key, out SerializedProperty enabled);
        if (key != null) key.stringValue = value;
    }

    private void FindEntryProperties(
        Entry entry,
        out SerializedProperty key,
        out SerializedProperty enabled)
    {
        SerializedProperty targets;
        if (entry.type == EntryType.UdonInteraction)
        {
            targets = _interactTargets;
            key = _interactKeys;
            enabled = _interactEnabled;
        }
        else if (entry.type == EntryType.PickupInteraction)
        {
            targets = _pickupInteractionTargets;
            key = _pickupInteractionKeys;
            enabled = _pickupInteractionEnabled;
        }
        else
        {
            targets = _pickupUseTargets;
            key = _pickupUseKeys;
            enabled = _pickupUseEnabled;
        }

        for (int i = 0; i < targets.arraySize; i++)
        {
            if (targets.GetArrayElementAtIndex(i).objectReferenceValue ==
                entry.target)
            {
                key = key.GetArrayElementAtIndex(i);
                enabled = enabled.GetArrayElementAtIndex(i);
                return;
            }
        }
        key = null;
        enabled = null;
    }

    private void EnsureEnabledArrays()
    {
        EnsureEnabledArray(_interactEnabled, _interactKeys.arraySize);
        EnsureEnabledArray(
            _pickupInteractionEnabled,
            _pickupInteractionKeys.arraySize);
        EnsureEnabledArray(_pickupUseEnabled, _pickupUseKeys.arraySize);
    }

    private static void EnsureEnabledArray(
        SerializedProperty property, int count)
    {
        int oldSize = property.arraySize;
        if (oldSize >= count) return;
        property.arraySize = count;
        for (int i = oldSize; i < count; i++)
            property.GetArrayElementAtIndex(i).boolValue = true;
    }

    private static string GetIdentity(Object targetObject, EntryType type)
    {
        return targetObject.GetInstanceID() + ":" + (int)type;
    }

    private static string GetTypeLabel(EntryType type)
    {
        if (type == EntryType.PickupInteraction)
            return "Pickup Interaction Text";
        if (type == EntryType.PickupUse)
            return "Pickup Use Text";
        return "Udon Interaction Text";
    }

    private static string GetAbsolutePath(Transform transform)
    {
        string path = "/" + transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = "/" + parent.name + path;
            parent = parent.parent;
        }
        return path;
    }

    private static bool IsExcludedByGameObject(
        Transform targetTransform, SerializedProperty excludedRoots)
    {
        if (targetTransform == null || excludedRoots == null) return false;
        for (int i = 0; i < excludedRoots.arraySize; i++)
        {
            GameObject root = excludedRoots.GetArrayElementAtIndex(i)
                .objectReferenceValue as GameObject;
            if (root == null) continue;
            if (targetTransform == root.transform ||
                targetTransform.IsChildOf(root.transform))
            {
                return true;
            }
        }
        return false;
    }

    private static string[] GetExcludedKeywords(
        SerializedProperty keywordsProperty)
    {
        if (keywordsProperty == null) return new string[0];
        List<string> keywords = new List<string>();
        for (int i = 0; i < keywordsProperty.arraySize; i++)
        {
            string keyword = keywordsProperty
                .GetArrayElementAtIndex(i).stringValue;
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            keywords.Add(keyword.Trim());
        }
        return keywords.ToArray();
    }

    private static bool ContainsExcludedKeyword(
        string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null) return false;
        for (int i = 0; i < keywords.Length; i++)
        {
            string keyword = keywords[i].Trim();
            if (string.IsNullOrEmpty(keyword)) continue;
            if (text.IndexOf(
                keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }
}
