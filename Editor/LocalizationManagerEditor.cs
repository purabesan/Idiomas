using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VRC.SDK3.Data;
using VRC.SDK3.Components;
using VRC.Udon;
using BenderDios.Idiomas;

/// <summary>
/// Inspector personalizado para LocalizationManager.
/// Orden visual: Config basica > Estadisticas > Dropdown > Listeners > Localizers > Canvas > Herramientas
/// </summary>
[CustomEditor(typeof(LocalizationManager))]
public class LocalizationManagerEditor : Editor
{
    // Propiedades serializadas
    private SerializedProperty _translationFile;
    private SerializedProperty _fallbackLanguage;
    private SerializedProperty _localizers;
    private SerializedProperty _canvasLocalizers;
    private SerializedProperty _excludedLocalizationRoots;
    private SerializedProperty _excludedLocalizationKeywords;
    private SerializedProperty _includeInteractionTexts;
    private SerializedProperty _includeDefaultUseText;
    private SerializedProperty _interactionLocalizers;
    private SerializedProperty _languageDropdown;
    private SerializedProperty _dropdownLanguageCodes;
    private SerializedProperty _listeners;

    // Estado del editor (foldouts)
    private bool _showCanvasSearch = true;
    private bool _showExclusionConditions = false;
    private bool _showCanvasResults = false;
    private bool _showInteractionResults = false;
    private bool _showTools = false;
    private bool _showListeners = false;
    private bool _showDropdown = false;
    private bool _showPreview = false;
    private string _previewLanguage = "en";
    private static List<CanvasSearchResult> _canvasSearchResults;
    private static List<InteractionSearchResult> _interactionSearchResults;
    private static int _interactionDetectedObjectCount;
    private static int _interactionRegisteredObjectCount;
    private const string QUICK_SETUP_LANGUAGE_SESSION_KEY =
        "Idiomas.QuickSetupBaseLanguage";
    private string _quickSetupBaseLanguage = "en";

    // Cache del JSON
    private DataDictionary _cachedData;
    private string _cachedJsonHash;
    private string[] _cachedLanguages;
    private string[] _cachedKeys;
    private Dictionary<string, Dictionary<string, string>>
        _cachedTranslations;
    private Dictionary<string, int> _sceneKeyReferenceCounts;
    private bool _sceneKeyReferenceCountsDirty = true;
    private TranslationFileStamp _translationFileStamp;
    private bool _hasTranslationFileStamp;
    private bool _translationFileStateDirty = true;
    private ScanResultSummary _scanResultSummary;
    private List<InteractionPendingRowSummary> _interactionPendingRows;
    private List<InteractionPendingRowSummary> _interactionExcludedRows;
    private List<InteractionRegisteredRowSummary> _interactionRegisteredRows;

    private struct TranslationFileStamp
    {
        public string assetPath;
        public string guid;
        public Hash128 dependencyHash;
        public long creationTimeTicks;
        public long lastWriteTimeTicks;
        public long fileLength;
        public int instanceId;
    }

    private class ScanResultSummary
    {
        public int canvasCandidateCount;
        public int canvasCandidateTextCount;
        public int localizedCanvasCount;
        public int noTextCanvasCount;
        public int interactionTextCount;
        public int interactionObjectCount;
        public bool hasInvalidInteractionKeys;
    }

    private class InteractionPendingRowSummary
    {
        public GameObject owner;
        public int includedCount;
        public int excludedCount;
    }

    private class InteractionRegisteredRowSummary
    {
        public GameObject owner;
        public int textCount;
        public int cleanCount;
        public int modifiedCount;
        public int missingCount;
        public int sharedKeyCount;
    }

    // Shortcut para traducciones del editor
    private static string S(string key) => IdiomasEditorStrings.Get(key);

