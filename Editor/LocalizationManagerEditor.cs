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
    private SerializedProperty _languageDropdown;
    private SerializedProperty _dropdownLanguageCodes;
    private SerializedProperty _listeners;

    // Estado del editor (foldouts)
    private bool _showCanvasSearch = true;
    private bool _showExclusionConditions = true;
    private bool _showTools = false;
    private bool _showListeners = false;
    private bool _showDropdown = false;
    private bool _showPreview = false;
    private string _previewLanguage = "en";
    private static List<CanvasSearchResult> _canvasSearchResults;
    private int _quickSetupLangIndex = 0; // Default: "en" (indice en IdiomasLanguages.Codes)
    private string _quickSetupExcludedKeywordsText = "";
    private const string QUICK_SETUP_KEYWORDS_SESSION_KEY =
        "Idiomas.QuickSetupExcludedKeywords";

    // Cache del JSON
    private DataDictionary _cachedData;
    private string _cachedJsonHash;
    private string[] _cachedLanguages;
    private string[] _cachedKeys;

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
        _languageDropdown = serializedObject.FindProperty("_languageDropdown");
        _dropdownLanguageCodes = serializedObject.FindProperty("_dropdownLanguageCodes");
        _listeners = serializedObject.FindProperty("_listeners");

        // Restaurar las palabras clave usadas durante esta sesion del Editor
        _quickSetupExcludedKeywordsText = SessionState.GetString(
            QUICK_SETUP_KEYWORDS_SESSION_KEY, "");

        // Limpiar resultados de escaneo anteriores para evitar
        // MissingReferenceException por GameObjects destruidos
        // (la lista es static y puede sobrevivir entre recargas)
        _canvasSearchResults = null;
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
        EditorGUILayout.PropertyField(_translationFile,
            new GUIContent(S("mgr_translation_file")));

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

        // === BUSCAR CANVAS SIN LOCALIZAR ===
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
            S("mgr_quick_setup_excluded_keywords"), EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField(
            S("mgr_quick_setup_excluded_keywords_desc"), EditorStyles.wordWrappedMiniLabel);

        string newKeywordsText = EditorGUILayout.TextArea(
            _quickSetupExcludedKeywordsText, GUILayout.MinHeight(60));
        if (newKeywordsText != _quickSetupExcludedKeywordsText)
        {
            _quickSetupExcludedKeywordsText = newKeywordsText;
            SessionState.SetString(
                QUICK_SETUP_KEYWORDS_SESSION_KEY,
                _quickSetupExcludedKeywordsText);
            _canvasSearchResults = null;
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
        public bool hasCanvasLocalizer;
        public string hierarchyPath;
        public string parentCanvasLocalizerName;
    }

    private void ScanSceneForCanvas()
    {
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
                hasCanvasLocalizer = hasCL,
                hierarchyPath = path,
                parentCanvasLocalizerName = parentCLName
            });
        }

        // Filtrar GameObjects destruidos antes de ordenar
        _canvasSearchResults.RemoveAll(r => r.gameObject == null);

        // Ordenar: primero los que NO tienen CanvasLocalizer, luego los que si
        _canvasSearchResults.Sort((a, b) =>
        {
            if (a.hasCanvasLocalizer != b.hasCanvasLocalizer)
                return a.hasCanvasLocalizer ? 1 : -1;
            return string.Compare(a.gameObject.name, b.gameObject.name);
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
        }
        GUI.backgroundColor = Color.white;

        // Filtrar elementos con GameObjects destruidos (evita MissingReferenceException)
        if (_canvasSearchResults != null)
        {
            _canvasSearchResults.RemoveAll(r => r.gameObject == null);
        }

        // === CONFIGURACION RAPIDA ===
        // Solo mostrar si hay resultados de escaneo con candidatos
        if (_canvasSearchResults != null)
        {
            int qsCandidateCount = 0;
            int qsCandidateTextCount = 0;
            for (int i = 0; i < _canvasSearchResults.Count; i++)
            {
                if (!_canvasSearchResults[i].hasCanvasLocalizer &&
                    _canvasSearchResults[i].tmpCount + _canvasSearchResults[i].legacyCount > 0)
                {
                    qsCandidateCount++;
                    qsCandidateTextCount += _canvasSearchResults[i].tmpCount
                        + _canvasSearchResults[i].legacyCount;
                }
            }

            if (qsCandidateCount > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField(
                    S("mgr_quick_setup"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    string.Format(S("mgr_quick_setup_desc"), qsCandidateCount, qsCandidateTextCount),
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(3);
                _quickSetupLangIndex = EditorGUILayout.Popup(
                    new GUIContent(S("mgr_quick_setup_lang"), S("mgr_quick_setup_lang_tooltip")),
                    _quickSetupLangIndex, IdiomasLanguages.PopupLabelsLatin);

                EditorGUILayout.Space(3);
                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.5f);
                if (GUILayout.Button(
                    string.Format(S("mgr_quick_setup_btn"), qsCandidateCount),
                    GUILayout.Height(28)))
                {
                    QuickSetupAll(qsCandidateCount, qsCandidateTextCount);
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndVertical();
            }
        }

        if (_canvasSearchResults == null)
        {
            EditorGUILayout.HelpBox(S("mgr_scan_hint"), MessageType.Info);
            return;
        }

        if (_canvasSearchResults.Count == 0)
        {
            EditorGUILayout.HelpBox(S("mgr_no_canvas"), MessageType.Info);
            return;
        }

        // Contar candidatos vs ya localizados
        int candidates = 0;
        int alreadyLocalized = 0;
        int noTexts = 0;
        for (int i = 0; i < _canvasSearchResults.Count; i++)
        {
            if (_canvasSearchResults[i].hasCanvasLocalizer) alreadyLocalized++;
            else if (_canvasSearchResults[i].tmpCount + _canvasSearchResults[i].legacyCount == 0) noTexts++;
            else candidates++;
        }

        EditorGUILayout.LabelField(
            string.Format(S("mgr_found_summary"), _canvasSearchResults.Count, candidates, alreadyLocalized, noTexts),
            EditorStyles.helpBox);

        EditorGUILayout.Space(3);

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
            if (GUILayout.Button(r.gameObject.name, linkStyle, GUILayout.ExpandWidth(false)))
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

            // Path de jerarquia (en gris, mas pequeno)
            GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel);
            pathStyle.normal.textColor = Color.gray;
            EditorGUILayout.LabelField(r.hierarchyPath, pathStyle);

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

                // Titulo + botones globales
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(S("mgr_already_localized"), EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();

                // Boton global: Restaurar JSON Faltantes
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
                if (GUILayout.Button(new GUIContent(S("mgr_restore_all_json"),
                    S("mgr_restore_all_json_tooltip")),
                    GUILayout.Width(160)))
                {
                    RestoreAllKeysToJson();
                }
                GUI.backgroundColor = Color.white;

                // Boton global: Quitar Todos los CanvasLocalizer
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button(new GUIContent(S("mgr_remove_all"),
                    S("mgr_remove_all_tooltip")),
                    GUILayout.Width(100)))
                {
                    RemoveAllCanvasLocalizers();
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();

                hasLocalized = true;
            }

            if (r.gameObject == null) continue;
            CanvasLocalizer cl = r.gameObject.GetComponent<CanvasLocalizer>();
            string clId = cl != null ? cl.GetCanvasId() : "";

            // Obtener claves del componente y contar cuantas estan en el JSON
            List<string> clKeys = GetCanvasLocalizerKeys(cl);
            int keysInJson = CountKeysInJson(clKeys);
            int totalKeys = clKeys.Count;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Linea 1: nombre + info + estado JSON
            EditorGUILayout.BeginHorizontal();

            GUIStyle greenStyle = new GUIStyle(EditorStyles.label);
            greenStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
            if (GUILayout.Button(r.gameObject.name, greenStyle, GUILayout.ExpandWidth(false)))
            {
                EditorGUIUtility.PingObject(r.gameObject);
            }

            EditorGUILayout.LabelField(
                $"canvasId: \"{clId}\"  |  {(cl != null ? cl.GetTextCount() : 0)} textos",
                EditorStyles.miniLabel);

            // Indicador de estado en el JSON
            GUIStyle jsonStatusStyle = new GUIStyle(EditorStyles.miniLabel);
            jsonStatusStyle.fontStyle = FontStyle.Bold;
            string jsonStatusText;
            if (totalKeys == 0)
            {
                jsonStatusStyle.normal.textColor = new Color(0.8f, 0.5f, 0.0f);
                jsonStatusText = S("mgr_no_keys");
            }
            else if (keysInJson == 0)
            {
                jsonStatusStyle.normal.textColor = new Color(0.9f, 0.3f, 0.0f);
                jsonStatusText = string.Format(S("mgr_json_not_exported"), totalKeys);
            }
            else if (keysInJson < totalKeys)
            {
                jsonStatusStyle.normal.textColor = new Color(0.8f, 0.7f, 0.0f);
                jsonStatusText = string.Format(S("mgr_json_partial"), keysInJson, totalKeys);
            }
            else
            {
                jsonStatusStyle.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
                jsonStatusText = string.Format(S("mgr_json_complete"), keysInJson, totalKeys);
            }
            EditorGUILayout.LabelField(jsonStatusText, jsonStatusStyle, GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();

            // Linea 2: botones alineados a la derecha
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // Boton para restaurar claves faltantes al JSON
            int missingKeys = totalKeys - keysInJson;
            EditorGUI.BeginDisabledGroup(missingKeys == 0);
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
            if (GUILayout.Button(string.Format(S("mgr_restore_json"), missingKeys), GUILayout.Width(140)))
            {
                RestoreKeysToJson(cl, clKeys);
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // Boton para limpiar claves del JSON
            EditorGUI.BeginDisabledGroup(keysInJson == 0);
            GUI.backgroundColor = new Color(1f, 0.7f, 0.3f);
            if (GUILayout.Button(string.Format(S("mgr_clean_json"), keysInJson), GUILayout.Width(130)))
            {
                RemoveKeysFromJson(cl, clKeys, keysInJson);
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

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
            EditorGUILayout.LabelField($"  {r.gameObject.name}", grayStyle);
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
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            CanvasLocalizer other = _canvasLocalizers
                .GetArrayElementAtIndex(i).objectReferenceValue as CanvasLocalizer;
            if (other == null || other == excludedLocalizer) continue;

            List<string> otherKeys = GetCanvasLocalizerKeys(other);
            if (otherKeys.Contains(key)) return true;
        }

        for (int i = 0; i < _localizers.arraySize; i++)
        {
            TextLocalizer textLocalizer = _localizers
                .GetArrayElementAtIndex(i).objectReferenceValue as TextLocalizer;
            if (textLocalizer != null && textLocalizer.GetTranslationKey() == key)
                return true;
        }

        return false;
    }

    private void RemoveKeysFromJson(CanvasLocalizer cl, List<string> keys, int keysInJson)
    {
        if (keys.Count == 0 || keysInJson == 0) return;

        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null)
        {
            EditorUtility.DisplayDialog(S("no_file_title"),
                S("no_file_msg"), S("ok"));
            return;
        }

        string canvasId = cl.GetCanvasId();
        if (!EditorUtility.DisplayDialog(S("mgr_clean_json_title"),
            string.Format(S("mgr_clean_json_msg"), keysInJson, canvasId),
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
            if (!IsTranslationKeyUsedElsewhere(keys[i], cl))
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

        Debug.Log($"[Idiomas] Eliminadas {totalRemoved} entradas del JSON (canvas \"{canvasId}\").");
        EditorUtility.DisplayDialog(S("clean_done_title"),
            string.Format(S("clean_done_msg"), totalRemoved, keysInJson, totalRemoved / Mathf.Max(keysInJson, 1)),
            S("ok"));
    }

    // =====================================================================
    // Restaurar claves faltantes al JSON
    // =====================================================================

    /// <summary>
    /// Restaura al JSON las claves de un CanvasLocalizer que faltan.
    /// Lee el texto actual de los componentes referenciados y lo escribe
    /// bajo el baseLanguage del canvas. No sobreescribe claves existentes.
    /// </summary>
    private void RestoreKeysToJson(CanvasLocalizer cl, List<string> allKeys)
    {
        if (cl == null || allKeys.Count == 0) return;

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

        if (!translations.ContainsKey(baseLang))
            translations[baseLang] = new Dictionary<string, string>();

        // Construir mapa clave -> texto actual desde los arrays del CanvasLocalizer
        Dictionary<string, string> keyTextMap = BuildKeyTextMap(cl);

        // Agregar solo las claves que faltan
        int added = 0;
        for (int i = 0; i < allKeys.Count; i++)
        {
            string key = allKeys[i];
            if (translations[baseLang].ContainsKey(key)) continue;
            if (!keyTextMap.ContainsKey(key)) continue;

            translations[baseLang][key] = keyTextMap[key];
            added++;
        }

        if (added == 0)
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

        string canvasId = cl.GetCanvasId();
        Debug.Log($"[Idiomas] Restauradas {added} claves al JSON (canvas \"{canvasId}\", idioma \"{baseLang}\").");
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

    // =====================================================================
    // Restaurar JSON Faltantes (global - todos los CanvasLocalizer)
    // =====================================================================

    /// <summary>
    /// Restaura al JSON las claves faltantes de TODOS los CanvasLocalizer de la escena.
    /// Solo agrega claves que no existan; no sobreescribe las existentes.
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

        if (localizers.Count == 0)
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

        for (int c = 0; c < localizers.Count; c++)
        {
            CanvasLocalizer cl = localizers[c];
            string baseLang = cl.GetBaseLanguage();
            if (string.IsNullOrEmpty(baseLang)) continue;

            if (!translations.ContainsKey(baseLang))
                translations[baseLang] = new Dictionary<string, string>();

            List<string> clKeys = GetCanvasLocalizerKeys(cl);
            Dictionary<string, string> keyTextMap = BuildKeyTextMap(cl);

            int added = 0;
            for (int i = 0; i < clKeys.Count; i++)
            {
                string key = clKeys[i];
                if (translations[baseLang].ContainsKey(key)) continue;
                if (!keyTextMap.ContainsKey(key)) continue;

                translations[baseLang][key] = keyTextMap[key];
                added++;
            }

            if (added > 0) canvasProcessed++;
            totalAdded += added;
        }

        if (totalAdded == 0)
        {
            EditorUtility.DisplayDialog(S("no_changes"),
                S("no_changes_msg"), S("ok"));
            return;
        }

        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        _cachedJsonHash = null;

        Debug.Log($"[Idiomas] Restauracion global: {totalAdded} claves de {canvasProcessed} canvas.");
        EditorUtility.DisplayDialog(S("mgr_restore_all_title"),
            string.Format(S("mgr_restore_all_msg"), totalAdded, canvasProcessed, localizers.Count),
            S("ok"));
    }

    // =====================================================================
    // Quitar Todos los CanvasLocalizer (global)
    // =====================================================================

    /// <summary>
    /// Elimina el componente CanvasLocalizer de TODOS los canvas de la escena
    /// y limpia sus claves del JSON.
    /// </summary>
    private void RemoveAllCanvasLocalizers()
    {
        // Recopilar todos los CanvasLocalizer registrados
        List<CanvasLocalizer> localizers = new List<CanvasLocalizer>();
        for (int i = 0; i < _canvasLocalizers.arraySize; i++)
        {
            CanvasLocalizer cl = _canvasLocalizers.GetArrayElementAtIndex(i).objectReferenceValue as CanvasLocalizer;
            if (cl != null) localizers.Add(cl);
        }

        if (localizers.Count == 0)
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

        string msg = string.Format(S("mgr_remove_all_title_msg"), localizers.Count);
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

        // Fase 3: Limpiar el array canvasLocalizers del manager
        _canvasLocalizers.ClearArray();
        serializedObject.ApplyModifiedProperties();

        // Actualizar resultados de escaneo si existen
        if (_canvasSearchResults != null)
        {
            for (int i = 0; i < _canvasSearchResults.Count; i++)
                _canvasSearchResults[i].hasCanvasLocalizer = false;
        }

        Debug.Log($"[Idiomas] Eliminados {removed} CanvasLocalizer y {totalKeysInJson} claves del JSON.");
        EditorUtility.DisplayDialog(S("mgr_remove_all_done_title"),
            string.Format(S("mgr_remove_all_done_msg"), removed, totalKeysInJson),
            S("ok"));
    }

    // =====================================================================
    // Configuracion Rapida: Localizar Todo
    // =====================================================================

    /// <summary>
    /// Convierte el texto del campo en una lista unica de palabras clave.
    /// Ignora lineas vacias y espacios al principio o al final.
    /// </summary>
    private string[] ParseQuickSetupExcludedKeywords()
    {
        string[] lines = _quickSetupExcludedKeywordsText.Split(
            new[] { '\r', '\n' },
            System.StringSplitOptions.RemoveEmptyEntries);

        List<string> result = new List<string>();
        HashSet<string> unique = new HashSet<string>(
            System.StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            string keyword = lines[i].Trim();
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

    private void QuickSetupAll(int candidateCount, int candidateTextCount)
    {
        string baseLang = IdiomasLanguages.Codes[_quickSetupLangIndex];
        string langName = IdiomasLanguages.GetNativeName(baseLang);

        if (!EditorUtility.DisplayDialog(
            S("mgr_quick_setup_title"),
            string.Format(S("mgr_quick_setup_confirm"), candidateCount, candidateTextCount, langName, baseLang),
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
            $"{totalTexts} textos. Idioma base: {baseLang}");
    }

    private void RefreshCache()
    {
        TextAsset ta = _translationFile.objectReferenceValue as TextAsset;
        if (ta == null) { _cachedData = null; _cachedLanguages = null; _cachedKeys = null; _cachedJsonHash = null; return; }

        // Usar longitud + primeros/ultimos chars como hash rapido y determinístico
        string text = ta.text;
        string hash = text.Length + (text.Length > 0 ? "_" + text[0] + text[text.Length - 1] : "");
        if (hash == _cachedJsonHash) return;
        _cachedJsonHash = hash;

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

            // Invalidar cache para que las estadisticas se actualicen
            _cachedJsonHash = null;

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