    /// <summary>
    /// Dibuja el selector de idioma del editor (dropdown) al inicio del inspector.
    /// </summary>
    private void DrawEditorLanguageSelector()
    {
        string current = IdiomasEditorStrings.CurrentLanguage;
        int currentIdx = IdiomasLanguages.IndexOf(current);
        if (currentIdx < 0) currentIdx = IdiomasLanguages.IndexOf("es");

        EditorGUILayout.BeginHorizontal();
        int newIdx = EditorGUILayout.Popup(
            new GUIContent(S("editor_language"), S("editor_language_tooltip")),
            currentIdx, IdiomasLanguages.PopupLabels);
        EditorGUILayout.EndHorizontal();

        if (newIdx >= 0 && newIdx != currentIdx)
        {
            IdiomasEditorStrings.CurrentLanguage = IdiomasLanguages.Codes[newIdx];
            // Repintar todos los inspectors abiertos
            foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors)
            {
                if (editor != null) editor.Repaint();
            }
        }
    }



    private void OnEnable()
    {
        _translationFile = serializedObject.FindProperty("translationFile");
        _fallbackLanguage = serializedObject.FindProperty("fallbackLanguage");
        _localizers = serializedObject.FindProperty("localizers");
        _canvasLocalizers = serializedObject.FindProperty("canvasLocalizers");
        _excludedLocalizationRoots = serializedObject.FindProperty("_excludedLocalizationRoots");
        _excludedLocalizationKeywords =
            serializedObject.FindProperty("_excludedLocalizationKeywords");
        _includeInteractionTexts =
            serializedObject.FindProperty("includeInteractionTexts");
        _includeDefaultUseText =
            serializedObject.FindProperty("includeDefaultUseText");
        _interactionLocalizers =
            serializedObject.FindProperty("interactionLocalizers");
        _languageDropdown = serializedObject.FindProperty("_languageDropdown");
        _dropdownLanguageCodes = serializedObject.FindProperty("_dropdownLanguageCodes");
        _listeners = serializedObject.FindProperty("_listeners");
        MigrateLegacyExcludedKeywords();
        _quickSetupBaseLanguage = SessionState.GetString(
            QUICK_SETUP_LANGUAGE_SESSION_KEY, "en");
        if (IdiomasLanguages.IndexOf(_quickSetupBaseLanguage) < 0)
            _quickSetupBaseLanguage = "en";

        // Limpiar resultados de escaneo anteriores para evitar
        // MissingReferenceException por GameObjects destruidos
        // (la lista es static y puede sobrevivir entre recargas)
        _canvasSearchResults = null;
        _interactionSearchResults = null;
        _interactionDetectedObjectCount = 0;
        _interactionRegisteredObjectCount = 0;
        _scanResultSummary = null;
        _interactionPendingRows = null;
        _interactionExcludedRows = null;
        _interactionRegisteredRows = null;
        InvalidateSceneKeyReferenceCounts();
        EditorApplication.hierarchyChanged +=
            InvalidateSceneKeyReferenceCounts;
        EditorApplication.projectChanged +=
            MarkTranslationFileStateDirty;
        Undo.undoRedoPerformed += InvalidateSceneKeyReferenceCounts;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -=
            InvalidateSceneKeyReferenceCounts;
        EditorApplication.projectChanged -=
            MarkTranslationFileStateDirty;
        Undo.undoRedoPerformed -= InvalidateSceneKeyReferenceCounts;
    }

    private void MigrateLegacyExcludedKeywords()
    {
        if (_excludedLocalizationKeywords == null ||
            _excludedLocalizationKeywords.arraySize > 0)
        {
            return;
        }

        const string sessionKey = "Idiomas.QuickSetupExcludedKeywords";
        string legacyValue = SessionState.GetString(sessionKey, "");
        if (string.IsNullOrWhiteSpace(legacyValue)) return;

        string[] values = legacyValue.Split(
            new[] { '\r', '\n' },
            System.StringSplitOptions.RemoveEmptyEntries);
        HashSet<string> unique = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Length; i++)
        {
            string keyword = values[i].Trim();
            if (string.IsNullOrEmpty(keyword) || !unique.Add(keyword))
                continue;
            int index = _excludedLocalizationKeywords.arraySize;
            _excludedLocalizationKeywords.InsertArrayElementAtIndex(index);
            _excludedLocalizationKeywords.GetArrayElementAtIndex(index)
                .stringValue = keyword;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        SessionState.EraseString(sessionKey);
    }

    public override void OnInspectorGUI()
    {
        // Cabecera estandar de UdonSharp (program asset, sync settings, etc.)
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        serializedObject.Update();

        // === IDIOMA DEL EDITOR ===
        EditorGUILayout.Space(5);
        DrawEditorLanguageSelector();
        EditorGUILayout.Space(3);

        // === TITULO ===
        EditorGUILayout.LabelField(S("mgr_title"), EditorStyles.boldLabel);
        EditorGUILayout.Space(3);

        // === CONFIGURACION BASICA ===
        EditorGUILayout.BeginHorizontal();
        Object previousTranslationFile =
            _translationFile.objectReferenceValue;
        EditorGUILayout.PropertyField(_translationFile,
            new GUIContent(S("mgr_translation_file")));
        if (_translationFile.objectReferenceValue !=
            previousTranslationFile)
        {
            ResetTranslationState();
        }

        bool hasJson = _translationFile.objectReferenceValue != null;
        string createBtnLabel = hasJson ? S("mgr_create_new") : S("mgr_create_json");
        string createBtnTooltip = hasJson
            ? S("mgr_create_new_tooltip")
            : S("mgr_create_json_tooltip");

        if (GUILayout.Button(new GUIContent(createBtnLabel, createBtnTooltip),
            GUILayout.Width(82), GUILayout.Height(18)))
        {
            CreateTranslationJsonFile();
        }
        EditorGUILayout.EndHorizontal();

        // Idioma de Fallback como dropdown
        string currentFallback = _fallbackLanguage.stringValue;
        int fbIndex = -1;
        for (int i = 0; i < IdiomasLanguages.Codes.Length; i++)
        {
            if (IdiomasLanguages.Codes[i] == currentFallback) { fbIndex = i; break; }
        }
        int newFbIndex = EditorGUILayout.Popup(
            new GUIContent(S("mgr_fallback_language"), S("mgr_fallback_tooltip")),
            fbIndex, IdiomasLanguages.PopupLabelsLatin);
        if (newFbIndex >= 0 && newFbIndex != fbIndex)
        {
            _fallbackLanguage.stringValue = IdiomasLanguages.Codes[newFbIndex];
        }

        RefreshCache();

        // === INFO: FONTS CJK ===
        // Los caracteres CJK (japones, coreano, chino) y cirilico (ruso)
        // se renderizan automaticamente con las fuentes internas de VRChat en runtime.
        // No es necesario configurar fallback fonts en TMP Settings.

        // === ESTADISTICAS ===
        EditorGUILayout.Space(8);
        if (_cachedLanguages != null && _cachedLanguages.Length > 0)
        {
            int listenerCount = _listeners != null ? _listeners.arraySize : 0;
            EditorGUILayout.LabelField(
                $"{S("mgr_languages")}: {_cachedLanguages.Length} ({string.Join(", ", _cachedLanguages)})",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField(
                $"{S("mgr_keys")}: {(_cachedKeys != null ? _cachedKeys.Length : 0)}  |  " +
                $"{S("mgr_canvas")}: {_canvasLocalizers.arraySize}  |  " +
                $"{S("mgr_texts")}: {_localizers.arraySize}  |  " +
                $"{S("mgr_listeners_label")}: {listenerCount}",
                EditorStyles.helpBox);
        }

        // === TEXTOS DE INTERACCION ===
        EditorGUILayout.Space(5);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            _includeInteractionTexts,
            new GUIContent("Interaction Text"));
        EditorGUI.BeginDisabledGroup(!_includeInteractionTexts.boolValue);
        EditorGUILayout.PropertyField(
            _includeDefaultUseText,
            new GUIContent("Default \"Use\""));
        EditorGUI.EndDisabledGroup();
        if (EditorGUI.EndChangeCheck())
        {
            _canvasSearchResults = null;
            _interactionSearchResults = null;
            _interactionDetectedObjectCount = 0;
            _interactionRegisteredObjectCount = 0;
            _scanResultSummary = null;
            _interactionPendingRows = null;
            _interactionExcludedRows = null;
            _interactionRegisteredRows = null;
        }
        EditorGUILayout.HelpBox(S("mgr_interaction_info"), MessageType.Info);

        // === CONDICIONES DE EXCLUSION ===
        EditorGUILayout.Space(5);
        _showExclusionConditions = EditorGUILayout.Foldout(
            _showExclusionConditions, S("mgr_exclusion_conditions"), true,
            EditorStyles.foldoutHeader);
        if (_showExclusionConditions)
        {
            EditorGUI.indentLevel++;
            DrawExclusionConditions();
            EditorGUI.indentLevel--;
        }

        // === BUSCAR TEXTOS SIN LOCALIZAR ===
        EditorGUILayout.Space(5);
        _showCanvasSearch = EditorGUILayout.Foldout(_showCanvasSearch,
            S("mgr_search_canvas"), true, EditorStyles.foldoutHeader);
        if (_showCanvasSearch)
        {
            EditorGUI.indentLevel++;
            DrawCanvasSearch();
            EditorGUI.indentLevel--;
        }

        // === TRADUCCION (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showTools = EditorGUILayout.Foldout(_showTools,
            S("mgr_translation"), true, EditorStyles.foldoutHeader);
        if (_showTools)
        {
            EditorGUI.indentLevel++;

            // Auto-Traducir
            EditorGUILayout.Space(3);
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button(S("mgr_auto_translate"), GUILayout.Height(24)))
            {
                OpenAutoTranslateWindow();
            }
            GUI.backgroundColor = Color.white;

            // Vista Previa de Traducciones
            EditorGUILayout.Space(3);
            _showPreview = EditorGUILayout.Foldout(_showPreview,
                S("mgr_preview"), true);
            if (_showPreview)
            {
                DrawPreview();
            }

            EditorGUI.indentLevel--;
        }

        // === LISTENERS (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showListeners = EditorGUILayout.Foldout(_showListeners,
            string.Format(S("mgr_listeners_title"), _listeners != null ? _listeners.arraySize : 0),
            true, EditorStyles.foldoutHeader);
        if (_showListeners)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(S("mgr_listeners_help"), MessageType.Info);

            EditorGUILayout.Space(3);
            EditorGUILayout.PropertyField(_listeners,
                new GUIContent(S("mgr_listeners_label")), true);

            EditorGUI.indentLevel--;
        }

        // === SELECTOR DE IDIOMA - DROPDOWN (colapsado por defecto) ===
        EditorGUILayout.Space(5);
        _showDropdown = EditorGUILayout.Foldout(_showDropdown,
            S("mgr_dropdown_title"), true, EditorStyles.foldoutHeader);
        if (_showDropdown)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(S("mgr_dropdown_help"), MessageType.Info);

            EditorGUILayout.Space(3);
            EditorGUILayout.PropertyField(_languageDropdown,
                new GUIContent(S("mgr_dropdown_field"), S("mgr_dropdown_field_tooltip")));

            if (_languageDropdown.objectReferenceValue != null)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(S("mgr_dropdown_codes_help"), MessageType.None);

                EditorGUILayout.PropertyField(_dropdownLanguageCodes,
                    new GUIContent(S("mgr_dropdown_codes")), true);

                TMP_Dropdown dd = _languageDropdown.objectReferenceValue as TMP_Dropdown;
                if (dd != null)
                {
                    SerializedObject ddSO = new SerializedObject(dd);
                    SerializedProperty onVal = ddSO.FindProperty("m_OnValueChanged");
                    SerializedProperty calls = onVal.FindPropertyRelative("m_PersistentCalls.m_Calls");

                    if (calls.arraySize == 0)
                    {
                        EditorGUILayout.Space(3);
                        GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
                        if (GUILayout.Button(S("mgr_dropdown_connect"), GUILayout.Height(26)))
                        {
                            WireDropdown(dd);
                        }
                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.HelpBox(S("mgr_dropdown_not_connected"), MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(S("mgr_dropdown_connected"), MessageType.Info);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    // =====================================================================
    // Conectar Dropdown
    // =====================================================================

    private void WireDropdown(TMP_Dropdown dropdown)
    {
        LocalizationManager mgr = (LocalizationManager)target;
        UdonBehaviour mgrUdon = IdiomasEditorUtils.FindUdonBehaviourFor(mgr);

        if (mgrUdon == null)
        {
            EditorUtility.DisplayDialog(S("mgr_dropdown_wire_error_title"),
                S("mgr_dropdown_wire_error_msg"), S("ok"));
            return;
        }

        SerializedObject ddSO = new SerializedObject(dropdown);
        SerializedProperty onVal = ddSO.FindProperty("m_OnValueChanged");
        SerializedProperty calls = onVal.FindPropertyRelative("m_PersistentCalls.m_Calls");

        calls.ClearArray();
        calls.arraySize = 1;
        SerializedProperty entry = calls.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("m_Target").objectReferenceValue = mgrUdon;
        entry.FindPropertyRelative("m_MethodName").stringValue = "SendCustomEvent";
        entry.FindPropertyRelative("m_Mode").intValue = 5;
        entry.FindPropertyRelative("m_Arguments")
            .FindPropertyRelative("m_StringArgument").stringValue = "OnLanguageDropdownChanged";
        entry.FindPropertyRelative("m_CallState").intValue = 2;
        ddSO.ApplyModifiedProperties();

        Debug.Log("[Idiomas] Dropdown conectado al LocalizationManager.");
        EditorUtility.DisplayDialog(S("mgr_dropdown_wire_title"),
            S("mgr_dropdown_wire_msg"), S("ok"));
    }

    // =====================================================================
    // Auto-buscar
    // =====================================================================

    private enum InteractionTextType
    {
        UdonInteraction,
        PickupInteraction,
        PickupUse
    }

    private class InteractionSearchResult
    {
        public Object target;
        public InteractionTextType type;
        public string currentText;
        public string translationKey;
        public string hierarchyPath;
        public bool include = true;
    }

    private class InteractionLocalizerGroup
    {
        public GameObject gameObject;
        public readonly List<UdonSharpBehaviour> udonTargets =
            new List<UdonSharpBehaviour>();
        public readonly List<string> udonKeys = new List<string>();
        public readonly List<bool> udonEnabled = new List<bool>();
        public readonly List<VRCPickup> pickupInteractionTargets =
            new List<VRCPickup>();
        public readonly List<string> pickupInteractionKeys = new List<string>();
        public readonly List<bool> pickupInteractionEnabled =
            new List<bool>();
        public readonly List<VRCPickup> pickupUseTargets =
            new List<VRCPickup>();
        public readonly List<string> pickupUseKeys = new List<string>();
        public readonly List<bool> pickupUseEnabled = new List<bool>();
    }

    private void DrawInteractionResults()
    {
        if (_interactionPendingRows == null ||
            _interactionPendingRows.Count == 0)
            return;

        EditorGUILayout.Space(5);
        for (int i = 0; i < _interactionPendingRows.Count; i++)
        {
            InteractionPendingRowSummary row =
                _interactionPendingRows[i];
            if (row.owner == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawInteractionSearchHeader(
                row.owner,
                row.includedCount,
                row.excludedCount);

            EditorGUILayout.EndVertical();
        }
    }

    private void DrawExcludedInteractionResults()
    {
        if (_interactionExcludedRows == null ||
            _interactionExcludedRows.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(
            S("mgr_no_translatable"),
            EditorStyles.miniLabel);
        GUIStyle grayStyle = new GUIStyle(EditorStyles.miniLabel);
        grayStyle.normal.textColor = Color.gray;

        for (int i = 0; i < _interactionExcludedRows.Count; i++)
        {
            InteractionPendingRowSummary row =
                _interactionExcludedRows[i];
            if (row.owner == null) continue;

            EditorGUILayout.LabelField(
                $"  {row.owner.name}, " +
                GetInteractionHierarchyPath(row.owner.transform),
                grayStyle);
        }
    }

    private static GUIStyle CreateInteractionLinkStyle(bool bold)
    {
        GUIStyle style = new GUIStyle(EditorStyles.label);
        style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        style.clipping = TextClipping.Clip;
        style.normal.textColor = new Color(0.3f, 0.6f, 1f);
        return style;
    }

    private static GUIStyle CreateRightAlignedStyle(GUIStyle source)
    {
        GUIStyle style = new GUIStyle(source);
        style.alignment = TextAnchor.MiddleRight;
        return style;
    }

    private void DrawInteractionSearchHeader(
        GameObject owner, int includedCount, int excludedCount)
    {
        string countText =
            string.Format(S("mgr_text_count"), includedCount);
        string statusText = S("mgr_localizer_not_configured");
        if (excludedCount > 0)
        {
            statusText += "  |  " +
                $"{S("cl_legend_excluded")} {excludedCount}";
        }
        GUIStyle linkStyle = CreateInteractionLinkStyle(true);
        DrawLocalizedObjectStatusRow(
            owner,
            GetInteractionHierarchyPath(owner.transform),
            countText,
            statusText,
            true,
            linkStyle);
    }

    private void DrawInteractionLocalizerEditLink(
        InteractionLocalizer localizer)
    {
        EditorGUILayout.Space(3);
        Color previousBackgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
        if (GUILayout.Button(
            S("mgr_edit_interaction_localizer"),
            GUILayout.Height(24)))
        {
            Selection.activeObject = localizer;
            EditorGUIUtility.PingObject(localizer);
        }
        GUI.backgroundColor = previousBackgroundColor;
    }

    private static void DrawLocalizedObjectStatusRow(
        GameObject owner,
        string hierarchyPath,
        string countText,
        string jsonText,
        bool hasJsonIssues,
        GUIStyle linkStyle)
    {
        GUIStyle countStyle = new GUIStyle(EditorStyles.miniLabel);
        GUIStyle jsonStyle = new GUIStyle(EditorStyles.miniLabel);
        jsonStyle.fontStyle = FontStyle.Bold;
        jsonStyle.normal.textColor = hasJsonIssues
            ? new Color(0.8f, 0.5f, 0.0f)
            : new Color(0.2f, 0.7f, 0.2f);

        float statusWidth =
            countStyle.CalcSize(new GUIContent(countText)).x +
            countStyle.CalcSize(new GUIContent("|")).x +
            jsonStyle.CalcSize(new GUIContent(jsonText)).x + 12f;
        float availableWidth =
            Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 45f);
        bool useTwoLines = availableWidth < statusWidth + 120f;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(
            new GUIContent(owner.name, hierarchyPath),
            linkStyle,
            GUILayout.MinWidth(60f),
            GUILayout.ExpandWidth(true)))
        {
            EditorGUIUtility.PingObject(owner);
        }
        GUILayout.Label(
            countText,
            countStyle,
            GUILayout.ExpandWidth(false));
        if (!useTwoLines)
        {
            GUILayout.Label("|", countStyle, GUILayout.ExpandWidth(false));
            GUILayout.Label(
                jsonText,
                jsonStyle,
                GUILayout.ExpandWidth(false));
        }
        EditorGUILayout.EndHorizontal();

        if (useTwoLines)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                jsonText,
                jsonStyle,
                GUILayout.ExpandWidth(false));
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawRegisteredInteractionHeader(
        GameObject owner,
        string countText,
        string jsonText,
        bool hasJsonIssues)
    {
        GUIStyle linkStyle = CreateInteractionLinkStyle(false);
        DrawLocalizedObjectStatusRow(
            owner,
            GetInteractionHierarchyPath(owner.transform),
            countText,
            jsonText,
            hasJsonIssues,
            linkStyle);
    }

    private void AddInteractionLocalizerTo(
        GameObject owner, List<InteractionSearchResult> results)
    {
        if (owner == null || results == null) return;
        LocalizationManager manager = (LocalizationManager)target;
        InteractionLocalizerGroup group =
            BuildInteractionLocalizerGroup(owner, results, null, null);
        if (GetInteractionGroupTextCount(group) == 0) return;

        InteractionLocalizer localizer =
            GetPrefabInteractionLocalizer(manager);
        if (localizer == null) return;
        AppendInteractionGroupToCentral(
            manager,
            localizer,
            group,
            _quickSetupBaseLanguage);
        _interactionSearchResults.RemoveAll(result =>
        {
            Component component = result.target as Component;
            return component != null && component.gameObject == owner;
        });
        RefreshScanResultSummary();
        serializedObject.ApplyModifiedProperties();
        EditorGUIUtility.PingObject(owner);
    }

    private void AppendInteractionGroupToCentral(
        LocalizationManager manager,
        InteractionLocalizer localizer,
        InteractionLocalizerGroup group,
        string baseLang)
    {
        SerializedObject localizerSO = new SerializedObject(localizer);
        List<UdonSharpBehaviour> udonTargets =
            ReadObjectArray<UdonSharpBehaviour>(
                localizerSO.FindProperty("interactTargets"));
        List<string> udonKeys =
            ReadStringArray(localizerSO.FindProperty("interactKeys"));
        List<VRCPickup> pickupInteractionTargets =
            ReadObjectArray<VRCPickup>(
                localizerSO.FindProperty("pickupInteractionTargets"));
        List<string> pickupInteractionKeys =
            ReadStringArray(
                localizerSO.FindProperty("pickupInteractionKeys"));
        List<VRCPickup> pickupUseTargets =
            ReadObjectArray<VRCPickup>(
                localizerSO.FindProperty("pickupUseTargets"));
        List<string> pickupUseKeys =
            ReadStringArray(localizerSO.FindProperty("pickupUseKeys"));

        List<InteractionLocalizer> existingLocalizers =
            GetRegisteredInteractionLocalizers();
        for (int i = 0; i < existingLocalizers.Count; i++)
        {
            InteractionLocalizer existing = existingLocalizers[i];
            if (existing == null || existing == localizer) continue;
            SerializedObject existingSO = new SerializedObject(existing);
            udonTargets.AddRange(
                ReadObjectArray<UdonSharpBehaviour>(
                    existingSO.FindProperty("interactTargets")));
            udonKeys.AddRange(
                ReadStringArray(existingSO.FindProperty("interactKeys")));
            pickupInteractionTargets.AddRange(
                ReadObjectArray<VRCPickup>(
                    existingSO.FindProperty(
                        "pickupInteractionTargets")));
            pickupInteractionKeys.AddRange(
                ReadStringArray(
                    existingSO.FindProperty("pickupInteractionKeys")));
            pickupUseTargets.AddRange(
                ReadObjectArray<VRCPickup>(
                    existingSO.FindProperty("pickupUseTargets")));
            pickupUseKeys.AddRange(
                ReadStringArray(
                    existingSO.FindProperty("pickupUseKeys")));
        }

        udonTargets.AddRange(group.udonTargets);
        udonKeys.AddRange(group.udonKeys);
        pickupInteractionTargets.AddRange(
            group.pickupInteractionTargets);
        pickupInteractionKeys.AddRange(group.pickupInteractionKeys);
        pickupUseTargets.AddRange(group.pickupUseTargets);
        pickupUseKeys.AddRange(group.pickupUseKeys);

        InteractionLocalizerGroup aggregate =
            new InteractionLocalizerGroup();
        aggregate.udonTargets.AddRange(udonTargets);
        aggregate.udonKeys.AddRange(udonKeys);
        aggregate.pickupInteractionTargets.AddRange(
            pickupInteractionTargets);
        aggregate.pickupInteractionKeys.AddRange(
            pickupInteractionKeys);
        aggregate.pickupUseTargets.AddRange(pickupUseTargets);
        aggregate.pickupUseKeys.AddRange(pickupUseKeys);
        ConfigureCentralInteractionLocalizer(
            manager, localizer, aggregate, baseLang);
    }

    private static List<T> ReadObjectArray<T>(
        SerializedProperty property) where T : Object
    {
        List<T> values = new List<T>();
        if (property == null) return values;
        for (int i = 0; i < property.arraySize; i++)
        {
            T value = property.GetArrayElementAtIndex(i)
                .objectReferenceValue as T;
            if (value != null) values.Add(value);
        }
        return values;
    }

    private static List<string> ReadStringArray(
        SerializedProperty property)
    {
        List<string> values = new List<string>();
        if (property == null) return values;
        for (int i = 0; i < property.arraySize; i++)
            values.Add(property.GetArrayElementAtIndex(i).stringValue);
        return values;
    }

    private static List<bool> ReadBoolArray(
        SerializedProperty property, int expectedCount)
    {
        List<bool> values = new List<bool>();
        for (int i = 0; i < expectedCount; i++)
        {
            values.Add(
                property == null || i >= property.arraySize ||
                property.GetArrayElementAtIndex(i).boolValue);
        }
        return values;
    }

    private InteractionLocalizerGroup BuildInteractionLocalizerGroup(
        GameObject owner,
        List<InteractionSearchResult> results,
        Dictionary<string, Dictionary<string, string>> translations,
        string baseLang)
    {
        InteractionLocalizerGroup group =
            new InteractionLocalizerGroup { gameObject = owner };
        for (int i = 0; i < results.Count; i++)
        {
            InteractionSearchResult result = results[i];
            Component component = result.target as Component;
            if (!result.include || component == null ||
                component.gameObject != owner)
            {
                continue;
            }

            if (translations != null && !string.IsNullOrEmpty(baseLang))
                translations[baseLang][result.translationKey] =
                    result.currentText;

            if (result.type == InteractionTextType.UdonInteraction)
            {
                group.udonTargets.Add((UdonSharpBehaviour)result.target);
                group.udonKeys.Add(result.translationKey);
                group.udonEnabled.Add(true);
            }
            else if (result.type == InteractionTextType.PickupInteraction)
            {
                group.pickupInteractionTargets.Add((VRCPickup)result.target);
                group.pickupInteractionKeys.Add(result.translationKey);
                group.pickupInteractionEnabled.Add(true);
            }
            else
            {
                group.pickupUseTargets.Add((VRCPickup)result.target);
                group.pickupUseKeys.Add(result.translationKey);
                group.pickupUseEnabled.Add(true);
            }
        }
        return group;
    }

    private static int GetInteractionGroupTextCount(
        InteractionLocalizerGroup group)
    {
        return group.udonKeys.Count + group.pickupInteractionKeys.Count +
            group.pickupUseKeys.Count;
    }

    private class RegisteredInteractionEntry
    {
        public Object target;
        public InteractionTextType type;
        public SerializedProperty key;
        public SerializedProperty enabled;
        public string text;
    }

    private enum InteractionJsonState
    {
        Clean,
        Modified,
        Missing
    }

    private enum SourceTranslationUpdateMode
    {
        ClearTranslations,
        KeepTranslations,
        Cancel
    }

    private class RegisteredCanvasEntry
    {
        public Component component;
        public SerializedProperty key;
        public string text;
    }

    private void DrawInteractionLocalizerList(InteractionLocalizer localizer)
    {
        if (localizer == null || _interactionRegisteredRows == null)
            return;

        int totalModified = 0;
        int totalMissing = 0;

        EditorGUILayout.LabelField(
            S("mgr_already_localized"), EditorStyles.boldLabel);
        for (int i = 0; i < _interactionRegisteredRows.Count; i++)
        {
            InteractionRegisteredRowSummary row =
                _interactionRegisteredRows[i];
            if (row.owner == null) continue;
            totalModified += row.modifiedCount;
            totalMissing += row.missingCount;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string countText = string.Format(
                S("mgr_text_count"), row.textCount);
            string jsonText =
                $"JSON ✓ {row.cleanCount}  △ {row.modifiedCount}  " +
                $"! {row.missingCount}";
            if (row.sharedKeyCount > 0)
            {
                jsonText += "  |  " +
                    string.Format(
                        S("mgr_shared_keys"), row.sharedKeyCount);
            }
            DrawRegisteredInteractionHeader(
                row.owner,
                countText,
                jsonText,
                row.modifiedCount > 0 || row.missingCount > 0);

            EditorGUILayout.EndVertical();
        }

        if (totalModified > 0 || totalMissing > 0)
        {
            EditorGUILayout.HelpBox(
                $"JSON: Modified {totalModified}, Missing {totalMissing}",
                MessageType.Warning);
        }
    }

    private static InteractionJsonState GetInteractionJsonState(
        RegisteredInteractionEntry entry,
        Dictionary<string, string> baseEntries)
    {
        string key = entry.key.stringValue;
        if (string.IsNullOrWhiteSpace(key) ||
            !baseEntries.TryGetValue(key, out string jsonText))
        {
            return InteractionJsonState.Missing;
        }
        return IdiomasEditorUtils.TextEquals(jsonText, entry.text)
            ? InteractionJsonState.Clean
            : InteractionJsonState.Modified;
    }

    private static void EnsureInteractionEnabledArrays(
        SerializedObject localizerSO)
    {
        EnsureBoolArray(
            localizerSO.FindProperty("interactEnabled"),
            GetArraySize(localizerSO, "interactKeys"));
        EnsureBoolArray(
            localizerSO.FindProperty("pickupInteractionEnabled"),
            GetArraySize(localizerSO, "pickupInteractionKeys"));
        EnsureBoolArray(
            localizerSO.FindProperty("pickupUseEnabled"),
            GetArraySize(localizerSO, "pickupUseKeys"));
    }

    private static void EnsureBoolArray(
        SerializedProperty property, int count)
    {
        if (property == null || property.arraySize >= count) return;
        int oldSize = property.arraySize;
        property.arraySize = count;
        for (int i = oldSize; i < count; i++)
            property.GetArrayElementAtIndex(i).boolValue = true;
    }

    private static Dictionary<GameObject, List<RegisteredInteractionEntry>>
        BuildRegisteredInteractionGroups(SerializedObject localizerSO)
    {
        Dictionary<GameObject, List<RegisteredInteractionEntry>> groups =
            new Dictionary<GameObject, List<RegisteredInteractionEntry>>();
        AddRegisteredEntries(
            localizerSO,
            "interactTargets",
            "interactKeys",
            "interactEnabled",
            InteractionTextType.UdonInteraction,
            groups);
        AddRegisteredEntries(
            localizerSO,
            "pickupInteractionTargets",
            "pickupInteractionKeys",
            "pickupInteractionEnabled",
            InteractionTextType.PickupInteraction,
            groups);
        AddRegisteredEntries(
            localizerSO,
            "pickupUseTargets",
            "pickupUseKeys",
            "pickupUseEnabled",
            InteractionTextType.PickupUse,
            groups);
        return groups;
    }

    private static void AddRegisteredEntries(
        SerializedObject localizerSO,
        string targetProperty,
        string keyProperty,
        string enabledProperty,
        InteractionTextType type,
        Dictionary<GameObject, List<RegisteredInteractionEntry>> groups)
    {
        SerializedProperty targets =
            localizerSO.FindProperty(targetProperty);
        SerializedProperty keys = localizerSO.FindProperty(keyProperty);
        SerializedProperty enabled =
            localizerSO.FindProperty(enabledProperty);
        int count = Mathf.Min(targets.arraySize, keys.arraySize);
        for (int i = 0; i < count; i++)
        {
            Object targetObject =
                targets.GetArrayElementAtIndex(i).objectReferenceValue;
            Component component = targetObject as Component;
            if (component == null) continue;
            GameObject owner = component.gameObject;
            if (!groups.TryGetValue(
                owner, out List<RegisteredInteractionEntry> entries))
            {
                entries = new List<RegisteredInteractionEntry>();
                groups[owner] = entries;
            }
            entries.Add(new RegisteredInteractionEntry
            {
                target = targetObject,
                type = type,
                key = keys.GetArrayElementAtIndex(i),
                enabled = enabled.GetArrayElementAtIndex(i),
                text = GetRegisteredInteractionText(targetObject, type)
            });
        }
    }

    private static string GetRegisteredInteractionText(
        Object targetObject, InteractionTextType type)
    {
        if (type == InteractionTextType.UdonInteraction)
        {
            UdonSharpBehaviour behaviour =
                targetObject as UdonSharpBehaviour;
            UdonBehaviour backing = behaviour != null
                ? IdiomasEditorUtils.FindUdonBehaviourFor(behaviour)
                : null;
            return backing != null ? backing.InteractionText : "";
        }

        VRCPickup pickup = targetObject as VRCPickup;
        if (pickup == null) return "";
        return type == InteractionTextType.PickupUse
            ? pickup.UseText
            : pickup.InteractionText;
    }

    private List<InteractionLocalizer> GetRegisteredInteractionLocalizers()
    {
        List<InteractionLocalizer> localizers =
            new List<InteractionLocalizer>();
        InteractionLocalizer localizer =
            GetPrefabInteractionLocalizer(
                target as LocalizationManager);
        if (localizer != null) localizers.Add(localizer);
        return localizers;
    }

    private void DrawGlobalLocalizerActions()
    {
        int localizerCount = _canvasLocalizers.arraySize +
            _interactionLocalizers.arraySize;
        EditorGUI.BeginDisabledGroup(localizerCount == 0);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
        if (GUILayout.Button(
            new GUIContent(
                S("mgr_restore_all_json"),
                S("mgr_restore_all_json_tooltip")),
            GUILayout.Width(160)))
        {
            RestoreAllKeysToJson();
        }
        GUI.backgroundColor = Color.white;

        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button(
            new GUIContent(S("mgr_remove_all"), S("mgr_remove_all_tooltip")),
            GUILayout.Width(100)))
        {
            RemoveAllLocalizers();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
    }

    private static int GetArraySize(SerializedObject serializedObject, string propertyName)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.arraySize : 0;
    }

    private static int GetConfiguredInteractionTextCount(
        InteractionLocalizer localizer)
    {
        SerializedObject localizerSO = new SerializedObject(localizer);
        return GetArraySize(localizerSO, "interactKeys") +
            GetArraySize(localizerSO, "pickupInteractionKeys") +
            GetArraySize(localizerSO, "pickupUseKeys");
    }

    private static List<string> GetInteractionLocalizerKeys(
        InteractionLocalizer localizer)
    {
        List<string> keys = new List<string>();
        if (localizer == null) return keys;

        SerializedObject localizerSO = new SerializedObject(localizer);
        AddEnabledStringArrayValues(
            localizerSO.FindProperty("interactKeys"),
            localizerSO.FindProperty("interactEnabled"),
            keys);
        AddEnabledStringArrayValues(
            localizerSO.FindProperty("pickupInteractionKeys"),
            localizerSO.FindProperty("pickupInteractionEnabled"),
            keys);
        AddEnabledStringArrayValues(
            localizerSO.FindProperty("pickupUseKeys"),
            localizerSO.FindProperty("pickupUseEnabled"),
            keys);
        return keys;
    }

    private static List<string> GetAllInteractionLocalizerKeys(
        InteractionLocalizer localizer)
    {
        List<string> keys = new List<string>();
        if (localizer == null) return keys;
        SerializedObject localizerSO = new SerializedObject(localizer);
        AddAllStringArrayValuesToList(
            localizerSO.FindProperty("interactKeys"), keys);
        AddAllStringArrayValuesToList(
            localizerSO.FindProperty("pickupInteractionKeys"), keys);
        AddAllStringArrayValuesToList(
            localizerSO.FindProperty("pickupUseKeys"), keys);
        return keys;
    }

    private static void AddAllStringArrayValuesToList(
        SerializedProperty property, List<string> values)
    {
        if (property == null) return;
        for (int i = 0; i < property.arraySize; i++)
        {
            string value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(value)) values.Add(value);
        }
    }

    private static void AddEnabledStringArrayValues(
        SerializedProperty property,
        SerializedProperty enabled,
        List<string> values)
    {
        if (property == null) return;
        for (int i = 0; i < property.arraySize; i++)
        {
            if (enabled != null && i < enabled.arraySize &&
                !enabled.GetArrayElementAtIndex(i).boolValue)
            {
                continue;
            }
            string value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(value)) values.Add(value);
        }
    }

    private void RemoveInteractionLocalizer(
        InteractionLocalizer localizer, int configuredTextCount)
    {
        if (!EditorUtility.DisplayDialog(
            S("mgr_remove_interaction_title"),
            string.Format(
                S("mgr_remove_interaction_msg"),
                localizer.gameObject.name,
                configuredTextCount),
            S("mgr_remove_cl_confirm"),
            S("cancel")))
        {
            return;
        }

        RemoveInteractionLocalizerWithoutDialog(localizer);
    }

    private void RemoveInteractionLocalizerWithoutDialog(
        InteractionLocalizer localizer)
    {
        if (localizer == null) return;
        Undo.RecordObject(localizer, "Clear Interaction Localizer");
        SerializedObject localizerSO = new SerializedObject(localizer);
        string[] arrays =
        {
            "interactTargets", "interactKeys", "interactEnabled",
            "pickupInteractionTargets", "pickupInteractionKeys",
            "pickupInteractionEnabled", "pickupUseTargets",
            "pickupUseKeys", "pickupUseEnabled"
        };
        for (int i = 0; i < arrays.Length; i++)
        {
            SerializedProperty property =
                localizerSO.FindProperty(arrays[i]);
            if (property != null) property.ClearArray();
        }
        localizerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(localizer);
        Debug.Log("[Idiomas] Contenido de InteractionLocalizer limpiado.");
    }

    private void ScanInteractionTexts()
    {
        _interactionSearchResults = new List<InteractionSearchResult>();
        HashSet<GameObject> detectedObjects = new HashSet<GameObject>();
        HashSet<GameObject> registeredObjects = new HashSet<GameObject>();
        HashSet<string> usedKeys = new HashSet<string>();
        HashSet<Object> registeredUdonTargets = new HashSet<Object>();
        HashSet<Object> registeredPickupInteractionTargets =
            new HashSet<Object>();
        HashSet<Object> registeredPickupUseTargets =
            new HashSet<Object>();
        string[] excludedKeywords = ParseQuickSetupExcludedKeywords();
        if (_cachedKeys != null)
        {
            for (int i = 0; i < _cachedKeys.Length; i++)
                usedKeys.Add(_cachedKeys[i]);
        }
        List<InteractionLocalizer> registeredLocalizers =
            GetRegisteredInteractionLocalizers();
        for (int i = 0; i < registeredLocalizers.Count; i++)
        {
            SerializedObject registeredSO =
                new SerializedObject(registeredLocalizers[i]);
            AddAllStringArrayValues(
                registeredSO.FindProperty("interactKeys"), usedKeys);
            AddAllStringArrayValues(
                registeredSO.FindProperty("pickupInteractionKeys"), usedKeys);
            AddAllStringArrayValues(
                registeredSO.FindProperty("pickupUseKeys"), usedKeys);
            AddAllObjectArrayValues(
                registeredSO.FindProperty("interactTargets"),
                registeredUdonTargets);
            AddAllObjectArrayValues(
                registeredSO.FindProperty("pickupInteractionTargets"),
                registeredPickupInteractionTargets);
            AddAllObjectArrayValues(
                registeredSO.FindProperty("pickupUseTargets"),
                registeredPickupUseTargets);
        }

        UdonBehaviour[] udonBehaviours = FindObjectsByType<UdonBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(
            udonBehaviours,
            (a, b) => IdiomasEditorUtils.CompareStableComponents(a, b));
        for (int i = 0; i < udonBehaviours.Length; i++)
        {
            UdonBehaviour udonBehaviour = udonBehaviours[i];
            UdonSharpBehaviour behaviour =
                UdonSharpEditorUtility.GetProxyBehaviour(udonBehaviour);
            if (behaviour == null ||
                behaviour is LocalizationManager ||
                behaviour is TextLocalizer ||
                behaviour is CanvasLocalizer ||
                behaviour is InteractionLocalizer ||
                IsExcludedByGameObject(behaviour.transform) ||
                string.IsNullOrWhiteSpace(udonBehaviour.InteractionText))
            {
                continue;
            }
            if (!_includeDefaultUseText.boolValue &&
                udonBehaviour.InteractionText == "Use")
            {
                continue;
            }
            detectedObjects.Add(behaviour.gameObject);
            if (registeredUdonTargets.Contains(behaviour))
            {
                registeredObjects.Add(behaviour.gameObject);
                continue;
            }

            string suffix = IdiomasEditorUtils.NormalizeName(behaviour.GetType().Name);
            AddInteractionResult(
                behaviour,
                InteractionTextType.UdonInteraction,
                udonBehaviour.InteractionText,
                BuildInteractionKey(
                    "interaction", behaviour.gameObject.name, suffix, usedKeys),
                !ContainsExcludedKeyword(
                    udonBehaviour.InteractionText, excludedKeywords));
        }

        VRCPickup[] pickups = FindObjectsByType<VRCPickup>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        System.Array.Sort(
            pickups,
            (a, b) => IdiomasEditorUtils.CompareStableComponents(a, b));
        for (int i = 0; i < pickups.Length; i++)
        {
            VRCPickup pickup = pickups[i];
            if (IsExcludedByGameObject(pickup.transform)) continue;
            if (!string.IsNullOrWhiteSpace(pickup.InteractionText))
            {
                detectedObjects.Add(pickup.gameObject);
                if (!registeredPickupInteractionTargets.Contains(pickup))
                {
                    AddInteractionResult(
                        pickup,
                        InteractionTextType.PickupInteraction,
                        pickup.InteractionText,
                        BuildInteractionKey(
                            "pickup", pickup.gameObject.name, "interaction", usedKeys),
                        !ContainsExcludedKeyword(
                            pickup.InteractionText, excludedKeywords));
                }
                else
                {
                    registeredObjects.Add(pickup.gameObject);
                }
            }
            if (!string.IsNullOrWhiteSpace(pickup.UseText) &&
                (_includeDefaultUseText.boolValue || pickup.UseText != "Use"))
            {
                detectedObjects.Add(pickup.gameObject);
                if (!registeredPickupUseTargets.Contains(pickup))
                {
                    AddInteractionResult(
                        pickup,
                        InteractionTextType.PickupUse,
                        pickup.UseText,
                        BuildInteractionKey(
                            "pickup", pickup.gameObject.name, "use", usedKeys),
                        !ContainsExcludedKeyword(
                            pickup.UseText, excludedKeywords));
                }
                else
                {
                    registeredObjects.Add(pickup.gameObject);
                }
            }
        }
        _interactionDetectedObjectCount = detectedObjects.Count;
        _interactionRegisteredObjectCount = registeredObjects.Count;
    }

    private static void AddAllStringArrayValues(
        SerializedProperty property, HashSet<string> values)
    {
        if (property == null) return;
        for (int i = 0; i < property.arraySize; i++)
        {
            string value = property.GetArrayElementAtIndex(i).stringValue;
            if (!string.IsNullOrEmpty(value)) values.Add(value);
        }
    }

    private static void AddAllObjectArrayValues(
        SerializedProperty property, HashSet<Object> values)
    {
        if (property == null) return;
        for (int i = 0; i < property.arraySize; i++)
        {
            Object value = property.GetArrayElementAtIndex(i)
                .objectReferenceValue;
            if (value != null) values.Add(value);
        }
    }

    private bool IsInteractionTargetRegistered(
        Object targetObject, InteractionTextType type)
    {
        List<InteractionLocalizer> localizers =
            GetRegisteredInteractionLocalizers();
        string propertyName = type == InteractionTextType.UdonInteraction
            ? "interactTargets"
            : type == InteractionTextType.PickupInteraction
                ? "pickupInteractionTargets"
                : "pickupUseTargets";
        for (int i = 0; i < localizers.Count; i++)
        {
            SerializedObject localizerSO =
                new SerializedObject(localizers[i]);
            SerializedProperty targets =
                localizerSO.FindProperty(propertyName);
            if (targets == null) continue;
            for (int j = 0; j < targets.arraySize; j++)
            {
                if (targets.GetArrayElementAtIndex(j)
                    .objectReferenceValue == targetObject)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void AddInteractionResult(
        Object targetObject,
        InteractionTextType type,
        string currentText,
        string translationKey,
        bool include)
    {
        Component component = targetObject as Component;
        _interactionSearchResults.Add(new InteractionSearchResult
        {
            target = targetObject,
            type = type,
            currentText = currentText,
            translationKey = translationKey,
            include = include,
            hierarchyPath = component != null
                ? GetInteractionHierarchyPath(component.transform)
                : targetObject.name
        });
    }

    private static string BuildInteractionKey(
        string prefix, string objectName, string suffix, HashSet<string> usedKeys)
    {
        string normalizedName = IdiomasEditorUtils.NormalizeName(objectName);
        if (string.IsNullOrEmpty(normalizedName)) normalizedName = "object";
        if (string.IsNullOrEmpty(suffix)) suffix = "text";

        string baseKey = $"{prefix}_{normalizedName}_{suffix}";
        string key = baseKey;
        int counter = 2;
        while (usedKeys.Contains(key))
            key = baseKey + "_" + counter++;

        usedKeys.Add(key);
        return key;
    }

    private static string GetInteractionHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private static string GetInteractionTypeLabel(InteractionTextType type)
    {
        switch (type)
        {
            case InteractionTextType.PickupInteraction:
                return "Pickup Interaction Text";
            case InteractionTextType.PickupUse:
                return "Pickup Use Text";
            default:
                return "Udon Interaction Text";
        }
    }

    private bool HasInvalidInteractionKeys()
    {
        if (!_includeInteractionTexts.boolValue || _interactionSearchResults == null)
            return false;
        HashSet<string> keys = new HashSet<string>();
        for (int i = 0; i < _interactionSearchResults.Count; i++)
        {
            InteractionSearchResult result = _interactionSearchResults[i];
            if (!result.include) continue;
            if (string.IsNullOrWhiteSpace(result.translationKey) ||
                !keys.Add(result.translationKey))
            {
                return true;
            }
        }
        return false;
    }

    private void RefreshScanResultSummary()
    {
        if (_canvasSearchResults == null)
        {
            _scanResultSummary = null;
            return;
        }

        ScanResultSummary summary = new ScanResultSummary();
        for (int i = _canvasSearchResults.Count - 1; i >= 0; i--)
        {
            CanvasSearchResult result = _canvasSearchResults[i];
            if (result == null || result.gameObject == null)
            {
                _canvasSearchResults.RemoveAt(i);
                continue;
            }

            int textCount = result.tmpCount + result.legacyCount;
            int pendingTextCount = result.hasCanvasLocalizer
                ? result.missingTextCount
                : textCount;
            if (pendingTextCount > 0)
            {
                summary.canvasCandidateCount++;
                summary.canvasCandidateTextCount += pendingTextCount;
            }
            if (result.hasCanvasLocalizer)
                summary.localizedCanvasCount++;
            else if (textCount == 0)
                summary.noTextCanvasCount++;
        }

        if (_includeInteractionTexts.boolValue &&
            _interactionSearchResults != null)
        {
            HashSet<GameObject> owners = new HashSet<GameObject>();
            HashSet<string> keys = new HashSet<string>();
            for (int i = 0; i < _interactionSearchResults.Count; i++)
            {
                InteractionSearchResult result =
                    _interactionSearchResults[i];
                if (!result.include) continue;
                summary.interactionTextCount++;
                Component component = result.target as Component;
                if (component != null) owners.Add(component.gameObject);
                if (string.IsNullOrWhiteSpace(result.translationKey) ||
                    !keys.Add(result.translationKey))
                {
                    summary.hasInvalidInteractionKeys = true;
                }
            }
            summary.interactionObjectCount = owners.Count;
        }
        _scanResultSummary = summary;
        RefreshInteractionRowSummaries();
    }

    private void RefreshInteractionRowSummaries()
    {
        RefreshSceneKeyReferenceCounts();
        _interactionPendingRows =
            new List<InteractionPendingRowSummary>();
        _interactionExcludedRows =
            new List<InteractionPendingRowSummary>();
        _interactionRegisteredRows =
            new List<InteractionRegisteredRowSummary>();

        Dictionary<GameObject, InteractionPendingRowSummary> pendingRows =
            new Dictionary<GameObject, InteractionPendingRowSummary>();
        if (_interactionSearchResults != null)
        {
            for (int i = 0; i < _interactionSearchResults.Count; i++)
            {
                InteractionSearchResult result =
                    _interactionSearchResults[i];
                Component component = result.target as Component;
                if (component == null) continue;
                GameObject owner = component.gameObject;
                if (!pendingRows.TryGetValue(
                    owner, out InteractionPendingRowSummary row))
                {
                    row = new InteractionPendingRowSummary
                    {
                        owner = owner
                    };
                    pendingRows[owner] = row;
                }
                if (result.include)
                    row.includedCount++;
                else
                    row.excludedCount++;
            }
        }
        foreach (InteractionPendingRowSummary row
            in pendingRows.Values)
        {
            if (row.includedCount > 0)
                _interactionPendingRows.Add(row);
            else
                _interactionExcludedRows.Add(row);
        }
        _interactionPendingRows.Sort((a, b) =>
            IdiomasEditorUtils.CompareStableComponents(
                a.owner.transform, b.owner.transform));
        _interactionExcludedRows.Sort((a, b) =>
            IdiomasEditorUtils.CompareStableComponents(
                a.owner.transform, b.owner.transform));

        InteractionLocalizer localizer =
            GetPrefabInteractionLocalizer(
                target as LocalizationManager);
        if (localizer == null) return;

        SerializedObject localizerSO = new SerializedObject(localizer);
        EnsureInteractionEnabledArrays(localizerSO);
        if (localizerSO.ApplyModifiedProperties())
            EditorUtility.SetDirty(localizer);
        Dictionary<GameObject, List<RegisteredInteractionEntry>> groups =
            BuildRegisteredInteractionGroups(localizerSO);
        Dictionary<string, string> baseEntries =
            GetInteractionBaseLanguageEntries(localizer);

        foreach (KeyValuePair<GameObject,
            List<RegisteredInteractionEntry>> pair in groups)
        {
            InteractionRegisteredRowSummary row =
                new InteractionRegisteredRowSummary
                {
                    owner = pair.Key
                };
            Dictionary<string, int> localKeyReferences =
                new Dictionary<string, int>(
                    System.StringComparer.Ordinal);
            List<RegisteredInteractionEntry> entries = pair.Value;
            for (int i = 0; i < entries.Count; i++)
            {
                RegisteredInteractionEntry entry = entries[i];
                if (!entry.enabled.boolValue) continue;
                row.textCount++;
                InteractionJsonState state =
                    GetInteractionJsonState(entry, baseEntries);
                if (state == InteractionJsonState.Clean)
                    row.cleanCount++;
                else if (state == InteractionJsonState.Modified)
                    row.modifiedCount++;
                else
                    row.missingCount++;

                string key = entry.key.stringValue;
                if (string.IsNullOrEmpty(key)) continue;
                localKeyReferences.TryGetValue(key, out int count);
                localKeyReferences[key] = count + 1;
            }

            foreach (KeyValuePair<string, int> keyUsage
                in localKeyReferences)
            {
                if (_sceneKeyReferenceCounts != null &&
                    _sceneKeyReferenceCounts.TryGetValue(
                        keyUsage.Key, out int sceneCount) &&
                    sceneCount > keyUsage.Value)
                {
                    row.sharedKeyCount++;
                }
            }
            _interactionRegisteredRows.Add(row);
        }
        _interactionRegisteredRows.Sort((a, b) =>
            IdiomasEditorUtils.CompareStableComponents(
                a.owner.transform, b.owner.transform));
    }

    private int CountIncludedInteractionTexts()
    {
        if (!_includeInteractionTexts.boolValue || _interactionSearchResults == null)
            return 0;

        int count = 0;
        for (int i = 0; i < _interactionSearchResults.Count; i++)
        {
            if (_interactionSearchResults[i].include)
                count++;
        }
        return count;
    }

    private int CountIncludedInteractionObjects()
    {
        if (!_includeInteractionTexts.boolValue ||
            _interactionSearchResults == null)
        {
            return 0;
        }

        HashSet<GameObject> owners = new HashSet<GameObject>();
        for (int i = 0; i < _interactionSearchResults.Count; i++)
        {
            InteractionSearchResult result = _interactionSearchResults[i];
            Component component = result.target as Component;
            if (result.include && component != null)
                owners.Add(component.gameObject);
        }
        return owners.Count;
    }

    private int ConfigureInteractionLocalizers(
        LocalizationManager manager,
        string baseLang,
        Dictionary<string, Dictionary<string, string>> translations,
        out int configuredLocalizerCount)
    {
        configuredLocalizerCount = 0;
        Dictionary<string, string> canonicalByText =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (translations.TryGetValue(
            baseLang, out Dictionary<string, string> baseTranslations))
        {
            foreach (KeyValuePair<string, string> pair in baseTranslations)
            {
                if (!string.IsNullOrEmpty(pair.Value) &&
                    !canonicalByText.ContainsKey(pair.Value))
                {
                    canonicalByText[pair.Value] = pair.Key;
                }
            }
        }
        Dictionary<GameObject, InteractionLocalizerGroup> groups =
            new Dictionary<GameObject, InteractionLocalizerGroup>();

        for (int i = 0; i < _interactionSearchResults.Count; i++)
        {
            InteractionSearchResult result = _interactionSearchResults[i];
            if (!result.include) continue;

            Component component = result.target as Component;
            if (component == null) continue;
            GameObject owner = component.gameObject;
            if (!groups.TryGetValue(owner, out InteractionLocalizerGroup group))
            {
                group = new InteractionLocalizerGroup { gameObject = owner };
                groups[owner] = group;
            }

            if (canonicalByText.TryGetValue(
                result.currentText, out string canonicalKey))
            {
                result.translationKey = canonicalKey;
            }
            else
            {
                canonicalByText[result.currentText] = result.translationKey;
                translations[baseLang][result.translationKey] =
                    result.currentText;
            }
            if (result.type == InteractionTextType.UdonInteraction)
            {
                group.udonTargets.Add((UdonSharpBehaviour)result.target);
                group.udonKeys.Add(result.translationKey);
                group.udonEnabled.Add(true);
            }
            else if (result.type == InteractionTextType.PickupInteraction)
            {
                group.pickupInteractionTargets.Add((VRCPickup)result.target);
                group.pickupInteractionKeys.Add(result.translationKey);
                group.pickupInteractionEnabled.Add(true);
            }
            else
            {
                group.pickupUseTargets.Add((VRCPickup)result.target);
                group.pickupUseKeys.Add(result.translationKey);
                group.pickupUseEnabled.Add(true);
            }
        }

        int total = 0;
        foreach (InteractionLocalizerGroup group in groups.Values)
            total += group.udonKeys.Count + group.pickupInteractionKeys.Count +
                group.pickupUseKeys.Count;
        if (total == 0) return 0;

        InteractionLocalizerGroup aggregate = new InteractionLocalizerGroup();
        List<InteractionLocalizer> existingLocalizers =
            GetRegisteredInteractionLocalizers();
        for (int i = 0; i < existingLocalizers.Count; i++)
        {
            SerializedObject existingSO =
                new SerializedObject(existingLocalizers[i]);
            aggregate.udonTargets.AddRange(
                ReadObjectArray<UdonSharpBehaviour>(
                    existingSO.FindProperty("interactTargets")));
            aggregate.udonKeys.AddRange(
                ReadStringArray(existingSO.FindProperty("interactKeys")));
            aggregate.udonEnabled.AddRange(
                ReadBoolArray(
                    existingSO.FindProperty("interactEnabled"),
                    aggregate.udonTargets.Count -
                        aggregate.udonEnabled.Count));
            aggregate.pickupInteractionTargets.AddRange(
                ReadObjectArray<VRCPickup>(
                    existingSO.FindProperty(
                        "pickupInteractionTargets")));
            aggregate.pickupInteractionKeys.AddRange(
                ReadStringArray(
                    existingSO.FindProperty("pickupInteractionKeys")));
            aggregate.pickupInteractionEnabled.AddRange(
                ReadBoolArray(
                    existingSO.FindProperty("pickupInteractionEnabled"),
                    aggregate.pickupInteractionTargets.Count -
                        aggregate.pickupInteractionEnabled.Count));
            aggregate.pickupUseTargets.AddRange(
                ReadObjectArray<VRCPickup>(
                    existingSO.FindProperty("pickupUseTargets")));
            aggregate.pickupUseKeys.AddRange(
                ReadStringArray(
                    existingSO.FindProperty("pickupUseKeys")));
            aggregate.pickupUseEnabled.AddRange(
                ReadBoolArray(
                    existingSO.FindProperty("pickupUseEnabled"),
                    aggregate.pickupUseTargets.Count -
                        aggregate.pickupUseEnabled.Count));
        }
        foreach (InteractionLocalizerGroup group in groups.Values)
        {
            aggregate.udonTargets.AddRange(group.udonTargets);
            aggregate.udonKeys.AddRange(group.udonKeys);
            aggregate.udonEnabled.AddRange(group.udonEnabled);
            aggregate.pickupInteractionTargets.AddRange(
                group.pickupInteractionTargets);
            aggregate.pickupInteractionKeys.AddRange(
                group.pickupInteractionKeys);
            aggregate.pickupInteractionEnabled.AddRange(
                group.pickupInteractionEnabled);
            aggregate.pickupUseTargets.AddRange(group.pickupUseTargets);
            aggregate.pickupUseKeys.AddRange(group.pickupUseKeys);
            aggregate.pickupUseEnabled.AddRange(group.pickupUseEnabled);
        }

        InteractionLocalizer central =
            GetPrefabInteractionLocalizer(manager);
        if (central == null) return 0;
        ConfigureCentralInteractionLocalizer(
            manager, central, aggregate, baseLang);
        configuredLocalizerCount = groups.Count;
        EditorUtility.SetDirty(manager);
        return total;
    }

    private const string INTERACTION_LOCALIZER_OBJECT_NAME =
        "InteractionLocalizer";

    private static InteractionLocalizer GetPrefabInteractionLocalizer(
        LocalizationManager manager)
    {
        if (manager == null) return null;
        Transform child =
            manager.transform.Find(INTERACTION_LOCALIZER_OBJECT_NAME);
        return child != null
            ? child.GetComponent<InteractionLocalizer>()
            : null;
    }

    private InteractionLocalizer AddPrefabInteractionLocalizer()
    {
        try
        {
            LocalizationManager manager = target as LocalizationManager;
            if (manager == null)
            {
                ShowInteractionLocalizerAddError();
                return null;
            }

            Transform child =
                manager.transform.Find(INTERACTION_LOCALIZER_OBJECT_NAME);
            GameObject owner;
            if (child == null)
            {
                owner = new GameObject(INTERACTION_LOCALIZER_OBJECT_NAME);
                Undo.RegisterCreatedObjectUndo(
                    owner, "Create InteractionLocalizer");
                Undo.SetTransformParent(
                    owner.transform,
                    manager.transform,
                    "Parent InteractionLocalizer");
                owner.transform.localPosition = Vector3.zero;
                owner.transform.localRotation = Quaternion.identity;
                owner.transform.localScale = Vector3.one;
            }
            else
            {
                owner = child.gameObject;
            }

            InteractionLocalizer localizer =
                owner.GetComponent<InteractionLocalizer>();
            if (localizer == null)
            {
                localizer =
                    UdonSharpUndo.AddComponent<InteractionLocalizer>(owner);
            }
            if (localizer == null)
            {
                ShowInteractionLocalizerAddError();
                return null;
            }

            SerializedObject localizerSO = new SerializedObject(localizer);
            SerializedProperty managerProperty =
                localizerSO.FindProperty("manager");
            if (managerProperty != null)
                managerProperty.objectReferenceValue = manager;
            SerializedProperty baseLanguageProperty =
                localizerSO.FindProperty("baseLanguage");
            if (baseLanguageProperty != null)
            {
                baseLanguageProperty.stringValue =
                    _quickSetupBaseLanguage;
            }
            localizerSO.ApplyModifiedProperties();
            SetRegisteredInteractionLocalizer(localizer);
            EditorUtility.SetDirty(localizer);
            EditorUtility.SetDirty(manager);

            if (localizer.transform.parent != manager.transform ||
                GetPrefabInteractionLocalizer(manager) != localizer)
            {
                ShowInteractionLocalizerAddError();
                return null;
            }

            return localizer;
        }
        catch (System.Exception exception)
        {
            ShowInteractionLocalizerAddError(exception);
            return null;
        }
    }

    private void ShowInteractionLocalizerAddError(
        System.Exception exception = null)
    {
        string message = S("mgr_add_interaction_error_msg");
        if (exception != null)
            Debug.LogException(exception);
        else
            Debug.LogError(message);
        EditorUtility.DisplayDialog(
            S("error_title"),
            message,
            S("ok"));
    }

    private void ConfigureCentralInteractionLocalizer(
        LocalizationManager manager,
        InteractionLocalizer localizer,
        InteractionLocalizerGroup group,
        string baseLang)
    {
        Undo.RecordObject(localizer, "Configure Interaction Localizer");
        SerializedObject localizerSO = new SerializedObject(localizer);
        localizerSO.FindProperty("manager").objectReferenceValue = manager;
        localizerSO.FindProperty("baseLanguage").stringValue = baseLang;
        SetObjectArray(
            localizerSO.FindProperty("interactTargets"),
            group.udonTargets);
        SetStringArray(
            localizerSO.FindProperty("interactKeys"), group.udonKeys);
        SetBoolArray(
            localizerSO.FindProperty("interactEnabled"),
            group.udonEnabled,
            group.udonKeys.Count);
        SetObjectArray(
            localizerSO.FindProperty("pickupInteractionTargets"),
            group.pickupInteractionTargets);
        SetStringArray(
            localizerSO.FindProperty("pickupInteractionKeys"),
            group.pickupInteractionKeys);
        SetBoolArray(
            localizerSO.FindProperty("pickupInteractionEnabled"),
            group.pickupInteractionEnabled,
            group.pickupInteractionKeys.Count);
        SetObjectArray(
            localizerSO.FindProperty("pickupUseTargets"),
            group.pickupUseTargets);
        SetStringArray(
            localizerSO.FindProperty("pickupUseKeys"),
            group.pickupUseKeys);
        SetBoolArray(
            localizerSO.FindProperty("pickupUseEnabled"),
            group.pickupUseEnabled,
            group.pickupUseKeys.Count);
        localizerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(localizer);
        EditorUtility.SetDirty(localizer.gameObject);
        InvalidateSceneKeyReferenceCounts();
    }

    private void SetRegisteredInteractionLocalizer(
        InteractionLocalizer localizer)
    {
        _interactionLocalizers.ClearArray();
        _interactionLocalizers.arraySize = 1;
        _interactionLocalizers.GetArrayElementAtIndex(0)
            .objectReferenceValue = localizer;
        serializedObject.ApplyModifiedProperties();
    }

    private static void SetObjectArray<T>(
        SerializedProperty property, List<T> values) where T : Object
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetStringArray(SerializedProperty property, List<string> values)
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).stringValue = values[i];
    }

    private static void SetBoolArray(
        SerializedProperty property, List<bool> values, int count)
    {
        // Los datos antiguos sin este array se consideran habilitados.
        property.arraySize = count;
        for (int i = 0; i < count; i++)
        {
            property.GetArrayElementAtIndex(i).boolValue =
                i >= values.Count || values[i];
        }
    }

    // =====================================================================
    // Auto-Traducir
    // =====================================================================

    private void OpenAutoTranslateWindow()
    {
        TextAsset textAsset = _translationFile.objectReferenceValue as TextAsset;
        if (textAsset == null)
        {
            EditorUtility.DisplayDialog(S("no_file_title"),
                S("no_file_export_msg"), S("ok"));
            return;
        }
        string path = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(textAsset));
        AutoTranslateWindow.Open(path);
    }

    // =====================================================================
    // Validacion (bajo demanda, con cache de resultados)
    // =====================================================================

    // =====================================================================
    // Vista previa
    // =====================================================================

    private void DrawPreview()
    {
        if (_cachedData == null || _cachedLanguages == null || _cachedLanguages.Length == 0)
        {
            EditorGUILayout.HelpBox(S("mgr_preview_no_json"), MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(S("mgr_preview_lang"), GUILayout.Width(50));
        for (int i = 0; i < _cachedLanguages.Length; i++)
        {
            GUI.backgroundColor = _previewLanguage == _cachedLanguages[i] ? Color.cyan : Color.white;
            if (GUILayout.Button(_cachedLanguages[i], GUILayout.Width(50)))
                _previewLanguage = _cachedLanguages[i];
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        if (_cachedData.TryGetValue(_previewLanguage, out DataToken lt) &&
            lt.TokenType == TokenType.DataDictionary)
        {
            DataDictionary ld = lt.DataDictionary;
            DataList keys = ld.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(keys[i].String, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(ld[keys[i].String].String, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    // =====================================================================
    // Buscar Canvas en la escena
    // =====================================================================

    /// <summary>
    /// Dibuja las condiciones comunes de exclusion para las herramientas del Editor.
    /// </summary>
    private void DrawExclusionConditions()
    {
        EditorGUILayout.LabelField(
            S("mgr_quick_setup_excluded_keywords_desc"),
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            _excludedLocalizationKeywords,
            new GUIContent(S("mgr_quick_setup_excluded_keywords")),
            true);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            _canvasSearchResults = null;
            _interactionSearchResults = null;
            _interactionDetectedObjectCount = 0;
            _interactionRegisteredObjectCount = 0;
            _scanResultSummary = null;
            _interactionPendingRows = null;
            _interactionExcludedRows = null;
            _interactionRegisteredRows = null;
        }

        EditorGUILayout.Space(5);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            _excludedLocalizationRoots,
            new GUIContent(S("mgr_excluded_gameobjects")),
            true);
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            _canvasSearchResults = null;
            _scanResultSummary = null;
            _interactionPendingRows = null;
            _interactionExcludedRows = null;
            _interactionRegisteredRows = null;
        }

        EditorGUILayout.HelpBox(
            S("mgr_excluded_gameobjects_note"), MessageType.Info);
    }

    /// <summary>
    /// Devuelve true si el Transform pertenece a uno de los objetos excluidos
    /// o se encuentra debajo de uno de ellos.
    /// </summary>
    private bool IsExcludedByGameObject(Transform targetTransform)
    {
        if (targetTransform == null || _excludedLocalizationRoots == null)
            return false;

        for (int i = 0; i < _excludedLocalizationRoots.arraySize; i++)
        {
            GameObject excludedRoot = _excludedLocalizationRoots
                .GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (excludedRoot == null) continue;

            Transform excludedTransform = excludedRoot.transform;
            if (targetTransform == excludedTransform ||
                targetTransform.IsChildOf(excludedTransform))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Devuelve true si el texto pertenece a un Canvas hijo distinto del Canvas analizado.
    /// </summary>
    private static bool IsUnderNestedCanvas(Transform textTransform, Transform canvasRoot)
    {
        Transform current = textTransform.parent;
        while (current != null && current != canvasRoot)
        {
            if (current.GetComponent<Canvas>() != null) return true;
            current = current.parent;
        }
        return false;
    }

    /// <summary>
    /// Cuenta unicamente los textos que se registrarian realmente en el JSON.
    /// </summary>
    private int CountTranslatableTexts(GameObject canvasObject,
        out int tmpCount, out int legacyCount)
    {
        tmpCount = 0;
        legacyCount = 0;
        if (canvasObject == null) return 0;

        Transform root = canvasObject.transform;
        string[] excludedKeywords = ParseQuickSetupExcludedKeywords();

        TextMeshProUGUI[] tmpAll =
            canvasObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < tmpAll.Length; i++)
        {
            TextMeshProUGUI text = tmpAll[i];
            if (IsUnderNestedCanvas(text.transform, root)) continue;
            if (text.GetComponent<TextLocalizer>() != null) continue;
            if (!IsTranslatableText(text.text)) continue;
            if (ContainsExcludedKeyword(text.text, excludedKeywords)) continue;
            tmpCount++;
        }

        Text[] legacyAll = canvasObject.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < legacyAll.Length; i++)
        {
            Text text = legacyAll[i];
            if (IsUnderNestedCanvas(text.transform, root)) continue;
            if (text.GetComponent<TextLocalizer>() != null) continue;
            if (!IsTranslatableText(text.text)) continue;
            if (ContainsExcludedKeyword(text.text, excludedKeywords)) continue;
            legacyCount++;
        }

        return tmpCount + legacyCount;
    }

    private static bool IsTranslatableText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        for (int i = 0; i < text.Length; i++)
            if (char.IsLetter(text[i])) return true;
        return false;
    }

    private static bool ContainsExcludedKeyword(string text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null) return false;
        for (int i = 0; i < keywords.Length; i++)
        {
            string keyword = keywords[i];
            if (string.IsNullOrWhiteSpace(keyword)) continue;
            if (text.IndexOf(keyword.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private class CanvasSearchResult
    {
        public GameObject gameObject;
        public Canvas canvas;
        public int tmpCount;
        public int legacyCount;
        public int missingTextCount;
        public bool hasCanvasLocalizer;
        public string hierarchyPath;
        public string parentCanvasLocalizerName;
    }

    private void ScanSceneForCanvas()
    {
        InvalidateSceneKeyReferenceCounts();
        _canvasSearchResults = new List<CanvasSearchResult>();
        Canvas[] allCanvas = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < allCanvas.Length; i++)
        {
            Canvas canvas = allCanvas[i];
            GameObject go = canvas.gameObject;

            if (IsExcludedByGameObject(go.transform)) continue;

            // Contar solo los textos que se registrarian realmente en el JSON
            int tmpCount;
            int legacyCount;
            CountTranslatableTexts(go, out tmpCount, out legacyCount);

            // Verificar si ya tiene CanvasLocalizer
            bool hasCL = go.GetComponent<CanvasLocalizer>() != null;
            int missingTextCount = hasCL
                ? CanvasLocalizerEditor.CountMissingTexts(
                    go.GetComponent<CanvasLocalizer>())
                : 0;

            // Verificar si es hijo de otro Canvas con CanvasLocalizer
            string parentCLName = null;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                CanvasLocalizer parentCL = parent.GetComponent<CanvasLocalizer>();
                if (parentCL != null)
                {
                    parentCLName = parent.gameObject.name;
                    break;
                }
                parent = parent.parent;
            }

            // Construir path de jerarquia
            string path = BuildHierarchyPath(go.transform);

            _canvasSearchResults.Add(new CanvasSearchResult
            {
                gameObject = go,
                canvas = canvas,
                tmpCount = tmpCount,
                legacyCount = legacyCount,
                missingTextCount = missingTextCount,
                hasCanvasLocalizer = hasCL,
                hierarchyPath = path,
                parentCanvasLocalizerName = parentCLName
            });
        }

        // Filtrar GameObjects destruidos antes de ordenar
        _canvasSearchResults.RemoveAll(r => r.gameObject == null);

        // Ordenar primero por estado y despues por una ruta estructural estable.
        // Esto mantiene el mismo orden al recrear CanvasLocalizer.
        _canvasSearchResults.Sort((a, b) =>
        {
            if (a.hasCanvasLocalizer != b.hasCanvasLocalizer)
                return a.hasCanvasLocalizer ? 1 : -1;
            return string.Compare(
                IdiomasEditorUtils.GetStableHierarchyPath(
                    null, a.gameObject.transform),
                IdiomasEditorUtils.GetStableHierarchyPath(
                    null, b.gameObject.transform),
                System.StringComparison.Ordinal);
        });
    }

    private static string BuildHierarchyPath(Transform t)
    {
        string path = t.name;
        Transform parent = t.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    private void DrawCanvasSearch()
    {
        EditorGUILayout.Space(3);
        GUI.backgroundColor = new Color(0.9f, 0.8f, 0.3f);
        if (GUILayout.Button(S("mgr_scan_scene"), GUILayout.Height(24)))
        {
            ScanSceneForCanvas();
            if (_includeInteractionTexts.boolValue &&
                GetPrefabInteractionLocalizer(
                    target as LocalizationManager) != null)
                ScanInteractionTexts();
            else
            {
                _interactionSearchResults = null;
                _interactionDetectedObjectCount = 0;
                _interactionRegisteredObjectCount = 0;
            }
            RefreshSceneKeyReferenceCounts();
            RefreshScanResultSummary();
        }
        GUI.backgroundColor = Color.white;

        // === CONFIGURACION RAPIDA ===
        // Solo mostrar si hay resultados de escaneo con candidatos
        if (_canvasSearchResults != null &&
            _scanResultSummary != null)
        {
            int qsCandidateCount =
                _scanResultSummary.canvasCandidateCount;
            int qsCandidateTextCount =
                _scanResultSummary.canvasCandidateTextCount;
            int qsInteractionCount =
                _scanResultSummary.interactionTextCount;
            int qsInteractionObjectCount =
                _scanResultSummary.interactionObjectCount;

            if (qsCandidateCount > 0 || qsInteractionCount > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    S("mgr_quick_setup"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    string.Format(
                        S("mgr_quick_setup_combined_desc"),
                        qsCandidateCount,
                        qsInteractionCount,
                        qsCandidateTextCount + qsInteractionCount),
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(3);
                int quickSetupLanguageIndex =
                    IdiomasLanguages.IndexOf(_quickSetupBaseLanguage);
                if (quickSetupLanguageIndex < 0)
                    quickSetupLanguageIndex = IdiomasLanguages.IndexOf("en");
                int newQuickSetupLanguageIndex = EditorGUILayout.Popup(
                    new GUIContent(S("mgr_quick_setup_lang"), S("mgr_quick_setup_lang_tooltip")),
                    quickSetupLanguageIndex,
                    IdiomasLanguages.PopupLabelsLatin);
                if (newQuickSetupLanguageIndex >= 0 &&
                    newQuickSetupLanguageIndex != quickSetupLanguageIndex)
                {
                    _quickSetupBaseLanguage =
                        IdiomasLanguages.Codes[
                            newQuickSetupLanguageIndex];
                    SessionState.SetString(
                        QUICK_SETUP_LANGUAGE_SESSION_KEY,
                        _quickSetupBaseLanguage);
                }

                EditorGUILayout.Space(3);
                bool interactionLocalizerMissing =
                    _includeInteractionTexts.boolValue &&
                    GetPrefabInteractionLocalizer(
                        target as LocalizationManager) == null;
                EditorGUI.BeginDisabledGroup(
                    _scanResultSummary.hasInvalidInteractionKeys ||
                    interactionLocalizerMissing);
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.5f);
                if (GUILayout.Button(
                    string.Format(
                        S("mgr_quick_setup_btn"),
                        qsCandidateCount + qsInteractionObjectCount),
                    GUILayout.Height(28)))
                {
                    QuickSetupAll(
                        qsCandidateCount,
                        qsCandidateTextCount,
                        qsInteractionObjectCount,
                        qsInteractionCount);
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndVertical();
            }
        }

        if (_canvasSearchResults == null)
        {
            EditorGUILayout.HelpBox(S("mgr_scan_hint"), MessageType.Info);
            return;
        }

        if (_canvasSearchResults.Count == 0 &&
            _interactionDetectedObjectCount == 0)
        {
            EditorGUILayout.HelpBox(S("mgr_no_canvas"), MessageType.Info);
            return;
        }

        if (_canvasSearchResults.Count > 0 ||
            _interactionDetectedObjectCount > 0)
        {
            EditorGUILayout.LabelField(
                string.Format(
                    S("mgr_found_summary"),
                    _canvasSearchResults.Count,
                    _interactionDetectedObjectCount,
                    _scanResultSummary.canvasCandidateCount +
                        _scanResultSummary.interactionObjectCount,
                    _scanResultSummary.localizedCanvasCount +
                        _interactionRegisteredObjectCount,
                    _scanResultSummary.noTextCanvasCount),
                EditorStyles.helpBox);
        }

        EditorGUILayout.Space(3);
        DrawGlobalLocalizerActions();

        _showCanvasResults = EditorGUILayout.Foldout(
            _showCanvasResults,
            $"Canvas ({_canvasSearchResults.Count})",
            true);
        if (_showCanvasResults)
        {

        // --- Candidatos (sin CanvasLocalizer, con textos) ---
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.hasCanvasLocalizer) continue;
            int totalTexts = r.tmpCount + r.legacyCount;
            if (totalTexts == 0) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Nombre clickable para seleccionar en jerarquia
            GUIStyle linkStyle = new GUIStyle(EditorStyles.label);
            linkStyle.fontStyle = FontStyle.Bold;
            linkStyle.normal.textColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button(
                new GUIContent(r.gameObject.name, r.hierarchyPath),
                linkStyle,
                GUILayout.ExpandWidth(false)))
            {
                EditorGUIUtility.PingObject(r.gameObject);
            }

            // Conteo de textos
            string textsInfo = "";
            if (r.tmpCount > 0) textsInfo += $"{r.tmpCount} TMP";
            if (r.legacyCount > 0)
            {
                if (textsInfo.Length > 0) textsInfo += " + ";
                textsInfo += $"{r.legacyCount} Legacy";
            }
            EditorGUILayout.LabelField(textsInfo, GUILayout.Width(120));

            // Empujar boton a la derecha
            GUILayout.FlexibleSpace();

            // Boton para anadir CanvasLocalizer
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
            if (GUILayout.Button(S("mgr_add_cl"), GUILayout.Width(170)))
            {
                AddCanvasLocalizerTo(r);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            // Aviso si es hijo de otro Canvas con CanvasLocalizer
            if (r.parentCanvasLocalizerName != null)
            {
                EditorGUILayout.HelpBox(
                    string.Format(S("mgr_child_warning"), r.parentCanvasLocalizerName),
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        // --- Ya localizados (con CanvasLocalizer) ---
        bool hasLocalized = false;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (!r.hasCanvasLocalizer) continue;

            if (!hasLocalized)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(
                    S("mgr_already_localized"), EditorStyles.boldLabel);

                hasLocalized = true;
            }

            if (r.gameObject == null) continue;
            CanvasLocalizer cl = r.gameObject.GetComponent<CanvasLocalizer>();
            string clId = cl != null ? cl.GetCanvasId() : "";

            // Obtener claves del componente y contar cuantas estan en el JSON
            List<string> clKeys = GetCanvasLocalizerKeys(cl);
            int keysInJson = CountKeysInJson(clKeys);
            int totalKeys = clKeys.Count;
            List<string> deletableKeys;
            int sharedKeyCount;
            GetCanvasKeyUsage(
                clKeys, out deletableKeys, out sharedKeyCount);
            int deletableKeyCount = deletableKeys.Count;
            Dictionary<string, string> canvasBaseEntries =
                GetCanvasBaseLanguageEntries(cl);
            List<RegisteredCanvasEntry> canvasEntries =
                BuildRegisteredCanvasEntries(new SerializedObject(cl));
            int cleanEntries = 0;
            int modifiedEntries = 0;
            int missingEntries = 0;
            for (int e = 0; e < canvasEntries.Count; e++)
            {
                string key = canvasEntries[e].key.stringValue;
                if (!canvasBaseEntries.TryGetValue(
                    key, out string storedText))
                {
                    missingEntries++;
                }
                else if (storedText == canvasEntries[e].text)
                {
                    cleanEntries++;
                }
                else
                {
                    modifiedEntries++;
                }
            }

            string jsonStatusText;
            if (totalKeys == 0)
            {
                jsonStatusText = S("mgr_no_keys");
            }
            else if (modifiedEntries > 0 || missingEntries > 0)
            {
                jsonStatusText =
                    $"JSON ✓ {cleanEntries}  △ {modifiedEntries}  ! {missingEntries}";
            }
            else
            {
                jsonStatusText =
                    $"JSON ✓ {cleanEntries}  △ 0  ! 0";
            }
            if (sharedKeyCount > 0)
            {
                jsonStatusText += "  |  " +
                    string.Format(
                        S("mgr_shared_keys"), sharedKeyCount);
            }
            int missingKeys = missingEntries + modifiedEntries +
                r.missingTextCount;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Linea 1: nombre, informacion del Canvas y estado JSON
            GUIStyle canvasLinkStyle = new GUIStyle(EditorStyles.label);
            canvasLinkStyle.normal.textColor = new Color(0.3f, 0.6f, 1f);
            string canvasCountText =
                string.Format(
                    S("mgr_text_count"),
                    cl != null ? cl.GetTextCount() : 0) +
                (r.missingTextCount > 0
                    ? "  |  " + string.Format(
                        S("mgr_unregistered_text_count"),
                        r.missingTextCount)
                    : "");
            DrawLocalizedObjectStatusRow(
                r.gameObject,
                r.hierarchyPath,
                canvasCountText,
                jsonStatusText,
                totalKeys == 0 ||
                modifiedEntries > 0 ||
                missingEntries > 0,
                canvasLinkStyle);

            // Linea 2: acciones disponibles
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"canvasId: \"{clId}\"",
                EditorStyles.miniLabel,
                GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();

            // Boton para restaurar claves faltantes al JSON
            if (missingKeys > 0)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
                if (GUILayout.Button(
                    string.Format(
                        S("mgr_restore_json"), missingKeys),
                    GUILayout.Width(140)))
                {
                    RestoreKeysToJson(cl, clKeys);
                }
                GUI.backgroundColor = Color.white;
            }

            // Boton para limpiar claves del JSON
            if (missingKeys == 0 && deletableKeyCount > 0)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
                if (GUILayout.Button(
                    string.Format(
                        S("mgr_clean_json"), deletableKeyCount),
                    GUILayout.Width(130)))
                {
                    RemoveKeysFromJson(
                        cl, deletableKeys, deletableKeyCount);
                }
                GUI.backgroundColor = Color.white;
            }

            // Boton para quitar CanvasLocalizer
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button(S("mgr_remove_cl"), GUILayout.Width(60)))
            {
                RemoveCanvasLocalizerFrom(r);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // --- Sin textos ---
        bool hasEmpty = false;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.hasCanvasLocalizer) continue;
            if (r.tmpCount + r.legacyCount > 0) continue;

            if (!hasEmpty)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(S("mgr_no_translatable"), EditorStyles.miniLabel);
                hasEmpty = true;
            }

            GUIStyle grayStyle = new GUIStyle(EditorStyles.miniLabel);
            grayStyle.normal.textColor = Color.gray;
            EditorGUILayout.LabelField(
                $"  {r.gameObject.name}, {r.hierarchyPath}",
                grayStyle);
        }
        }

        InteractionLocalizer interactionLocalizer =
            GetPrefabInteractionLocalizer(target as LocalizationManager);
        int interactionCandidateCount = _scanResultSummary != null
            ? _scanResultSummary.interactionTextCount
            : 0;
        if (_includeInteractionTexts.boolValue ||
            interactionLocalizer != null ||
            interactionCandidateCount > 0)
        {
            _showInteractionResults = EditorGUILayout.Foldout(
                _showInteractionResults,
                $"Interaction ({_interactionDetectedObjectCount})",
                true);
            if (_showInteractionResults)
            {
                if (interactionLocalizer == null)
                {
                    EditorGUILayout.HelpBox(
                        S("mgr_interaction_missing"),
                        MessageType.Warning);
                    EditorGUILayout.Space(3);
                    Color previousBackgroundColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
                    if (GUILayout.Button(
                        S("mgr_add_interaction_localizer"),
                        GUILayout.Height(24)))
                    {
                        interactionLocalizer =
                            AddPrefabInteractionLocalizer();
                    }
                    GUI.backgroundColor = previousBackgroundColor;
                }
                else
                {
                    DrawInteractionLocalizerEditLink(
                        interactionLocalizer);
                }
                DrawInteractionResults();
                if (interactionLocalizer != null)
                    DrawInteractionLocalizerList(interactionLocalizer);
                DrawExcludedInteractionResults();
            }
        }
    }

    private void RemoveCanvasLocalizerFrom(CanvasSearchResult result)
    {
        GameObject go = result.gameObject;
        CanvasLocalizer cl = go.GetComponent<CanvasLocalizer>();
        if (cl == null) return;

        string canvasId = cl.GetCanvasId();
        int textCount = cl.GetTextCount();
        if (!EditorUtility.DisplayDialog(S("mgr_remove_cl_title"),
            string.Format(S("mgr_remove_cl_msg"), go.name, canvasId, textCount),
            S("mgr_remove_cl_confirm"), S("cancel")))
        {
            return;
        }

        // Quitar de la lista de canvasLocalizers del manager
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            if (_canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue == cl)
            {
                // En Unity, si el slot tiene referencia, el primer DeleteArrayElement solo lo pone a null
                _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue = null;
                _canvasLocalizers.DeleteArrayElementAtIndex(i);
                serializedObject.ApplyModifiedProperties();
                break;
            }
        }

        // Quitar el UdonBehaviour asociado (backing) y luego el componente C#
        UdonBehaviour udon = IdiomasEditorUtils.FindUdonBehaviourFor(cl);
        if (udon != null) Undo.DestroyObjectImmediate(udon);
        Undo.DestroyObjectImmediate(cl);

        result.hasCanvasLocalizer = false;
        EditorUtility.SetDirty(go);

        Debug.Log($"[Idiomas] CanvasLocalizer quitado de \"{go.name}\".");
    }

    private void AddCanvasLocalizerTo(CanvasSearchResult result)
    {
        if (result == null || result.gameObject == null) return;
        if (IsExcludedByGameObject(result.gameObject.transform)) return;

        int currentTmpCount;
        int currentLegacyCount;
        if (CountTranslatableTexts(result.gameObject,
            out currentTmpCount, out currentLegacyCount) == 0)
        {
            EditorUtility.DisplayDialog(
                S("mgr_no_translatable_title"),
                S("mgr_no_translatable_msg"), S("ok"));
            ScanSceneForCanvas();
            RefreshScanResultSummary();
            return;
        }

        LocalizationManager mgr = (LocalizationManager)target;
        GameObject go = result.gameObject;

        // Usar UdonSharpUndo para soporte de Ctrl+Z
        CanvasLocalizer newCL = UdonSharpUndo.AddComponent<CanvasLocalizer>(go);

        if (newCL == null)
        {
            EditorUtility.DisplayDialog(S("mgr_add_cl_error_title"),
                S("mgr_add_cl_error_msg"),
                S("ok"));
            return;
        }

        // Asignar el LocalizationManager y un canvasId unico
        SerializedObject clSO = new SerializedObject(newCL);
        SerializedProperty mgrProp = clSO.FindProperty("manager");
        SerializedProperty idProp = clSO.FindProperty("canvasId");

        if (mgrProp != null) mgrProp.objectReferenceValue = mgr;

        // Generar ID unico: normalizar nombre + verificar colisiones
        string uniqueId = IdiomasEditorUtils.GenerateUniqueCanvasId(go.name, newCL);
        if (idProp != null) idProp.stringValue = uniqueId;

        ApplyQuickSetupExcludedKeywords(clSO);
        clSO.ApplyModifiedProperties();

        // Registrar en el array canvasLocalizers del manager
        bool alreadyRegistered = false;
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            if (_canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue == newCL)
            {
                alreadyRegistered = true;
                break;
            }
        }
        if (!alreadyRegistered)
        {
            int idx = _canvasLocalizers.arraySize;
            _canvasLocalizers.arraySize = idx + 1;
            _canvasLocalizers.GetArrayElementAtIndex(idx).objectReferenceValue = newCL;
            serializedObject.ApplyModifiedProperties();
        }

        // Marcar resultado como ya localizado
        result.hasCanvasLocalizer = true;
        RefreshScanResultSummary();

        EditorUtility.SetDirty(go);

        Debug.Log($"[Idiomas] CanvasLocalizer anadido a \"{go.name}\" con canvasId=\"{uniqueId}\".");

        // Seleccionar el objeto para que el usuario vea el nuevo componente
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    // =====================================================================
    // Gestion de claves JSON por CanvasLocalizer
    // =====================================================================

    /// <summary>
    /// Obtiene todas las claves (tmpKeys + legacyKeys) de un CanvasLocalizer.
    /// </summary>
    private List<string> GetCanvasLocalizerKeys(CanvasLocalizer cl)
    {
        List<string> keys = new List<string>();
        HashSet<string> uniqueKeys = new HashSet<string>();
        if (cl == null) return keys;

        SerializedObject clSO = new SerializedObject(cl);

        SerializedProperty tmpKeys = clSO.FindProperty("tmpKeys");
        if (tmpKeys != null)
        {
            for (int i = 0; i < tmpKeys.arraySize; i++)
            {
                string key = tmpKeys.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(key) && uniqueKeys.Add(key)) keys.Add(key);
            }
        }

        SerializedProperty legKeys = clSO.FindProperty("legacyKeys");
        if (legKeys != null)
        {
            for (int i = 0; i < legKeys.arraySize; i++)
            {
                string key = legKeys.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(key) && uniqueKeys.Add(key)) keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Cuenta cuantas claves de la lista existen en al menos un idioma del JSON cacheado.
    /// </summary>
    private int CountKeysInJson(List<string> keys)
    {
        if (_cachedData == null || _cachedLanguages == null || keys.Count == 0) return 0;

        int count = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            for (int j = 0; j < _cachedLanguages.Length; j++)
            {
                if (_cachedData.TryGetValue(_cachedLanguages[j], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary &&
                    lt.DataDictionary.ContainsKey(keys[i]))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Elimina las claves de un CanvasLocalizer del archivo JSON de traducciones.
    /// </summary>
    private bool IsTranslationKeyUsedElsewhere(string key, CanvasLocalizer excludedLocalizer)
    {
        int referencesInExcludedLocalizer = 0;
        if (excludedLocalizer != null)
        {
            SerializedObject localizerSO =
                new SerializedObject(excludedLocalizer);
            string[] keyArrays = { "tmpKeys", "legacyKeys" };
            for (int a = 0; a < keyArrays.Length; a++)
            {
                SerializedProperty keys =
                    localizerSO.FindProperty(keyArrays[a]);
                if (keys == null) continue;
                for (int i = 0; i < keys.arraySize; i++)
                {
                    if (keys.GetArrayElementAtIndex(i).stringValue == key)
                        referencesInExcludedLocalizer++;
                }
            }
        }

        return GetSceneTranslationKeyReferenceCount(key) >
            referencesInExcludedLocalizer;
    }

    private void InvalidateSceneKeyReferenceCounts()
    {
        _sceneKeyReferenceCountsDirty = true;
    }

    private void MarkTranslationFileStateDirty()
    {
        _translationFileStateDirty = true;
    }

    private void ResetTranslationState()
    {
        ClearTranslationJsonCache();
        _canvasSearchResults = null;
        _interactionSearchResults = null;
        _interactionDetectedObjectCount = 0;
        _interactionRegisteredObjectCount = 0;
        _scanResultSummary = null;
        _interactionPendingRows = null;
        _interactionExcludedRows = null;
        _interactionRegisteredRows = null;
        _hasTranslationFileStamp = false;
        _translationFileStateDirty = true;
        InvalidateSceneKeyReferenceCounts();
    }

    private void ClearTranslationJsonCache()
    {
        _cachedData = null;
        _cachedLanguages = null;
        _cachedKeys = null;
        _cachedTranslations = null;
        _cachedJsonHash = null;
    }

    private int GetSceneTranslationKeyReferenceCount(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        RefreshSceneKeyReferenceCounts();
        return _sceneKeyReferenceCounts.TryGetValue(
            key, out int count) ? count : 0;
    }

    private void RefreshSceneKeyReferenceCounts()
    {
        if (!_sceneKeyReferenceCountsDirty &&
            _sceneKeyReferenceCounts != null)
        {
            return;
        }

        _sceneKeyReferenceCounts =
            IdiomasEditorUtils.BuildSceneTranslationKeyReferenceCounts();
        _sceneKeyReferenceCountsDirty = false;
    }

    private void GetCanvasKeyUsage(
        List<string> keys, out List<string> deletable,
        out int shared)
    {
        deletable = new List<string>();
        shared = 0;
        Dictionary<string, int> localReferences =
            new Dictionary<string, int>(System.StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            if (string.IsNullOrEmpty(key)) continue;
            if (localReferences.TryGetValue(key, out int count))
                localReferences[key] = count + 1;
            else
                localReferences[key] = 1;
        }

        foreach (KeyValuePair<string, int> pair in localReferences)
        {
            if (!IsKeyInJson(pair.Key)) continue;
            if (GetSceneTranslationKeyReferenceCount(pair.Key) > pair.Value)
                shared++;
            else
                deletable.Add(pair.Key);
        }
    }

    private bool IsKeyInJson(string key)
    {
        if (_cachedData == null || _cachedLanguages == null ||
            string.IsNullOrEmpty(key))
        {
            return false;
        }
        for (int i = 0; i < _cachedLanguages.Length; i++)
        {
            if (_cachedData.TryGetValue(
                    _cachedLanguages[i], out DataToken language) &&
                language.TokenType == TokenType.DataDictionary &&
                language.DataDictionary.ContainsKey(key))
            {
                return true;
            }
        }
        return false;
    }

    private void RemoveKeysFromJson(CanvasLocalizer cl, List<string> keys, int keysInJson)
    {
        RemoveKeysFromJson(keys, keysInJson, cl.GetCanvasId(), cl);
    }

    /// <summary>
    /// Elimina una lista de claves del archivo JSON de traducciones.
    /// </summary>
    private void RemoveKeysFromJson(
        List<string> keys,
        int keysInJson,
        string localizerId,
        CanvasLocalizer excludedLocalizer)
    {
        if (keys.Count == 0 || keysInJson == 0) return;

        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog(S("no_file_title"),
                S("no_file_msg"), S("ok"));
            return;
        }

        if (!EditorUtility.DisplayDialog(S("mgr_clean_json_title"),
            string.Format(S("mgr_clean_json_msg"), keysInJson, localizerId),
            S("mgr_clean_json_confirm"), S("cancel")))
        {
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(ta);
        string fullPath = Path.GetFullPath(assetPath);
        string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);

        // Parsear JSON a diccionario editable
        var translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
        if (translations == null)
        {
            EditorUtility.DisplayDialog(S("error_title"), S("error_parse_json"), S("ok"));
            return;
        }

        // Crear HashSet para busqueda rapida
        HashSet<string> keysToRemove = new HashSet<string>();
        for (int i = 0; i < keys.Count; i++)
        {
            if (!IsTranslationKeyUsedElsewhere(
                keys[i], excludedLocalizer))
                keysToRemove.Add(keys[i]);
        }

        // Eliminar claves de todos los idiomas
        int totalRemoved = 0;
        foreach (var langPair in translations)
        {
            List<string> toRemove = new List<string>();
            foreach (string key in langPair.Value.Keys)
            {
                if (keysToRemove.Contains(key))
                    toRemove.Add(key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                langPair.Value.Remove(toRemove[i]);
                totalRemoved++;
            }
        }

        if (totalRemoved == 0)
        {
            EditorUtility.DisplayDialog(S("no_changes_clean"),
                S("no_changes_clean_msg"), S("ok"));
            return;
        }

        // Escribir JSON actualizado
        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        // Invalidar cache para que se recargue
        _cachedJsonHash = null;

        Debug.Log($"[Idiomas] Eliminadas {totalRemoved} entradas del JSON (localizer \"{localizerId}\").");
        EditorUtility.DisplayDialog(S("clean_done_title"),
            string.Format(S("clean_done_msg"), totalRemoved, keysInJson, totalRemoved / Mathf.Max(keysInJson, 1)),
            S("ok"));
    }

    // =====================================================================
    // Restaurar claves faltantes al JSON
    // =====================================================================

    /// <summary>
    /// Restaura las claves que faltan y sincroniza los textos modificados.
    /// Las claves compartidas se separan antes de actualizar el idioma base.
    /// </summary>
    private void RestoreKeysToJson(CanvasLocalizer cl, List<string> allKeys)
    {
        if (cl == null) return;

        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog(S("no_file_title"),
                S("no_file_msg"), S("ok"));
            return;
        }

        string baseLang = cl.GetBaseLanguage();
        if (string.IsNullOrEmpty(baseLang))
        {
            EditorUtility.DisplayDialog(S("no_base_lang_title"),
                S("no_base_lang_msg"), S("ok"));
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(ta);
        string fullPath = Path.GetFullPath(assetPath);
        string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);

        var translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
        if (translations == null)
        {
            EditorUtility.DisplayDialog(S("error_title"), S("error_parse_json"), S("ok"));
            return;
        }

        int appended = CanvasLocalizerEditor.AppendMissingTexts(
            cl, cl.GetBaseLanguage(), translations);
        if (appended > 0) InvalidateSceneKeyReferenceCounts();
        SourceTranslationUpdateMode updateMode =
            ConfirmModifiedSourceTexts(
                CountModifiedCanvasEntries(cl, translations));
        if (updateMode == SourceTranslationUpdateMode.Cancel) return;

        HashSet<string> orphanCandidates = new HashSet<string>();
        int added = appended + SyncCanvasLocalizerToJson(
            cl,
            translations,
            updateMode ==
                SourceTranslationUpdateMode.ClearTranslations,
            orphanCandidates);
        int removed = ConfirmAndRemoveOrphanedKeys(
            translations, orphanCandidates);

        if (added == 0 && removed == 0)
        {
            EditorUtility.DisplayDialog(S("no_changes"),
                S("no_changes_msg"), S("ok"));
            return;
        }

        // Escribir JSON actualizado
        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        _cachedJsonHash = null;
        int appliedTexts = ApplySynchronizedTextsInEditor(
            translations,
            new List<CanvasLocalizer> { cl },
            new List<InteractionLocalizer>());

        string canvasId = cl.GetCanvasId();
        Debug.Log(
            $"[Idiomas] Restauradas {added} claves al JSON " +
            $"(canvas \"{canvasId}\", idioma \"{baseLang}\"). " +
            $"{appliedTexts} textos reaplicados.");
        EditorUtility.DisplayDialog(S("mgr_restore_json_title"),
            string.Format(S("mgr_restore_json_msg"), added, baseLang, canvasId),
            S("ok"));
    }

    /// <summary>
    /// Construye un mapa clave -> texto actual leyendo los arrays serializados
    /// de un CanvasLocalizer y el texto de cada componente referenciado.
    /// </summary>
    private Dictionary<string, string> BuildKeyTextMap(CanvasLocalizer cl)
    {
        Dictionary<string, string> map = new Dictionary<string, string>();
        SerializedObject clSO = new SerializedObject(cl);

        SerializedProperty tmpTexts = clSO.FindProperty("tmpTexts");
        SerializedProperty tmpKeys = clSO.FindProperty("tmpKeys");
        if (tmpTexts != null && tmpKeys != null)
        {
            int count = Mathf.Min(tmpTexts.arraySize, tmpKeys.arraySize);
            for (int i = 0; i < count; i++)
            {
                string key = tmpKeys.GetArrayElementAtIndex(i).stringValue;
                TextMeshProUGUI comp = tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue as TextMeshProUGUI;
                if (!string.IsNullOrEmpty(key) && comp != null && !string.IsNullOrEmpty(comp.text))
                    map[key] = comp.text;
            }
        }

        SerializedProperty legTexts = clSO.FindProperty("legacyTexts");
        SerializedProperty legKeys = clSO.FindProperty("legacyKeys");
        if (legTexts != null && legKeys != null)
        {
            int count = Mathf.Min(legTexts.arraySize, legKeys.arraySize);
            for (int i = 0; i < count; i++)
            {
                string key = legKeys.GetArrayElementAtIndex(i).stringValue;
                Text comp = legTexts.GetArrayElementAtIndex(i).objectReferenceValue as Text;
                if (!string.IsNullOrEmpty(key) && comp != null && !string.IsNullOrEmpty(comp.text))
                    map[key] = comp.text;
            }
        }

        return map;
    }

    private static List<RegisteredCanvasEntry> BuildRegisteredCanvasEntries(
        SerializedObject localizerSO)
    {
        List<RegisteredCanvasEntry> entries =
            new List<RegisteredCanvasEntry>();
        AddRegisteredCanvasEntries(
            localizerSO.FindProperty("tmpTexts"),
            localizerSO.FindProperty("tmpKeys"),
            entries);
        AddRegisteredCanvasEntries(
            localizerSO.FindProperty("legacyTexts"),
            localizerSO.FindProperty("legacyKeys"),
            entries);
        return entries;
    }

    private static void AddRegisteredCanvasEntries(
        SerializedProperty texts,
        SerializedProperty keys,
        List<RegisteredCanvasEntry> entries)
    {
        if (texts == null || keys == null) return;
        int count = Mathf.Min(texts.arraySize, keys.arraySize);
        for (int i = 0; i < count; i++)
        {
            Component component =
                texts.GetArrayElementAtIndex(i).objectReferenceValue
                as Component;
            string text = "";
            if (component is TextMeshProUGUI tmp) text = tmp.text;
            else if (component is Text legacy) text = legacy.text;
            if (component == null || string.IsNullOrWhiteSpace(text)) continue;
            entries.Add(new RegisteredCanvasEntry
            {
                component = component,
                key = keys.GetArrayElementAtIndex(i),
                text = text
            });
        }
    }

    private int SyncCanvasLocalizerToJson(
        CanvasLocalizer localizer,
        Dictionary<string, Dictionary<string, string>> translations,
        bool clearModifiedTranslations,
        HashSet<string> orphanCandidates)
    {
        if (localizer == null || translations == null) return 0;
        string baseLanguage = localizer.GetBaseLanguage();
        if (string.IsNullOrEmpty(baseLanguage)) return 0;
        if (!translations.ContainsKey(baseLanguage))
            translations[baseLanguage] = new Dictionary<string, string>();
        Dictionary<string, string> baseEntries = translations[baseLanguage];

        SerializedObject localizerSO = new SerializedObject(localizer);
        List<RegisteredCanvasEntry> entries =
            BuildRegisteredCanvasEntries(localizerSO);
        Dictionary<string, string> canonicalByText =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        HashSet<string> reservedKeys =
            new HashSet<string>(baseEntries.Keys);
        for (int i = 0; i < entries.Count; i++)
        {
            string key = entries[i].key.stringValue;
            if (!string.IsNullOrWhiteSpace(key)) reservedKeys.Add(key);
        }
        foreach (KeyValuePair<string, string> pair in baseEntries)
        {
            string normalizedValue =
                IdiomasEditorUtils.NormalizeLineEndings(pair.Value);
            if (!string.IsNullOrEmpty(normalizedValue) &&
                !canonicalByText.ContainsKey(normalizedValue))
            {
                canonicalByText[normalizedValue] = pair.Key;
            }
        }

        Dictionary<string, int> referenceCounts =
            new Dictionary<string, int>();
        int updated = 0;
        int reassigned = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            RegisteredCanvasEntry entry = entries[i];
            string normalizedText =
                IdiomasEditorUtils.NormalizeLineEndings(entry.text);
            string oldKey = entry.key.stringValue;
            string key = oldKey;
            if (canonicalByText.TryGetValue(
                normalizedText, out string canonicalKey))
            {
                key = canonicalKey;
            }
            else if (!string.IsNullOrWhiteSpace(oldKey) &&
                baseEntries.TryGetValue(oldKey, out string storedText) &&
                !IdiomasEditorUtils.TextEquals(
                    storedText, normalizedText) &&
                GetInteractionReferenceCount(referenceCounts, oldKey) > 1)
            {
                key = GenerateInteractionDetachedKey(
                    BuildCanvasNativeKey(localizer, entry),
                    reservedKeys,
                    referenceCounts);
            }
            else if (string.IsNullOrWhiteSpace(key))
            {
                key = GenerateInteractionDetachedKey(
                    "canvas_text", reservedKeys, referenceCounts);
            }

            if (key != oldKey)
            {
                if (!string.IsNullOrWhiteSpace(oldKey))
                {
                    referenceCounts[oldKey] = Mathf.Max(
                        0,
                        GetInteractionReferenceCount(
                            referenceCounts, oldKey) - 1);
                }
                referenceCounts[key] =
                    GetInteractionReferenceCount(referenceCounts, key) + 1;
                entry.key.stringValue = key;
                if (!string.IsNullOrWhiteSpace(oldKey))
                    orphanCandidates?.Add(oldKey);
                reassigned++;
            }

            reservedKeys.Add(key);
            canonicalByText[normalizedText] = key;
            bool modifiesExistingKey =
                key == oldKey &&
                baseEntries.TryGetValue(key, out string current) &&
                !IdiomasEditorUtils.TextEquals(
                    current, normalizedText);
            if (!baseEntries.TryGetValue(key, out current) ||
                !IdiomasEditorUtils.TextEquals(
                    current, normalizedText))
            {
                if (modifiesExistingKey && clearModifiedTranslations)
                {
                    RemoveTranslationsExceptBase(
                        translations, baseLanguage, key);
                }
                baseEntries[key] = normalizedText;
                updated++;
            }
        }

        localizerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(localizer);
        if (reassigned > 0) InvalidateSceneKeyReferenceCounts();
        return updated + reassigned;
    }

    private static void RemoveTranslationsExceptBase(
        Dictionary<string, Dictionary<string, string>> translations,
        string baseLanguage,
        string key)
    {
        foreach (KeyValuePair<string, Dictionary<string, string>> language
            in translations)
        {
            if (language.Key != baseLanguage)
                language.Value.Remove(key);
        }
    }

    private SourceTranslationUpdateMode ConfirmModifiedSourceTexts(
        int modifiedCount)
    {
        if (modifiedCount == 0)
            return SourceTranslationUpdateMode.KeepTranslations;

        int result = EditorUtility.DisplayDialogComplex(
            S("mgr_sync_modified_title"),
            string.Format(S("mgr_sync_modified_msg"), modifiedCount),
            S("mgr_sync_clear_translations"),
            S("cancel"),
            S("mgr_sync_keep_translations"));
        if (result == 0)
            return SourceTranslationUpdateMode.ClearTranslations;
        if (result == 2)
            return SourceTranslationUpdateMode.KeepTranslations;
        return SourceTranslationUpdateMode.Cancel;
    }

    private static int CountModifiedCanvasEntries(
        CanvasLocalizer localizer,
        Dictionary<string, Dictionary<string, string>> translations)
    {
        if (localizer == null || translations == null) return 0;
        string baseLanguage = localizer.GetBaseLanguage();
        if (!translations.TryGetValue(
            baseLanguage, out Dictionary<string, string> baseEntries))
        {
            return 0;
        }

        int modified = 0;
        List<RegisteredCanvasEntry> entries =
            BuildRegisteredCanvasEntries(new SerializedObject(localizer));
        for (int i = 0; i < entries.Count; i++)
        {
            string key = entries[i].key.stringValue;
            if (baseEntries.TryGetValue(key, out string storedText) &&
                !IdiomasEditorUtils.TextEquals(
                    storedText, entries[i].text))
            {
                modified++;
            }
        }
        return modified;
    }

    private static int CountModifiedInteractionEntries(
        InteractionLocalizer localizer,
        Dictionary<string, Dictionary<string, string>> translations)
    {
        if (localizer == null || translations == null) return 0;
        string baseLanguage = localizer.GetBaseLanguage();
        if (!translations.TryGetValue(
            baseLanguage, out Dictionary<string, string> baseEntries))
        {
            return 0;
        }

        SerializedObject localizerSO = new SerializedObject(localizer);
        EnsureInteractionEnabledArrays(localizerSO);
        Dictionary<GameObject, List<RegisteredInteractionEntry>> groups =
            BuildRegisteredInteractionGroups(localizerSO);
        int modified = 0;
        foreach (var pair in groups)
        {
            List<RegisteredInteractionEntry> entries = pair.Value;
            for (int i = 0; i < entries.Count; i++)
            {
                RegisteredInteractionEntry entry = entries[i];
                string key = entry.key.stringValue;
                if (entry.enabled.boolValue &&
                    baseEntries.TryGetValue(key, out string storedText) &&
                    !IdiomasEditorUtils.TextEquals(
                        storedText, entry.text))
                {
                    modified++;
                }
            }
        }
        return modified;
    }

    /// <summary>
    /// Obtiene las entradas del idioma base para comprobar su sincronizacion.
    /// </summary>
    private Dictionary<string, string> GetInteractionBaseLanguageEntries(
        InteractionLocalizer localizer)
    {
        if (_cachedTranslations == null || localizer == null)
            return new Dictionary<string, string>();

        string baseLanguage = localizer.GetBaseLanguage();
        if (_cachedTranslations.TryGetValue(
                baseLanguage, out Dictionary<string, string> entries))
        {
            return entries;
        }
        return new Dictionary<string, string>();
    }

    private Dictionary<string, string> GetCanvasBaseLanguageEntries(
        CanvasLocalizer localizer)
    {
        if (_cachedTranslations == null || localizer == null)
            return new Dictionary<string, string>();

        string baseLanguage = localizer.GetBaseLanguage();
        if (_cachedTranslations.TryGetValue(
                baseLanguage, out Dictionary<string, string> entries))
        {
            return entries;
        }
        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Sincroniza los textos registrados con el idioma base sin modificar
    /// una clave compartida por otros componentes.
    /// </summary>
    private int SyncInteractionLocalizerToJson(
        InteractionLocalizer localizer,
        Dictionary<string, Dictionary<string, string>> translations,
        bool clearModifiedTranslations,
        HashSet<string> orphanCandidates)
    {
        if (localizer == null || translations == null) return 0;

        string baseLanguage = localizer.GetBaseLanguage();
        if (string.IsNullOrEmpty(baseLanguage)) return 0;
        if (!translations.ContainsKey(baseLanguage))
            translations[baseLanguage] = new Dictionary<string, string>();
        Dictionary<string, string> baseEntries = translations[baseLanguage];

        SerializedObject localizerSO = new SerializedObject(localizer);
        EnsureInteractionEnabledArrays(localizerSO);
        Dictionary<GameObject, List<RegisteredInteractionEntry>> groups =
            BuildRegisteredInteractionGroups(localizerSO);
        List<RegisteredInteractionEntry> entries =
            new List<RegisteredInteractionEntry>();
        foreach (var pair in groups)
            entries.AddRange(pair.Value);

        Dictionary<string, string> canonicalByText =
            new Dictionary<string, string>(System.StringComparer.Ordinal);
        HashSet<string> reservedKeys =
            new HashSet<string>(baseEntries.Keys);
        for (int i = 0; i < entries.Count; i++)
        {
            string key = entries[i].key.stringValue;
            if (!string.IsNullOrWhiteSpace(key)) reservedKeys.Add(key);
        }
        foreach (KeyValuePair<string, string> pair in baseEntries)
        {
            string normalizedValue =
                IdiomasEditorUtils.NormalizeLineEndings(pair.Value);
            if (!string.IsNullOrEmpty(normalizedValue) &&
                !canonicalByText.ContainsKey(normalizedValue))
            {
                canonicalByText[normalizedValue] = pair.Key;
            }
        }

        Dictionary<string, int> referenceCounts =
            new Dictionary<string, int>();
        int updated = 0;
        int reassigned = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            RegisteredInteractionEntry entry = entries[i];
            if (!entry.enabled.boolValue ||
                string.IsNullOrWhiteSpace(entry.text))
            {
                continue;
            }

            string normalizedText =
                IdiomasEditorUtils.NormalizeLineEndings(entry.text);
            string oldKey = entry.key.stringValue;
            string key = oldKey;
            if (canonicalByText.TryGetValue(
                normalizedText, out string canonicalKey))
            {
                key = canonicalKey;
            }
            else if (!string.IsNullOrWhiteSpace(oldKey) &&
                baseEntries.TryGetValue(oldKey, out string storedText) &&
                !IdiomasEditorUtils.TextEquals(
                    storedText, normalizedText) &&
                GetInteractionReferenceCount(referenceCounts, oldKey) > 1)
            {
                key = GenerateInteractionDetachedKey(
                    BuildInteractionNativeKey(entry),
                    reservedKeys,
                    referenceCounts);
            }
            else if (string.IsNullOrWhiteSpace(key))
            {
                key = GenerateInteractionDetachedKey(
                    "interaction", reservedKeys, referenceCounts);
            }

            if (key != oldKey)
            {
                if (!string.IsNullOrWhiteSpace(oldKey))
                {
                    referenceCounts[oldKey] = Mathf.Max(
                        0,
                        GetInteractionReferenceCount(
                            referenceCounts, oldKey) - 1);
                }
                referenceCounts[key] =
                    GetInteractionReferenceCount(referenceCounts, key) + 1;
                entry.key.stringValue = key;
                if (!string.IsNullOrWhiteSpace(oldKey))
                    orphanCandidates?.Add(oldKey);
                reassigned++;
            }

            reservedKeys.Add(key);
            canonicalByText[normalizedText] = key;
            bool modifiesExistingKey =
                key == oldKey &&
                baseEntries.TryGetValue(key, out string current) &&
                !IdiomasEditorUtils.TextEquals(
                    current, normalizedText);
            if (!baseEntries.TryGetValue(key, out current) ||
                !IdiomasEditorUtils.TextEquals(
                    current, normalizedText))
            {
                if (modifiesExistingKey && clearModifiedTranslations)
                {
                    RemoveTranslationsExceptBase(
                        translations, baseLanguage, key);
                }
                baseEntries[key] = normalizedText;
                updated++;
            }
        }

        localizerSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(localizer);
        if (reassigned > 0) InvalidateSceneKeyReferenceCounts();
        Debug.Log(
            $"[Idiomas] Interaction Text sincronizado: {updated} claves, " +
            $"{reassigned} reasignadas, " +
            $"idioma base \"{baseLanguage}\".");
        return updated + reassigned;
    }

    private static int GetInteractionReferenceCount(
        Dictionary<string, int> referenceCounts, string key)
    {
        if (!referenceCounts.TryGetValue(key, out int count))
        {
            count =
                IdiomasEditorUtils.CountSceneTranslationKeyReferences(key);
            referenceCounts[key] = count;
        }
        return count;
    }

    private static string BuildCanvasNativeKey(
        CanvasLocalizer localizer, RegisteredCanvasEntry entry)
    {
        string canvasId = IdiomasEditorUtils.NormalizeName(
            localizer != null ? localizer.GetCanvasId() : "");
        if (string.IsNullOrEmpty(canvasId)) canvasId = "canvas";
        if (canvasId.StartsWith("canvas_"))
            canvasId = canvasId.Substring(7);

        List<string> names = new List<string>();
        Transform current = entry.component != null
            ? entry.component.transform
            : null;
        Transform root = localizer != null ? localizer.transform : null;
        while (current != null && current != root && names.Count < 2)
        {
            string name = IdiomasEditorUtils.NormalizeName(current.name);
            if (!string.IsNullOrEmpty(name) &&
                name != "text" && name != "label" && name != "tmp" &&
                name != "text_tmp")
            {
                names.Insert(0, name);
            }
            current = current.parent;
        }
        string suffix = names.Count > 0
            ? string.Join("_", names)
            : "text";
        return "canvas_" + canvasId + "_" + suffix;
    }

    private static string BuildInteractionNativeKey(
        RegisteredInteractionEntry entry)
    {
        Component component = entry.target as Component;
        string objectName = IdiomasEditorUtils.NormalizeName(
            component != null ? component.gameObject.name : "object");
        if (string.IsNullOrEmpty(objectName)) objectName = "object";
        if (entry.type == InteractionTextType.PickupInteraction)
            return "pickup_interaction_" + objectName;
        if (entry.type == InteractionTextType.PickupUse)
            return "pickup_use_" + objectName;
        return "interaction_" + objectName;
    }

    private static string GenerateInteractionDetachedKey(
        string originalKey,
        HashSet<string> reservedKeys,
        Dictionary<string, int> referenceCounts)
    {
        string stem = string.IsNullOrWhiteSpace(originalKey)
            ? "interaction"
            : originalKey;
        int suffix = 2;
        string candidate = stem;
        while (reservedKeys.Contains(candidate) ||
            GetInteractionReferenceCount(referenceCounts, candidate) > 0)
        {
            candidate = stem + "_" + suffix;
            suffix++;
        }
        return candidate;
    }

    /// <summary>
    /// Confirma y elimina de todos los idiomas las claves antiguas que ya no
    /// estan referenciadas por ningun localizador de la escena.
    /// </summary>
    private int ConfirmAndRemoveOrphanedKeys(
        Dictionary<string, Dictionary<string, string>> translations,
        HashSet<string> candidates)
    {
        if (translations == null || candidates == null ||
            candidates.Count == 0)
        {
            return 0;
        }

        List<string> orphanedKeys = new List<string>();
        foreach (string key in candidates)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                IdiomasEditorUtils.CountSceneTranslationKeyReferences(key) > 0)
            {
                continue;
            }

            foreach (Dictionary<string, string> language in
                translations.Values)
            {
                if (language.ContainsKey(key))
                {
                    orphanedKeys.Add(key);
                    break;
                }
            }
        }

        if (orphanedKeys.Count == 0) return 0;
        orphanedKeys.Sort(System.StringComparer.Ordinal);
        bool remove = EditorUtility.DisplayDialog(
            S("mgr_orphan_keys_title"),
            string.Format(
                S("mgr_orphan_keys_msg"), orphanedKeys.Count),
            S("mgr_orphan_keys_delete"),
            S("mgr_orphan_keys_keep"));
        if (!remove) return 0;

        for (int i = 0; i < orphanedKeys.Count; i++)
        {
            foreach (Dictionary<string, string> language in
                translations.Values)
            {
                language.Remove(orphanedKeys[i]);
            }
        }
        return orphanedKeys.Count;
    }

    /// <summary>
    /// Restaura al JSON las claves faltantes de un InteractionLocalizer.
    /// </summary>
    private void RestoreInteractionKeysToJson(
        InteractionLocalizer localizer, List<string> allKeys)
    {
        if (localizer == null || allKeys.Count == 0) return;

        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog(
                S("no_file_title"), S("no_file_msg"), S("ok"));
            return;
        }

        string baseLang = localizer.GetBaseLanguage();
        if (string.IsNullOrEmpty(baseLang))
        {
            EditorUtility.DisplayDialog(
                S("no_base_lang_title"), S("no_base_lang_msg"), S("ok"));
            return;
        }

        string fullPath = Path.GetFullPath(AssetDatabase.GetAssetPath(ta));
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

        Dictionary<string, string> keyTextMap =
            BuildInteractionKeyTextMap(localizer);
        int added = AddMissingKeys(
            translations[baseLang], allKeys, keyTextMap);
        if (added == 0)
        {
            EditorUtility.DisplayDialog(
                S("no_changes"), S("no_changes_msg"), S("ok"));
            return;
        }

        File.WriteAllText(
            fullPath,
            IdiomasEditorUtils.WriteDictionaryToJson(translations),
            Encoding.UTF8);
        AssetDatabase.Refresh();
        _cachedJsonHash = null;

        Debug.Log(
            $"[Idiomas] Restauradas {added} claves al JSON " +
            $"(InteractionLocalizer \"{localizer.gameObject.name}\", idioma \"{baseLang}\").");
        EditorUtility.DisplayDialog(
            S("mgr_restore_json_title"),
            string.Format(
                S("mgr_restore_json_msg"),
                added,
                baseLang,
                localizer.gameObject.name),
            S("ok"));
    }

    private static int AddMissingKeys(
        Dictionary<string, string> translations,
        List<string> keys,
        Dictionary<string, string> keyTextMap)
    {
        int added = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            string key = keys[i];
            if (translations.ContainsKey(key) ||
                !keyTextMap.TryGetValue(key, out string text))
            {
                continue;
            }

            translations[key] = text;
            added++;
        }
        return added;
    }

    private static Dictionary<string, string> BuildInteractionKeyTextMap(
        InteractionLocalizer localizer)
    {
        Dictionary<string, string> map = new Dictionary<string, string>();
        SerializedObject localizerSO = new SerializedObject(localizer);

        SerializedProperty targets = localizerSO.FindProperty("interactTargets");
        SerializedProperty keys = localizerSO.FindProperty("interactKeys");
        if (targets != null && keys != null)
        {
            int count = Mathf.Min(targets.arraySize, keys.arraySize);
            for (int i = 0; i < count; i++)
            {
                string key = keys.GetArrayElementAtIndex(i).stringValue;
                UdonSharpBehaviour target =
                    targets.GetArrayElementAtIndex(i).objectReferenceValue
                    as UdonSharpBehaviour;
                UdonBehaviour backing = target != null
                    ? IdiomasEditorUtils.FindUdonBehaviourFor(target)
                    : null;
                if (!string.IsNullOrEmpty(key) && backing != null &&
                    !string.IsNullOrEmpty(backing.InteractionText))
                {
                    map[key] = backing.InteractionText;
                }
            }
        }

        AddPickupTextsToMap(
            localizerSO.FindProperty("pickupInteractionTargets"),
            localizerSO.FindProperty("pickupInteractionKeys"),
            false,
            map);
        AddPickupTextsToMap(
            localizerSO.FindProperty("pickupUseTargets"),
            localizerSO.FindProperty("pickupUseKeys"),
            true,
            map);
        return map;
    }

    private static void AddPickupTextsToMap(
        SerializedProperty targets,
        SerializedProperty keys,
        bool useText,
        Dictionary<string, string> map)
    {
        if (targets == null || keys == null) return;
        int count = Mathf.Min(targets.arraySize, keys.arraySize);
        for (int i = 0; i < count; i++)
        {
            string key = keys.GetArrayElementAtIndex(i).stringValue;
            VRCPickup pickup =
                targets.GetArrayElementAtIndex(i).objectReferenceValue as VRCPickup;
            string text = pickup == null
                ? null
                : (useText ? pickup.UseText : pickup.InteractionText);
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(text))
                map[key] = text;
        }
    }

    // =====================================================================
    // Restaurar JSON Faltantes (global - todos los Localizer)
    // =====================================================================

    /// <summary>
    /// Restaura las claves faltantes de todos los Localizer y sincroniza
    /// los Interaction Text modificados con el idioma base.
    /// </summary>
    private void RestoreAllKeysToJson()
    {
        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog(S("no_file_title"),
                S("no_file_msg"), S("ok"));
            return;
        }

        // Recopilar todos los CanvasLocalizer registrados
        List<CanvasLocalizer> localizers = new List<CanvasLocalizer>();
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            CanvasLocalizer cl = _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue as CanvasLocalizer;
            if (cl != null) localizers.Add(cl);
        }

        List<InteractionLocalizer> interactionLocalizers =
            new List<InteractionLocalizer>();
        for (int i = 0; i < _interactionLocalizers.arraySize; i++)
        {
            InteractionLocalizer localizer =
                _interactionLocalizers.GetArrayElementAtIndex(i)
                    .objectReferenceValue as InteractionLocalizer;
            if (localizer != null) interactionLocalizers.Add(localizer);
        }

        if (localizers.Count == 0 && interactionLocalizers.Count == 0)
        {
            EditorUtility.DisplayDialog(S("no_cl_title"),
                S("no_cl_msg"), S("ok"));
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(ta);
        string fullPath = Path.GetFullPath(assetPath);
        string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);

        var translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
        if (translations == null)
        {
            EditorUtility.DisplayDialog(S("error_title"), S("error_parse_json"), S("ok"));
            return;
        }

        int totalAdded = 0;
        int canvasProcessed = 0;
        int modifiedSourceTexts = 0;
        for (int i = 0; i < localizers.Count; i++)
        {
            totalAdded += CanvasLocalizerEditor.AppendMissingTexts(
                localizers[i],
                localizers[i].GetBaseLanguage(),
                translations);
            modifiedSourceTexts += CountModifiedCanvasEntries(
                localizers[i], translations);
        }
        if (totalAdded > 0) InvalidateSceneKeyReferenceCounts();
        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            modifiedSourceTexts += CountModifiedInteractionEntries(
                interactionLocalizers[i], translations);
        }
        SourceTranslationUpdateMode updateMode =
            ConfirmModifiedSourceTexts(modifiedSourceTexts);
        if (updateMode == SourceTranslationUpdateMode.Cancel) return;
        bool clearModifiedTranslations =
            updateMode == SourceTranslationUpdateMode.ClearTranslations;
        HashSet<string> orphanCandidates = new HashSet<string>();

        for (int c = 0; c < localizers.Count; c++)
        {
            CanvasLocalizer cl = localizers[c];
            int synchronized = SyncCanvasLocalizerToJson(
                cl, translations, clearModifiedTranslations,
                orphanCandidates);
            if (synchronized > 0) canvasProcessed++;
            totalAdded += synchronized;
        }

        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            InteractionLocalizer localizer = interactionLocalizers[i];
            int synchronized = SyncInteractionLocalizerToJson(
                localizer, translations, clearModifiedTranslations,
                orphanCandidates);
            if (synchronized > 0) canvasProcessed++;
            totalAdded += synchronized;
        }

        int removed = ConfirmAndRemoveOrphanedKeys(
            translations, orphanCandidates);
        if (totalAdded == 0 && removed == 0)
        {
            EditorUtility.DisplayDialog(S("no_changes"),
                S("no_changes_msg"), S("ok"));
            return;
        }

        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        _cachedJsonHash = null;
        int appliedTexts = ApplySynchronizedTextsInEditor(
            translations,
            localizers,
            interactionLocalizers);

        int totalLocalizers = localizers.Count + interactionLocalizers.Count;
        Debug.Log(
            $"[Idiomas] Restauracion global: {totalAdded} claves de " +
            $"{canvasProcessed} localizers, " +
            $"{appliedTexts} textos reaplicados.");
        EditorUtility.DisplayDialog(S("mgr_restore_all_title"),
            string.Format(
                S("mgr_restore_all_msg"),
                totalAdded,
                canvasProcessed,
                totalLocalizers,
                appliedTexts),
            S("ok"));
    }

    /// <summary>
    /// Reaplica en el Editor los textos del idioma base sin inicializar el
    /// LocalizationManager ni depender de la API de idioma de VRChat.
    /// </summary>
    private static int ApplySynchronizedTextsInEditor(
        Dictionary<string, Dictionary<string, string>> translations,
        List<CanvasLocalizer> canvasLocalizers,
        List<InteractionLocalizer> interactionLocalizers)
    {
        if (translations == null) return 0;

        int applied = 0;
        for (int i = 0; i < canvasLocalizers.Count; i++)
        {
            CanvasLocalizer localizer = canvasLocalizers[i];
            if (localizer == null ||
                !translations.TryGetValue(
                    localizer.GetBaseLanguage(),
                    out Dictionary<string, string> baseEntries))
            {
                continue;
            }

            List<RegisteredCanvasEntry> entries =
                BuildRegisteredCanvasEntries(
                    new SerializedObject(localizer));
            for (int j = 0; j < entries.Count; j++)
            {
                RegisteredCanvasEntry entry = entries[j];
                string key = entry.key.stringValue;
                if (!baseEntries.TryGetValue(
                    key, out string synchronizedText))
                {
                    continue;
                }

                if (entry.component is TextMeshProUGUI tmp &&
                    !IdiomasEditorUtils.TextEquals(
                        tmp.text, synchronizedText))
                {
                    Undo.RecordObject(tmp, "Apply Synchronized Text");
                    tmp.text = synchronizedText;
                    EditorUtility.SetDirty(tmp);
                    applied++;
                }
                else if (entry.component is Text legacy &&
                    !IdiomasEditorUtils.TextEquals(
                        legacy.text, synchronizedText))
                {
                    Undo.RecordObject(
                        legacy, "Apply Synchronized Text");
                    legacy.text = synchronizedText;
                    EditorUtility.SetDirty(legacy);
                    applied++;
                }
            }
        }

        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            InteractionLocalizer localizer = interactionLocalizers[i];
            if (localizer == null ||
                !translations.TryGetValue(
                    localizer.GetBaseLanguage(),
                    out Dictionary<string, string> baseEntries))
            {
                continue;
            }

            SerializedObject localizerSO =
                new SerializedObject(localizer);
            EnsureInteractionEnabledArrays(localizerSO);
            Dictionary<GameObject, List<RegisteredInteractionEntry>>
                groups = BuildRegisteredInteractionGroups(localizerSO);
            foreach (KeyValuePair<GameObject,
                List<RegisteredInteractionEntry>> group in groups)
            {
                List<RegisteredInteractionEntry> entries = group.Value;
                for (int j = 0; j < entries.Count; j++)
                {
                    RegisteredInteractionEntry entry = entries[j];
                    if (!entry.enabled.boolValue ||
                        !baseEntries.TryGetValue(
                            entry.key.stringValue,
                            out string synchronizedText))
                    {
                        continue;
                    }
                    if (ApplyInteractionTextInEditor(
                        entry, synchronizedText))
                    {
                        applied++;
                    }
                }
            }
            if (localizerSO.ApplyModifiedProperties())
                EditorUtility.SetDirty(localizer);
        }

        if (applied > 0)
            SceneView.RepaintAll();
        return applied;
    }

    private static bool ApplyInteractionTextInEditor(
        RegisteredInteractionEntry entry,
        string synchronizedText)
    {
        if (entry.type == InteractionTextType.UdonInteraction)
        {
            UdonSharpBehaviour behaviour =
                entry.target as UdonSharpBehaviour;
            UdonBehaviour backing = behaviour != null
                ? IdiomasEditorUtils.FindUdonBehaviourFor(behaviour)
                : null;
            if (backing == null ||
                IdiomasEditorUtils.TextEquals(
                    backing.InteractionText, synchronizedText))
            {
                return false;
            }
            Undo.RecordObject(
                backing, "Apply Synchronized Interaction Text");
            backing.InteractionText = synchronizedText;
            EditorUtility.SetDirty(backing);
            return true;
        }

        VRCPickup pickup = entry.target as VRCPickup;
        if (pickup == null) return false;
        if (entry.type == InteractionTextType.PickupUse)
        {
            if (IdiomasEditorUtils.TextEquals(
                pickup.UseText, synchronizedText))
            {
                return false;
            }
            Undo.RecordObject(
                pickup, "Apply Synchronized Interaction Text");
            pickup.UseText = synchronizedText;
        }
        else
        {
            if (IdiomasEditorUtils.TextEquals(
                pickup.InteractionText, synchronizedText))
                return false;
            Undo.RecordObject(
                pickup, "Apply Synchronized Interaction Text");
            pickup.InteractionText = synchronizedText;
        }
        EditorUtility.SetDirty(pickup);
        return true;
    }

    // =====================================================================
    // Quitar Todos los Localizer administrados (global)
    // =====================================================================

    /// <summary>
    /// Elimina todos los CanvasLocalizer e InteractionLocalizer administrados
    /// y limpia sus claves del JSON.
    /// </summary>
    private void RemoveAllLocalizers()
    {
        // Recopilar todos los CanvasLocalizer registrados
        List<CanvasLocalizer> localizers = new List<CanvasLocalizer>();
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            CanvasLocalizer cl = _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue as CanvasLocalizer;
            if (cl != null) localizers.Add(cl);
        }

        List<InteractionLocalizer> interactionLocalizers =
            new List<InteractionLocalizer>();
        for (int i = 0; i < _interactionLocalizers.arraySize; i++)
        {
            InteractionLocalizer localizer =
                _interactionLocalizers.GetArrayElementAtIndex(i)
                    .objectReferenceValue as InteractionLocalizer;
            if (localizer != null) interactionLocalizers.Add(localizer);
        }

        if (localizers.Count == 0 && interactionLocalizers.Count == 0)
        {
            EditorUtility.DisplayDialog(S("no_cl_title"),
                S("no_cl_msg"), S("ok"));
            return;
        }

        // Contar claves totales en JSON para el dialogo
        int totalKeysInJson = 0;
        List<List<string>> allKeyLists = new List<List<string>>();
        for (int i = 0; i < localizers.Count; i++)
        {
            List<string> keys = GetCanvasLocalizerKeys(localizers[i]);
            allKeyLists.Add(keys);
            totalKeysInJson += CountKeysInJson(keys);
        }
        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            List<string> keys =
                GetAllInteractionLocalizerKeys(interactionLocalizers[i]);
            allKeyLists.Add(keys);
            totalKeysInJson += CountKeysInJson(keys);
        }
        int interactionTextCount = 0;
        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            interactionTextCount += GetConfiguredInteractionTextCount(
                interactionLocalizers[i]);
        }

        string msg = string.Format(
            S("mgr_remove_all_title_msg"),
            localizers.Count,
            interactionTextCount);
        if (totalKeysInJson > 0)
            msg += string.Format(S("mgr_remove_all_msg_keys"), totalKeysInJson);
        msg += S("mgr_remove_all_footer");

        if (!EditorUtility.DisplayDialog(S("mgr_remove_all_title"), msg, S("mgr_remove_all"), S("cancel")))
            return;

        // Fase 1: Limpiar claves del JSON
        if (totalKeysInJson > 0)
        {
            TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
            if (ta != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(ta);
                string fullPath = Path.GetFullPath(assetPath);
                string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);

                var translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
                if (translations != null)
                {
                    // Reunir todas las claves a eliminar
                    HashSet<string> keysToRemove = new HashSet<string>();
                    for (int i = 0; i < allKeyLists.Count; i++)
                    {
                        for (int j = 0; j < allKeyLists[i].Count; j++)
                            keysToRemove.Add(allKeyLists[i][j]);
                    }

                    // Eliminar de todos los idiomas
                    foreach (var langPair in translations)
                    {
                        List<string> toRemove = new List<string>();
                        foreach (string key in langPair.Value.Keys)
                        {
                            if (keysToRemove.Contains(key))
                                toRemove.Add(key);
                        }
                        for (int i = 0; i < toRemove.Count; i++)
                            langPair.Value.Remove(toRemove[i]);
                    }

                    string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
                    File.WriteAllText(fullPath, newJson, Encoding.UTF8);
                    AssetDatabase.Refresh();
                    _cachedJsonHash = null;
                }
            }
        }

        // Fase 2: Eliminar componentes CanvasLocalizer y UdonBehaviour
        int removed = 0;
        for (int i = 0; i < localizers.Count; i++)
        {
            CanvasLocalizer cl = localizers[i];
            if (cl == null) continue;

            GameObject go = cl.gameObject;
            UdonBehaviour udon = IdiomasEditorUtils.FindUdonBehaviourFor(cl);
            if (udon != null) Undo.DestroyObjectImmediate(udon);
            Undo.DestroyObjectImmediate(cl);

            EditorUtility.SetDirty(go);
            removed++;
        }
        for (int i = 0; i < interactionLocalizers.Count; i++)
        {
            InteractionLocalizer localizer = interactionLocalizers[i];
            if (localizer == null) continue;
            RemoveInteractionLocalizerWithoutDialog(localizer);
        }

        // Fase 3: Limpiar los arrays administrados del manager
        _canvasLocalizers.ClearArray();
        serializedObject.ApplyModifiedProperties();

        // Reconstruir ambos resultados. La lista de Interaction queda vacia
        // despues de una configuracion rapida y no debe reutilizarse.
        ScanSceneForCanvas();
        if (_includeInteractionTexts.boolValue &&
            GetPrefabInteractionLocalizer(
                target as LocalizationManager) != null)
        {
            ScanInteractionTexts();
        }
        else
        {
            _interactionSearchResults = null;
            _interactionDetectedObjectCount = 0;
            _interactionRegisteredObjectCount = 0;
        }
        RefreshSceneKeyReferenceCounts();
        RefreshScanResultSummary();

        Debug.Log(
            $"[Idiomas] Eliminados {removed} CanvasLocalizer, limpiados " +
            $"{interactionTextCount} Interaction Text y " +
            $"{totalKeysInJson} claves del JSON.");
        EditorUtility.DisplayDialog(S("mgr_remove_all_done_title"),
            string.Format(
                S("mgr_remove_all_done_msg"),
                removed,
                interactionTextCount,
                totalKeysInJson),
            S("ok"));
    }

    // =====================================================================
    // Configuracion Rapida: Localizar Todo
    // =====================================================================

    /// <summary>
    /// Obtiene una lista unica de palabras clave.
    /// Ignora elementos vacios y espacios al principio o al final.
    /// </summary>
    private string[] ParseQuickSetupExcludedKeywords()
    {
        List<string> result = new List<string>();
        HashSet<string> unique = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);

        if (_excludedLocalizationKeywords == null) return result.ToArray();
        for (int i = 0; i < _excludedLocalizationKeywords.arraySize; i++)
        {
            string keyword = _excludedLocalizationKeywords
                .GetArrayElementAtIndex(i).stringValue;
            if (keyword == null) continue;
            keyword = keyword.Trim();
            if (string.IsNullOrEmpty(keyword)) continue;
            if (unique.Add(keyword)) result.Add(keyword);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Copia las palabras clave comunes al CanvasLocalizer indicado.
    /// </summary>
    private void ApplyQuickSetupExcludedKeywords(SerializedObject canvasLocalizerObject)
    {
        if (canvasLocalizerObject == null) return;

        SerializedProperty keywordsProp =
            canvasLocalizerObject.FindProperty("excludedKeywords");
        if (keywordsProp == null) return;

        string[] keywords = ParseQuickSetupExcludedKeywords();
        keywordsProp.arraySize = keywords.Length;

        for (int i = 0; i < keywords.Length; i++)
            keywordsProp.GetArrayElementAtIndex(i).stringValue = keywords[i];
    }

    private void QuickSetupAll(
        int candidateCount,
        int candidateTextCount,
        int interactionObjectCount,
        int interactionTextCount)
    {
        string baseLang = _quickSetupBaseLanguage;
        string langName = IdiomasLanguages.GetNativeName(baseLang);

        if (!EditorUtility.DisplayDialog(
            S("mgr_quick_setup_title"),
            string.Format(
                S("mgr_quick_setup_combined_confirm"),
                candidateCount,
                interactionTextCount,
                candidateTextCount + interactionTextCount,
                langName,
                baseLang),
            S("mgr_process_all"), S("cancel")))
        {
            return;
        }

        LocalizationManager mgr = (LocalizationManager)target;
        // ============================================================
        // Preparar archivo JSON (leer o crear)
        // ============================================================
        TextAsset textAsset = _translationFile.objectReferenceValue as TextAsset;
        string assetPath;
        string fullPath;
        Dictionary<string, Dictionary<string, string>> translations;

        if (textAsset != null)
        {
            assetPath = AssetDatabase.GetAssetPath(textAsset);
            fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath))
            {
                string json = File.ReadAllText(fullPath, Encoding.UTF8);
                translations = IdiomasEditorUtils.ParseJsonToDictionary(json);
                if (translations == null)
                    translations = new Dictionary<string, Dictionary<string, string>>();
            }
            else
            {
                translations = new Dictionary<string, Dictionary<string, string>>();
            }
        }
        else
        {
            assetPath = "Assets/Idiomas_Data/translation.json";
            fullPath = Path.GetFullPath(assetPath);
            translations = new Dictionary<string, Dictionary<string, string>>();
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        if (!translations.ContainsKey(baseLang))
            translations[baseLang] = new Dictionary<string, string>();

        // ============================================================
        // Fase 1: Anadir CanvasLocalizer a TODOS los candidatos
        // (antes de escanear, para que IsUnderAny funcione correctamente
        //  cuando un canvas padre y su hijo son ambos candidatos)
        // ============================================================
        List<CanvasSearchResult> toProcess = new List<CanvasSearchResult>();
        List<CanvasLocalizer> newLocalizers = new List<CanvasLocalizer>();
        List<string> errors = new List<string>();

        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult r = _canvasSearchResults[i];
            if (r.gameObject == null) continue;
            if (IsExcludedByGameObject(r.gameObject.transform)) continue;
            if (r.hasCanvasLocalizer) continue;

            int currentTmpCount;
            int currentLegacyCount;
            if (CountTranslatableTexts(r.gameObject,
                out currentTmpCount, out currentLegacyCount) == 0) continue;

            CanvasLocalizer newCL = UdonSharpUndo.AddComponent<CanvasLocalizer>(r.gameObject);
            if (newCL == null)
            {
                errors.Add($"No se pudo anadir CanvasLocalizer a '{r.gameObject.name}'");
                continue;
            }

            // Configurar: manager + canvasId
            SerializedObject clSO = new SerializedObject(newCL);
            SerializedProperty mgrProp = clSO.FindProperty("manager");
            SerializedProperty idProp = clSO.FindProperty("canvasId");

            if (mgrProp != null) mgrProp.objectReferenceValue = mgr;
            string uniqueId = IdiomasEditorUtils.GenerateUniqueCanvasId(r.gameObject.name, newCL);
            if (idProp != null) idProp.stringValue = uniqueId;
            ApplyQuickSetupExcludedKeywords(clSO);
            clSO.ApplyModifiedProperties();

            toProcess.Add(r);
            newLocalizers.Add(newCL);
        }

        // ============================================================
        // Fase 2: Escanear y aplicar cada canvas
        // (ahora todos los CanvasLocalizer existen, IsUnderAny funciona)
        // ============================================================
        int totalTexts = 0;
        int processedCanvas = 0;

        // Incorporar textos agregados a CanvasLocalizer ya configurados.
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            CanvasSearchResult result = _canvasSearchResults[i];
            if (!result.hasCanvasLocalizer ||
                result.missingTextCount == 0 ||
                result.gameObject == null)
            {
                continue;
            }
            CanvasLocalizer existing =
                result.gameObject.GetComponent<CanvasLocalizer>();
            int appended = CanvasLocalizerEditor.AppendMissingTexts(
                existing, baseLang, translations);
            if (appended > 0)
            {
                InvalidateSceneKeyReferenceCounts();
                totalTexts += appended;
                processedCanvas++;
                result.missingTextCount = 0;
            }
        }

        for (int i = 0; i < toProcess.Count; i++)
        {
            CanvasSearchResult r = toProcess[i];
            CanvasLocalizer cl = newLocalizers[i];

            int textsConfigured = CanvasLocalizerEditor.QuickSetup(cl, baseLang, translations);

            if (textsConfigured < 0)
            {
                errors.Add($"Error al escanear '{r.gameObject.name}'");
            }
            else if (textsConfigured == 0)
            {
                // No conservar un CanvasLocalizer si no hay textos traducibles
                UdonBehaviour backing = IdiomasEditorUtils.FindUdonBehaviourFor(cl);
                if (backing != null) Undo.DestroyObjectImmediate(backing);
                Undo.DestroyObjectImmediate(cl);
                newLocalizers[i] = null;
            }
            else
            {
                totalTexts += textsConfigured;
                processedCanvas++;
                r.hasCanvasLocalizer = true;
            }

            EditorUtility.SetDirty(r.gameObject);
        }

        // Configurar InteractionLocalizer y registrar sus textos en el mismo JSON.
        int interactionTextsConfigured = 0;
        int interactionLocalizersConfigured = 0;
        if (_includeInteractionTexts.boolValue &&
            _interactionSearchResults != null &&
            interactionTextCount > 0)
        {
            interactionTextsConfigured = ConfigureInteractionLocalizers(
                mgr,
                baseLang,
                translations,
                out interactionLocalizersConfigured);
            totalTexts += interactionTextsConfigured;
            if (interactionTextsConfigured > 0)
            {
                // Quitar solo los resultados procesados. Los que el usuario
                // dejo fuera permanecen visibles para poder revisarlos.
                _interactionSearchResults.RemoveAll(result => result.include);
                RefreshScanResultSummary();
            }
        }

        // ============================================================
        // Fase 3: Escribir JSON una sola vez (eficiente)
        // ============================================================
        if (totalTexts > 0)
        {
            string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
            File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        }

        AssetDatabase.Refresh();

        // Asignar JSON al manager si no tenia uno
        if (textAsset == null && totalTexts > 0)
        {
            TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (newAsset != null)
            {
                _translationFile.objectReferenceValue = newAsset;
                serializedObject.ApplyModifiedProperties();
            }
        }

        // ============================================================
        // Fase 4: Registrar todos los CanvasLocalizer con el manager
        // ============================================================
        SerializedObject mgrSO = new SerializedObject(mgr);
        SerializedProperty clArray = mgrSO.FindProperty("canvasLocalizers");

        // Construir set de ya registrados para busqueda rapida
        HashSet<Object> registered = new HashSet<Object>();
        for (int i = 0; i < clArray.arraySize; i++)
        {
            Object obj = clArray.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) registered.Add(obj);
        }

        // Agregar los nuevos
        for (int i = 0; i < newLocalizers.Count; i++)
        {
            if (newLocalizers[i] != null && !registered.Contains(newLocalizers[i]))
            {
                int idx = clArray.arraySize;
                clArray.arraySize = idx + 1;
                clArray.GetArrayElementAtIndex(idx).objectReferenceValue = newLocalizers[i];
            }
        }
        mgrSO.ApplyModifiedProperties();

        // Invalidar cache del JSON para que las estadisticas se actualicen
        _cachedJsonHash = null;

        // ============================================================
        // Mostrar resumen
        // ============================================================
        StringBuilder summary = new StringBuilder();
        summary.AppendLine(string.Format(S("mgr_quick_setup_summary_canvas"), processedCanvas, candidateCount));
        summary.AppendLine(string.Format(
            S("mgr_quick_setup_summary_interaction"),
            interactionLocalizersConfigured,
            interactionObjectCount,
            interactionTextsConfigured));
        summary.AppendLine(string.Format(S("mgr_quick_setup_summary_texts"), totalTexts));
        summary.AppendLine(string.Format(S("mgr_quick_setup_summary_lang"), langName, baseLang));
        summary.AppendLine(string.Format(S("mgr_quick_setup_summary_file"), assetPath));

        if (errors.Count > 0)
        {
            summary.AppendLine(string.Format(S("mgr_quick_setup_summary_errors"), errors.Count));
            for (int i = 0; i < errors.Count; i++)
                summary.AppendLine($"  - {errors[i]}");
        }

        summary.AppendLine(S("mgr_quick_setup_summary_footer"));

        EditorUtility.DisplayDialog(
            S("mgr_quick_setup_done_title"),
            summary.ToString(), S("ok"));

        Debug.Log($"[Idiomas] Configuracion Rapida: {processedCanvas} canvas, " +
            $"{interactionLocalizersConfigured} InteractionLocalizer, " +
            $"{totalTexts} textos ({interactionTextsConfigured} Interaction Text). " +
            $"Idioma base: {baseLang}");
    }

    private void RefreshCache()
    {
        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            if (_hasTranslationFileStamp || _cachedData != null)
                ResetTranslationState();
            _translationFileStateDirty = false;
            return;
        }

        // Comprobar identidad y estado del archivo solo cuando Unity informa cambios.
        string assetPath = AssetDatabase.GetAssetPath(ta);
        if (!_translationFileStateDirty &&
            _hasTranslationFileStamp &&
            _translationFileStamp.assetPath == assetPath &&
            _translationFileStamp.instanceId == ta.GetInstanceID() &&
            _cachedJsonHash != null)
        {
            return;
        }

        string fullPath = string.IsNullOrEmpty(assetPath)
            ? ""
            : Path.GetFullPath(assetPath);
        FileInfo fileInfo = !string.IsNullOrEmpty(fullPath) &&
            File.Exists(fullPath)
            ? new FileInfo(fullPath)
            : null;
        TranslationFileStamp currentStamp = new TranslationFileStamp
        {
            assetPath = assetPath,
            guid = string.IsNullOrEmpty(assetPath)
                ? ""
                : AssetDatabase.AssetPathToGUID(assetPath),
            dependencyHash = string.IsNullOrEmpty(assetPath)
                ? default(Hash128)
                : AssetDatabase.GetAssetDependencyHash(assetPath),
            creationTimeTicks = fileInfo != null
                ? fileInfo.CreationTimeUtc.Ticks
                : 0,
            lastWriteTimeTicks = fileInfo != null
                ? fileInfo.LastWriteTimeUtc.Ticks
                : 0,
            fileLength = fileInfo != null ? fileInfo.Length : 0,
            instanceId = ta.GetInstanceID()
        };
        bool identityChanged = _hasTranslationFileStamp &&
            (_translationFileStamp.assetPath != currentStamp.assetPath ||
             _translationFileStamp.guid != currentStamp.guid ||
             _translationFileStamp.creationTimeTicks !=
                currentStamp.creationTimeTicks ||
             _translationFileStamp.instanceId != currentStamp.instanceId);
        bool contentChanged = !_hasTranslationFileStamp ||
            _translationFileStamp.dependencyHash !=
                currentStamp.dependencyHash ||
            _translationFileStamp.lastWriteTimeTicks !=
                currentStamp.lastWriteTimeTicks ||
            _translationFileStamp.fileLength != currentStamp.fileLength ||
            _cachedJsonHash == null;
        if (identityChanged)
        {
            _canvasSearchResults = null;
            _interactionSearchResults = null;
            _interactionDetectedObjectCount = 0;
            _interactionRegisteredObjectCount = 0;
            _scanResultSummary = null;
            _interactionPendingRows = null;
            _interactionExcludedRows = null;
            _interactionRegisteredRows = null;
            InvalidateSceneKeyReferenceCounts();
        }
        _translationFileStamp = currentStamp;
        _hasTranslationFileStamp = true;
        bool shouldRefresh = contentChanged;
        _translationFileStateDirty = false;
        if (!shouldRefresh) return;

        ClearTranslationJsonCache();
        string text = ta.text;
        _cachedJsonHash =
            currentStamp.dependencyHash + "_" +
            currentStamp.lastWriteTimeTicks + "_" +
            currentStamp.fileLength;
        _cachedTranslations =
            IdiomasEditorUtils.ParseJsonToDictionary(text);

        if (VRCJson.TryDeserializeFromJson(ta.text, out DataToken d) &&
            d.TokenType == TokenType.DataDictionary)
        {
            _cachedData = d.DataDictionary;
            DataList k = _cachedData.GetKeys();
            _cachedLanguages = new string[k.Count];
            for (int i = 0; i < k.Count; i++) _cachedLanguages[i] = k[i].String;

            HashSet<string> all = new HashSet<string>();
            for (int i = 0; i < _cachedLanguages.Length; i++)
            {
                if (_cachedData.TryGetValue(_cachedLanguages[i], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary)
                {
                    DataList lk = lt.DataDictionary.GetKeys();
                    for (int j = 0; j < lk.Count; j++) all.Add(lk[j].String);
                }
            }
            _cachedKeys = new string[all.Count];
            all.CopyTo(_cachedKeys);
        }
        else
        {
            _cachedData = null;
            _cachedLanguages = null;
            _cachedKeys = null;
            _cachedTranslations = null;
        }
        if (_canvasSearchResults != null)
            RefreshInteractionRowSummaries();
    }

    // =====================================================================
    // Crear archivo JSON de traducciones
    // =====================================================================

    /// <summary>
    /// Ruta base donde se crean los archivos JSON de traducciones.
    /// Se usa Assets/ (no Packages/) porque los paquetes VPM son de solo lectura.
    /// </summary>
    private const string JSON_BASE_DIR = "Assets/Idiomas_Data";
    private const string JSON_BASE_NAME = "translation";

    private void CreateTranslationJsonFile()
    {
        // Asegurar que el directorio existe
        string fullDir = Path.GetFullPath(JSON_BASE_DIR);
        if (!Directory.Exists(fullDir))
        {
            Directory.CreateDirectory(fullDir);
        }

        // Buscar nombre disponible: translation.json, translation_2.json, translation_3.json...
        string assetPath = JSON_BASE_DIR + "/" + JSON_BASE_NAME + ".json";
        string fullPath = Path.GetFullPath(assetPath);

        if (File.Exists(fullPath))
        {
            int counter = 2;
            while (true)
            {
                assetPath = JSON_BASE_DIR + "/" + JSON_BASE_NAME + "_" + counter + ".json";
                fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) break;
                counter++;
            }
        }

        // Escribir JSON vacio
        File.WriteAllText(fullPath, "{}\n", Encoding.UTF8);
        AssetDatabase.Refresh();

        // Cargar el asset recien creado y asignarlo al campo
        TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        if (newAsset != null)
        {
            _translationFile.objectReferenceValue = newAsset;
            serializedObject.ApplyModifiedProperties();

            // El archivo puede reutilizar la misma ruta y el mismo nombre.
            ResetTranslationState();

            Debug.Log($"[Idiomas] Archivo de traducciones creado: {assetPath}");
            EditorUtility.DisplayDialog(S("mgr_json_created_title"),
                string.Format(S("mgr_json_created_msg"), assetPath),
                S("ok"));
        }
        else
        {
            Debug.LogError($"[Idiomas] No se pudo cargar el archivo creado: {assetPath}");
        }
    }
}
