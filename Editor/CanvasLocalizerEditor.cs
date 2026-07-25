using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UdonSharpEditor;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using VRC.SDK3.Data;
using BenderDios.Idiomas;

/// <summary>
/// Inspector personalizado para CanvasLocalizer.
/// Proporciona herramientas para:
///   - Escanear automaticamente todos los textos del canvas.
///   - Generar claves de traduccion a partir de la jerarquia de GameObjects.
///   - Exportar textos originales al archivo JSON de traducciones.
///   - Excluir textos que no deben traducirse (numeros, iconos, texto dinamico).
///   - Vista previa de claves vs texto actual.
///   - Registrar automaticamente el CanvasLocalizer en el LocalizationManager.
///
/// Flujo:
///   1. Configurar canvasId y baseLanguage.
///   2. Clic "Escanear Canvas" -> detecta TMP_Text y Text en todos los hijos.
///   3. Revisar tabla: ajustar claves, marcar exclusiones.
///   4. Clic "Exportar al JSON y Aplicar" -> guarda en el JSON y llena los arrays.
/// </summary>
[CustomEditor(typeof(CanvasLocalizer))]
public class CanvasLocalizerEditor : Editor
{
    // =====================================================================
    // Propiedades serializadas
    // =====================================================================

    private SerializedProperty _manager;
    private SerializedProperty _canvasId;
    private SerializedProperty _baseLanguage;
    private SerializedProperty _tmpTexts;
    private SerializedProperty _tmpKeys;
    private SerializedProperty _legacyTexts;
    private SerializedProperty _legacyKeys;
    private SerializedProperty _excludedKeywords;
    private SerializedProperty _excludedObjects;

    // =====================================================================
    // Estado del escaneo (solo en editor, no persiste)
    // =====================================================================

    private List<ScanEntry> _scanResults;
    private bool _hasScanResults;
    private string _searchFilter = "";

    private static string S(string key) => IdiomasEditorStrings.Get(key);

    /// <summary>
    /// Resultado de escaneo para un solo componente de texto.
    /// </summary>
    private struct ScanEntry
    {
        public Component component;       // TextMeshProUGUI o Text
        public bool isTMP;                // true = TMP, false = legacy Text
        public string generatedKey;       // clave de traduccion generada/editada
        public string currentText;        // texto actual del componente
        public string objectPath;         // ruta en la jerarquia (para mostrar)
        public bool excluded;             // true = no traducir este texto
        public bool existsInJson;         // true = la clave ya existe en el JSON
        public string canonicalKey;       // clave existente que comparte el mismo texto
        public bool shareCanonicalKey;    // true = reutilizar la clave canonica
    }

    // =====================================================================
    // Nombres genericos que se saltan en la generacion de claves
    // =====================================================================

    private static readonly HashSet<string> GENERIC_NAMES = new HashSet<string>
    {
        "text", "label", "title", "text (tmp)", "tmp", "textmeshpro",
        "text (1)", "text (2)", "text (3)", "text (4)", "text (5)",
        "text (6)", "text (7)", "text (8)", "text (9)", "text (10)",
        "label (1)", "label (2)", "label (3)", "label (4)", "label (5)",
        "tmptext", "tmp text", "uitext", "ui text",
        "placeholder", "text area",
    };

    // =====================================================================
    // OnEnable / OnInspectorGUI
    // =====================================================================

    private void OnEnable()
    {
        _manager = serializedObject.FindProperty("manager");
        _canvasId = serializedObject.FindProperty("canvasId");
        _baseLanguage = serializedObject.FindProperty("baseLanguage");
        _tmpTexts = serializedObject.FindProperty("tmpTexts");
        _tmpKeys = serializedObject.FindProperty("tmpKeys");
        _legacyTexts = serializedObject.FindProperty("legacyTexts");
        _legacyKeys = serializedObject.FindProperty("legacyKeys");
        _excludedKeywords = serializedObject.FindProperty("excludedKeywords");
        _excludedObjects = serializedObject.FindProperty("_excludedObjects");

        // Auto-generar ID del canvas si esta vacio
        AutoGenerateCanvasId();

        // Reconstruir resultados de escaneo desde los arrays serializados
        RebuildScanResultsFromArrays();
    }

    /// <summary>
    /// Auto-genera un ID unico para el canvas basado en el nombre del GameObject.
    /// Solo actua si el campo canvasId esta vacio.
    /// Revisa todos los CanvasLocalizer de la escena para evitar colisiones.
    /// </summary>
    private void AutoGenerateCanvasId()
    {
        if (!string.IsNullOrEmpty(_canvasId.stringValue)) return;

        CanvasLocalizer cl = target as CanvasLocalizer;
        if (cl == null) return;

        _canvasId.stringValue = IdiomasEditorUtils.GenerateUniqueCanvasId(cl.gameObject.name, cl);
        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Reconstruye _scanResults a partir de los arrays ya serializados (tmpTexts/Keys, legacyTexts/Keys)
    /// y la lista de exclusiones. Asi al volver al inspector no se pierden los datos.
    /// </summary>
    private void RebuildScanResultsFromArrays()
    {
        int tmpCount = _tmpTexts.arraySize;
        int legacyCount = _legacyTexts.arraySize;
        int excludedCount = _excludedObjects.arraySize;

        if (tmpCount == 0 && legacyCount == 0 && excludedCount == 0)
        {
            _scanResults = null;
            _hasScanResults = false;
            return;
        }

        _scanResults = new List<ScanEntry>();
        CanvasLocalizer cl = (CanvasLocalizer)target;
        Transform root = cl.transform;

        // Cargar exclusiones
        HashSet<GameObject> excludedSet = new HashSet<GameObject>();
        for (int i = 0; i < excludedCount; i++)
        {
            Object obj = _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) excludedSet.Add(obj as GameObject);
        }

        // Reconstruir entradas TMP
        int tmpKeyCount = _tmpKeys.arraySize;
        for (int i = 0; i < tmpCount && i < tmpKeyCount; i++)
        {
            TextMeshProUGUI comp = _tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue as TextMeshProUGUI;
            string key = _tmpKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp == null) continue;

            _scanResults.Add(new ScanEntry
            {
                component = comp,
                isTMP = true,
                generatedKey = key,
                currentText = comp.text ?? "",
                objectPath = GetRelativePath(root, comp.transform),
                excluded = false,
                existsInJson = false,
            });
        }

        // Reconstruir entradas Legacy
        int legacyKeyCount = _legacyKeys.arraySize;
        for (int i = 0; i < legacyCount && i < legacyKeyCount; i++)
        {
            Text comp = _legacyTexts.GetArrayElementAtIndex(i).objectReferenceValue as Text;
            string key = _legacyKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp == null) continue;

            _scanResults.Add(new ScanEntry
            {
                component = comp,
                isTMP = false,
                generatedKey = key,
                currentText = comp.text ?? "",
                objectPath = GetRelativePath(root, comp.transform),
                excluded = false,
                existsInJson = false,
            });
        }

        // Reconstruir entradas excluidas (obtener texto actual de cada componente)
        for (int i = 0; i < excludedCount; i++)
        {
            GameObject go = _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (go == null) continue;

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                _scanResults.Add(new ScanEntry
                {
                    component = tmp,
                    isTMP = true,
                    generatedKey = "",
                    currentText = tmp.text ?? "",
                    objectPath = GetRelativePath(root, tmp.transform),
                    excluded = true,
                    existsInJson = false,
                });
                continue;
            }

            Text legacy = go.GetComponent<Text>();
            if (legacy != null)
            {
                _scanResults.Add(new ScanEntry
                {
                    component = legacy,
                    isTMP = false,
                    generatedKey = "",
                    currentText = legacy.text ?? "",
                    objectPath = GetRelativePath(root, legacy.transform),
                    excluded = true,
                    existsInJson = false,
                });
            }
        }

        // Marcar cuales ya existen en JSON
        MarkExistingKeysInJson();
        AssignCanonicalKeys();

        _hasScanResults = _scanResults.Count > 0;
    }

    /// <summary>
    /// Busca si otro CanvasLocalizer en la escena tiene el mismo canvasId.
    /// Devuelve el nombre del GameObject duplicado, o null si no hay conflicto.
    /// </summary>
    private string FindDuplicateCanvasId(string id)
    {
        CanvasLocalizer cl = target as CanvasLocalizer;
        if (cl == null) return null;

        CanvasLocalizer[] allLocalizers = FindObjectsByType<CanvasLocalizer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allLocalizers.Length; i++)
        {
            if (allLocalizers[i] == cl) continue;
            if (allLocalizers[i].GetCanvasId() == id)
                return allLocalizers[i].gameObject.name;
        }
        return null;
    }

    public override void OnInspectorGUI()
    {
        // Header estandar de UdonSharp (boton convert, program asset, sync, etc.)
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        serializedObject.Update();

        // --- Titulo ---
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(S("cl_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("cl_subtitle"),
            EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // --- Configuracion basica ---
        EditorGUILayout.PropertyField(_manager,
            new GUIContent(S("cl_manager"), S("cl_manager_tooltip")));
        EditorGUILayout.PropertyField(_canvasId,
            new GUIContent(S("cl_canvas_id"), S("cl_canvas_id_tooltip")));

        // --- Idioma Base como dropdown ---
        DrawBaseLanguageDropdown();

        // --- Palabras clave de exclusion automatica ---
        EditorGUILayout.PropertyField(_excludedKeywords,
            new GUIContent(S("cl_excluded_keywords")), true);
        EditorGUILayout.LabelField(
            S("cl_excluded_keywords_desc"), EditorStyles.wordWrappedMiniLabel);

        // Validar canvasId
        string canvasId = _canvasId.stringValue;
        if (string.IsNullOrEmpty(canvasId))
        {
            EditorGUILayout.HelpBox(
                S("cl_no_id_warning"),
                MessageType.Warning);
        }
        else
        {
            // Verificar si otro CanvasLocalizer ya usa el mismo ID
            string duplicateOwner = FindDuplicateCanvasId(canvasId);
            if (duplicateOwner != null)
            {
                EditorGUILayout.HelpBox(
                    string.Format(S("cl_duplicate_id"), canvasId, duplicateOwner),
                    MessageType.Error);
            }
        }

        EditorGUILayout.Space(8);

        // --- Resumen del estado actual ---
        int tmpCount = _tmpTexts.arraySize;
        int legacyCount = _legacyTexts.arraySize;
        int total = tmpCount + legacyCount;
        if (total > 0)
        {
            EditorGUILayout.LabelField(
                string.Format(S("cl_texts_configured"), tmpCount, legacyCount, total),
                EditorStyles.helpBox);
        }
        else
        {
            EditorGUILayout.LabelField(
                S("cl_no_texts"),
                EditorStyles.helpBox);
        }

        EditorGUILayout.Space(5);

        // === BOTON ESCANEAR ===
        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(canvasId));

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button(S("cl_scan"), GUILayout.Height(32)))
        {
            ScanCanvas();
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();

        // === RESULTADOS DEL ESCANEO ===
        if (_hasScanResults && _scanResults != null && _scanResults.Count > 0)
        {
            DrawScanResults();
        }
        else if (_hasScanResults && (_scanResults == null || _scanResults.Count == 0))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                S("cl_no_scan_results"),
                MessageType.Info);
        }


        serializedObject.ApplyModifiedProperties();
    }

    // =====================================================================
    // Dropdown de idioma base
    // =====================================================================

    // Idiomas centralizados en IdiomasLanguages.cs
    private static string[] BASE_LANG_CODES => IdiomasLanguages.Codes;
    private static string[] BASE_LANG_LABELS => IdiomasLanguages.PopupLabelsLatin;

    private void DrawBaseLanguageDropdown()
    {
        string current = _baseLanguage.stringValue;

        int selectedIndex = -1;
        for (int i = 0; i < BASE_LANG_CODES.Length; i++)
        {
            if (BASE_LANG_CODES[i] == current)
            {
                selectedIndex = i;
                break;
            }
        }

        int newIndex = EditorGUILayout.Popup(
            new GUIContent(S("cl_base_language"), S("cl_base_language_tooltip")),
            selectedIndex, BASE_LANG_LABELS);

        if (newIndex >= 0 && newIndex != selectedIndex)
        {
            _baseLanguage.stringValue = BASE_LANG_CODES[newIndex];
        }
    }

    // =====================================================================
    // Escaneo del Canvas
    // =====================================================================

    private void ScanCanvas()
    {
        CanvasLocalizer cl = (CanvasLocalizer)target;
        Transform root = cl.transform;
        string id = _canvasId.stringValue;

        // Cargar objetos excluidos
        HashSet<GameObject> excludedSet = new HashSet<GameObject>();
        for (int i = 0; i < _excludedObjects.arraySize; i++)
        {
            Object obj = _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue;
            if (obj != null) excludedSet.Add(obj as GameObject);
        }

        // Cargar asignaciones existentes para preservar claves ya configuradas
        Dictionary<Component, string> existingKeys = BuildExistingKeysMap();

        // Buscar otros CanvasLocalizer hijos (para no robar sus textos)
        HashSet<Transform> childCanvasRoots = new HashSet<Transform>();
        CanvasLocalizer[] childCLs = root.GetComponentsInChildren<CanvasLocalizer>(true);
        for (int i = 0; i < childCLs.Length; i++)
        {
            if (childCLs[i] != cl) // No excluirnos a nosotros mismos
            {
                childCanvasRoots.Add(childCLs[i].transform);
            }
        }

        _scanResults = new List<ScanEntry>();
        HashSet<string> usedKeys = GetBaseLanguageJsonKeys();

        // --- Buscar TextMeshProUGUI ---
        TextMeshProUGUI[] tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        SortComponentsByStablePath(tmps, root);
        for (int i = 0; i < tmps.Length; i++)
        {
            TextMeshProUGUI tmp = tmps[i];

            // Saltar si esta bajo otro CanvasLocalizer hijo
            if (IsUnderAny(tmp.transform, childCanvasRoots, root)) continue;

            // Saltar si tiene un TextLocalizer (gestionado individualmente)
            if (tmp.GetComponent<TextLocalizer>() != null) continue;

            string text = tmp.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;

            // Determinar clave: preservar existente o generar nueva
            string key;
            if (existingKeys.ContainsKey(tmp))
                key = existingKeys[tmp];
            else
                key = GenerateKey(root, tmp.transform, id, usedKeys);

            usedKeys.Add(key);

            bool wasConfigured = existingKeys.ContainsKey(tmp);
            _scanResults.Add(new ScanEntry
            {
                component = tmp,
                isTMP = true,
                generatedKey = key,
                currentText = text,
                objectPath = GetRelativePath(root, tmp.transform),
                excluded = excludedSet.Contains(tmp.gameObject) ||
                           ContainsExcludedKeyword(text) ||
                           (!wasConfigured && IsNonTranslatable(text)),
                existsInJson = false,
            });
        }

        // --- Buscar Text (legacy) ---
        Text[] legacyTexts = root.GetComponentsInChildren<Text>(true);
        SortComponentsByStablePath(legacyTexts, root);
        for (int i = 0; i < legacyTexts.Length; i++)
        {
            Text txt = legacyTexts[i];

            if (IsUnderAny(txt.transform, childCanvasRoots, root)) continue;
            if (txt.GetComponent<TextLocalizer>() != null) continue;

            string text = txt.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;

            string key;
            if (existingKeys.ContainsKey(txt))
                key = existingKeys[txt];
            else
                key = GenerateKey(root, txt.transform, id, usedKeys);

            usedKeys.Add(key);

            bool wasConfiguredLegacy = existingKeys.ContainsKey(txt);
            _scanResults.Add(new ScanEntry
            {
                component = txt,
                isTMP = false,
                generatedKey = key,
                currentText = text,
                objectPath = GetRelativePath(root, txt.transform),
                excluded = excludedSet.Contains(txt.gameObject) ||
                           ContainsExcludedKeyword(text) ||
                           (!wasConfiguredLegacy && IsNonTranslatable(text)),
                existsInJson = false,
            });
        }

        // Verificar cuales claves ya existen en el JSON
        MarkExistingKeysInJson();
        AssignCanonicalKeys();

        _hasScanResults = true;

        Debug.Log($"[CanvasLocalizer] Escaneo completado: {_scanResults.Count} textos encontrados en '{id}'.");
    }

    /// <summary>
    /// Construye un mapa de Component -> clave existente desde los arrays serializados.
    /// Permite preservar claves ya configuradas al re-escanear.
    /// </summary>
    private Dictionary<Component, string> BuildExistingKeysMap()
    {
        Dictionary<Component, string> map = new Dictionary<Component, string>();

        for (int i = 0; i < _tmpTexts.arraySize && i < _tmpKeys.arraySize; i++)
        {
            Object comp = _tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue;
            string key = _tmpKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp != null && !string.IsNullOrEmpty(key))
            {
                map[(Component)comp] = key;
            }
        }

        for (int i = 0; i < _legacyTexts.arraySize && i < _legacyKeys.arraySize; i++)
        {
            Object comp = _legacyTexts.GetArrayElementAtIndex(i).objectReferenceValue;
            string key = _legacyKeys.GetArrayElementAtIndex(i).stringValue;
            if (comp != null && !string.IsNullOrEmpty(key))
            {
                map[(Component)comp] = key;
            }
        }

        return map;
    }

    /// <summary>
    /// Verifica si un Transform esta bajo alguno de los roots de otros CanvasLocalizer.
    /// Evita que un CanvasLocalizer padre robe textos de un CanvasLocalizer hijo.
    /// </summary>
    private bool IsUnderAny(Transform t, HashSet<Transform> roots, Transform ownRoot)
    {
        Transform current = t;
        while (current != null && current != ownRoot)
        {
            if (roots.Contains(current)) return true;
            current = current.parent;
        }
        return false;
    }

    /// <summary>
    /// Marca en los resultados del escaneo cuales claves ya existen en el JSON.
    /// </summary>
    private void MarkExistingKeysInJson()
    {
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null) return;

        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty tfProp = mgrSO.FindProperty("translationFile");
        if (tfProp == null) return;

        TextAsset textAsset = tfProp.objectReferenceValue as TextAsset;
        if (textAsset == null) return;

        if (!VRCJson.TryDeserializeFromJson(textAsset.text, out DataToken data)) return;
        if (data.TokenType != TokenType.DataDictionary) return;

        DataDictionary rootDict = data.DataDictionary;
        string baseLanguage = _baseLanguage.stringValue;
        if (!rootDict.TryGetValue(baseLanguage, out DataToken languageToken) ||
            languageToken.TokenType != TokenType.DataDictionary)
            return;
        DataDictionary baseEntries = languageToken.DataDictionary;

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];
            entry.existsInJson =
                baseEntries.TryGetValue(
                    entry.generatedKey, out DataToken storedText) &&
                storedText.TokenType == TokenType.String &&
                storedText.String == entry.currentText;
            _scanResults[i] = entry;
        }
    }

    // =====================================================================
    // Dibujar tabla de resultados
    // =====================================================================

    private void DrawScanResults()
    {
        EditorGUILayout.Space(8);

        // --- Resumen ---
        int totalCount = _scanResults.Count;
        int excludedCount = 0;
        int newCount = 0;
        int existingCount = 0;

        for (int i = 0; i < totalCount; i++)
        {
            if (_scanResults[i].excluded) excludedCount++;
            else if (_scanResults[i].existsInJson) existingCount++;
            else newCount++;
        }

        EditorGUILayout.LabelField(
            string.Format(S("cl_scan_results"), totalCount),
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            string.Format(S("cl_scan_summary"), totalCount - excludedCount, existingCount, newCount, excludedCount),
            EditorStyles.miniLabel);

        bool hasCanonicalKeys = false;
        for (int i = 0; i < totalCount; i++)
        {
            if (!string.IsNullOrEmpty(_scanResults[i].canonicalKey))
            {
                hasCanonicalKeys = true;
                break;
            }
        }
        if (hasCanonicalKeys)
            EditorGUILayout.HelpBox(S("cl_canonical_key_desc"), MessageType.Info);

        // --- Leyenda de colores ---
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        DrawColorLegend(new Color(0.3f, 0.7f, 0.3f, 1f), S("cl_legend_in_json"));
        DrawColorLegend(new Color(1f, 0.85f, 0.2f, 1f), S("cl_legend_new"));
        DrawColorLegend(new Color(0.5f, 0.5f, 0.5f, 1f), S("cl_legend_excluded"));
        DrawColorLegend(new Color(1f, 0.2f, 0.2f, 1f), S("cl_legend_duplicate"));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        // --- Construir set de claves para detectar duplicados (mejora 4) ---
        HashSet<string> duplicateCheck = new HashSet<string>();
        HashSet<string> duplicateKeys = new HashSet<string>();
        for (int i = 0; i < _scanResults.Count; i++)
        {
            if (_scanResults[i].excluded) continue;
            string k = _scanResults[i].generatedKey;
            if (!string.IsNullOrEmpty(k))
            {
                if (!duplicateCheck.Add(k))
                    duplicateKeys.Add(k);
            }
        }

        if (duplicateKeys.Count > 0)
        {
            EditorGUILayout.HelpBox(
                string.Format(S("cl_duplicate_warning"), duplicateKeys.Count),
                MessageType.Error);
        }

        // --- Botones incluir/excluir todos + filtro ---
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(S("cl_include_all"), EditorStyles.miniButton, GUILayout.Width(85)))
        {
            for (int i = 0; i < _scanResults.Count; i++)
            {
                ScanEntry e = _scanResults[i];
                e.excluded = false;
                _scanResults[i] = e;
            }
        }
        if (GUILayout.Button(S("cl_exclude_all"), EditorStyles.miniButton, GUILayout.Width(85)))
        {
            for (int i = 0; i < _scanResults.Count; i++)
            {
                ScanEntry e = _scanResults[i];
                e.excluded = true;
                _scanResults[i] = e;
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField(S("cl_filter"), GUILayout.Width(42));
        _searchFilter = EditorGUILayout.TextField(_searchFilter, GUILayout.MinWidth(60));
        if (!string.IsNullOrEmpty(_searchFilter))
        {
            if (GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(20)))
                _searchFilter = "";
        }
        EditorGUILayout.EndHorizontal();

        string filterLower = string.IsNullOrEmpty(_searchFilter)
            ? null
            : _searchFilter.ToLower();

        EditorGUILayout.Space(2);

        // --- Cabecera de tabla ---
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(S("cl_col_include"), EditorStyles.miniLabel, GUILayout.Width(32));
        GUILayout.Label(S("cl_col_key"), EditorStyles.miniLabel, GUILayout.MinWidth(120));
        GUILayout.Label(S("cl_col_text"), EditorStyles.miniLabel, GUILayout.MinWidth(100));
        GUILayout.Label(S("cl_col_path"), EditorStyles.miniLabel, GUILayout.Width(140));
        GUILayout.Label("", GUILayout.Width(20)); // Columna ping
        EditorGUILayout.EndHorizontal();

        // --- Lista de textos (sin scroll interno, usa el scroll del Inspector) ---

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];

            // Filtrar por busqueda (mejora 6)
            if (filterLower != null)
            {
                bool match = entry.generatedKey.ToLower().Contains(filterLower) ||
                             entry.currentText.ToLower().Contains(filterLower) ||
                             entry.objectPath.ToLower().Contains(filterLower);
                if (!match) continue;
            }

            // Color de fondo segun estado
            Color bgColor;
            if (entry.excluded)
                bgColor = new Color(0.5f, 0.5f, 0.5f, 0.12f);
            else if (entry.existsInJson)
                bgColor = new Color(0.2f, 0.8f, 0.2f, 0.12f);
            else
                bgColor = new Color(1f, 0.8f, 0.1f, 0.12f);

            // Claves duplicadas en rojo
            bool isDuplicate = !entry.excluded &&
                !string.IsNullOrEmpty(entry.generatedKey) &&
                duplicateKeys.Contains(entry.generatedKey);
            if (isDuplicate)
                bgColor = new Color(1f, 0.15f, 0.15f, 0.2f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Checkbox incluir (invertido: check = incluido)
            bool included = EditorGUILayout.Toggle(
                !entry.excluded, GUILayout.Width(32));
            if (included == entry.excluded) // Cambio
            {
                entry.excluded = !included;
                _scanResults[i] = entry;
            }

            // Clave editable
            string newKey = EditorGUILayout.TextField(
                entry.generatedKey, GUILayout.MinWidth(120));
            if (newKey != entry.generatedKey)
            {
                entry.generatedKey = newKey;
                _scanResults[i] = entry;
            }

            // Texto actual (solo lectura, con tooltip completo)
            string fullText = entry.currentText.Replace("\n", " ").Replace("\r", "");
            EditorGUILayout.LabelField(
                new GUIContent(fullText, fullText),
                GUILayout.MinWidth(100));

            // Ruta en la jerarquia (con tooltip completo)
            string displayPath = entry.objectPath;
            string fullPath = entry.objectPath;
            if (displayPath.Length > 24)
                displayPath = "..." + displayPath.Substring(displayPath.Length - 21);
            EditorGUILayout.LabelField(
                new GUIContent(displayPath, fullPath),
                EditorStyles.miniLabel, GUILayout.Width(140));

            // Boton ping para seleccionar el GameObject en la jerarquia (mejora 2)
            if (entry.component != null)
            {
                if (GUILayout.Button(
                    new GUIContent("\u25CE", S("cl_ping_tooltip")),
                    EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    EditorGUIUtility.PingObject(entry.component.gameObject);
                    Selection.activeGameObject = entry.component.gameObject;
                }
            }
            else
            {
                GUILayout.Space(20);
            }

            EditorGUILayout.EndHorizontal();

            if (!entry.excluded && !string.IsNullOrEmpty(entry.canonicalKey))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    S("cl_canonical_key") + ": " + entry.canonicalKey,
                    EditorStyles.miniLabel);
                bool share = EditorGUILayout.ToggleLeft(
                    S("cl_share_canonical_key"), entry.shareCanonicalKey,
                    GUILayout.Width(170));
                if (share != entry.shareCanonicalKey)
                {
                    entry.shareCanonicalKey = share;
                    _scanResults[i] = entry;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }

            // Dibujar rectangulo de color sobre la fila ya renderizada
            Rect rowRect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(rowRect, bgColor);
        }

        EditorGUILayout.Space(5);

        // --- Boton de accion ---
        EditorGUI.BeginDisabledGroup(duplicateKeys.Count > 0);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button(S("cl_export_btn"), GUILayout.Height(30)))
        {
            ExportToJsonAndApply();
        }
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();
    }

    private void DrawColorLegend(Color color, string label)
    {
        Rect rect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
        EditorGUI.DrawRect(rect, color);
        GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(60));
    }

    // =====================================================================
    // Exportar al JSON
    // =====================================================================

    /// <summary>
    /// Ruta por defecto donde se crea el JSON si no existe ninguno.
    /// </summary>
    private const string DEFAULT_JSON_DIR = "Assets/Idiomas_Data";
    private const string DEFAULT_JSON_NAME = "translation.json";

    private static string GetOutputKey(ScanEntry entry)
    {
        return entry.shareCanonicalKey && !string.IsNullOrEmpty(entry.canonicalKey)
            ? entry.canonicalKey
            : entry.generatedKey;
    }

    private HashSet<string> GetBaseLanguageJsonKeys()
    {
        HashSet<string> keys = new HashSet<string>();
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null) return keys;

        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty translationFile =
            mgrSO.FindProperty("translationFile");
        TextAsset textAsset = translationFile != null
            ? translationFile.objectReferenceValue as TextAsset
            : null;
        if (textAsset == null) return keys;

        Dictionary<string, Dictionary<string, string>> translations =
            IdiomasEditorUtils.ParseJsonToDictionary(textAsset.text);
        string baseLang = _baseLanguage.stringValue;
        if (translations == null || !translations.ContainsKey(baseLang))
            return keys;

        keys.UnionWith(translations[baseLang].Keys);
        return keys;
    }

    /// <summary>
    /// Asigna una clave canonica cuando otro texto identico ya existe en el idioma base.
    /// La comparacion es exacta para no mezclar textos que requieren traducciones distintas.
    /// </summary>
    private void AssignCanonicalKeys()
    {
        if (_scanResults == null) return;

        Dictionary<string, string> canonicalByText = new Dictionary<string, string>(
            System.StringComparer.Ordinal);
        Dictionary<string, string> baseEntries = null;
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj != null)
        {
            SerializedObject mgrSO = new SerializedObject(managerObj);
            SerializedProperty tfProp = mgrSO.FindProperty("translationFile");
            TextAsset textAsset = tfProp != null
                ? tfProp.objectReferenceValue as TextAsset
                : null;
            if (textAsset != null)
            {
                var translations = IdiomasEditorUtils.ParseJsonToDictionary(textAsset.text);
                string baseLang = _baseLanguage.stringValue;
                if (translations != null && translations.ContainsKey(baseLang))
                {
                    baseEntries = translations[baseLang];
                    List<string> keys = new List<string>(baseEntries.Keys);
                    keys.Sort(System.StringComparer.Ordinal);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        string value = baseEntries[keys[i]];
                        if (!string.IsNullOrEmpty(value) && !canonicalByText.ContainsKey(value))
                            canonicalByText[value] = keys[i];
                    }
                }
            }
        }

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];
            entry.canonicalKey = null;
            entry.shareCanonicalKey = false;

            // Separar automaticamente una clave compartida si solo este texto cambio.
            if (baseEntries != null && baseEntries.ContainsKey(entry.generatedKey) &&
                baseEntries[entry.generatedKey] != entry.currentText &&
                IdiomasEditorUtils.CountSceneTranslationKeyReferences(
                    entry.generatedKey) > 1)
            {
                entry.generatedKey = GenerateDetachedKey(entry.generatedKey, baseEntries);
                entry.existsInJson = false;
            }

            if (!entry.excluded && canonicalByText.TryGetValue(entry.currentText, out string canonical) &&
                canonical != entry.generatedKey)
            {
                entry.canonicalKey = canonical;
                entry.shareCanonicalKey = true;
            }
            else if (!entry.excluded && !canonicalByText.ContainsKey(entry.currentText))
            {
                canonicalByText[entry.currentText] = entry.generatedKey;
            }
            _scanResults[i] = entry;
        }
    }

    private string GenerateDetachedKey(string originalKey,
        Dictionary<string, string> baseEntries)
    {
        HashSet<string> used = new HashSet<string>(baseEntries.Keys);
        for (int i = 0; i < _scanResults.Count; i++)
            used.Add(_scanResults[i].generatedKey);

        int suffix = 2;
        string candidate = originalKey + "_" + suffix;
        while (used.Contains(candidate) ||
            IdiomasEditorUtils.CountSceneTranslationKeyReferences(candidate) > 0)
        {
            suffix++;
            candidate = originalKey + "_" + suffix;
        }
        return candidate;
    }

    private void ExportToJsonAndApply()
    {
        if (_scanResults == null || _scanResults.Count == 0)
        {
            EditorUtility.DisplayDialog(S("cl_no_results_title"),
                S("cl_no_results_msg"), S("ok"));
            return;
        }

        // Verificar manager
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null)
        {
            EditorUtility.DisplayDialog(S("error_title"),
                S("cl_no_manager_msg"), S("ok"));
            return;
        }

        string baseLang = _baseLanguage.stringValue;
        if (string.IsNullOrEmpty(baseLang))
        {
            EditorUtility.DisplayDialog(S("error_title"),
                S("cl_no_base_lang_msg"), S("ok"));
            return;
        }

        // ============================================================
        // Obtener o CREAR el archivo JSON de traducciones
        // ============================================================
        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty tfProp = mgrSO.FindProperty("translationFile");

        string assetPath;
        string fullPath;
        Dictionary<string, Dictionary<string, string>> translations;

        TextAsset textAsset = (tfProp != null) ? tfProp.objectReferenceValue as TextAsset : null;

        if (textAsset != null)
        {
            // --- Archivo existente: leer y parsear ---
            assetPath = AssetDatabase.GetAssetPath(textAsset);
            fullPath = Path.GetFullPath(assetPath);

            if (File.Exists(fullPath))
            {
                string jsonContent = File.ReadAllText(fullPath, Encoding.UTF8);
                translations = IdiomasEditorUtils.ParseJsonToDictionary(jsonContent);
                if (translations == null)
                {
                    // JSON corrupto: empezar de cero
                    Debug.LogWarning("[CanvasLocalizer] JSON corrupto, se creara uno nuevo.");
                    translations = new Dictionary<string, Dictionary<string, string>>();
                }
            }
            else
            {
                // Referencia existe pero archivo fue borrado del disco
                Debug.LogWarning($"[CanvasLocalizer] Archivo '{assetPath}' no existe en disco. Se recreara.");
                translations = new Dictionary<string, Dictionary<string, string>>();
            }
        }
        else
        {
            // --- No hay archivo asignado: CREAR uno nuevo ---
            assetPath = DEFAULT_JSON_DIR + "/" + DEFAULT_JSON_NAME;
            fullPath = Path.GetFullPath(assetPath);
            translations = new Dictionary<string, Dictionary<string, string>>();

            // Crear directorio si no existe
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Debug.Log($"[CanvasLocalizer] Creando archivo de traducciones nuevo: {assetPath}");
        }

        // ============================================================
        // Agregar claves del escaneo al idioma base
        // ============================================================
        if (!translations.ContainsKey(baseLang))
        {
            translations[baseLang] = new Dictionary<string, string>();
        }

        // ============================================================
        // Calcular cambios antes de escribir (vista previa)
        // ============================================================
        List<string> newKeys = new List<string>();
        List<string> updatedKeys = new List<string>();

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];
            if (entry.excluded) continue;

            string key = GetOutputKey(entry);
            string value = entry.currentText;
            if (string.IsNullOrEmpty(key)) continue;

            if (!translations[baseLang].ContainsKey(key))
            {
                if (!newKeys.Contains(key)) newKeys.Add(key);
            }
            else if (translations[baseLang][key] != value)
            {
                if (!updatedKeys.Contains(key)) updatedKeys.Add(key);
            }
        }

        // Mostrar vista previa y pedir confirmacion
        if (newKeys.Count > 0 || updatedKeys.Count > 0)
        {
            StringBuilder preview = new StringBuilder();
            preview.AppendLine(string.Format(S("cl_export_base_lang"), baseLang));
            preview.AppendLine(string.Format(S("cl_export_file"), assetPath) + "\n");

            if (newKeys.Count > 0)
            {
                preview.AppendLine(string.Format(S("cl_export_new_keys"), newKeys.Count));
                for (int i = 0; i < newKeys.Count && i < 20; i++)
                    preview.AppendLine($"  + {newKeys[i]}");
                if (newKeys.Count > 20)
                    preview.AppendLine(string.Format(S("cl_export_and_more"), newKeys.Count - 20));
                preview.AppendLine();
            }

            if (updatedKeys.Count > 0)
            {
                preview.AppendLine(string.Format(S("cl_export_updated_keys"), updatedKeys.Count));
                for (int i = 0; i < updatedKeys.Count && i < 20; i++)
                    preview.AppendLine($"  ~ {updatedKeys[i]}");
                if (updatedKeys.Count > 20)
                    preview.AppendLine(string.Format(S("cl_export_and_more"), updatedKeys.Count - 20));
            }

            if (!EditorUtility.DisplayDialog(S("cl_confirm_export_title"),
                preview.ToString(), S("cl_export_confirm"), S("cancel")))
            {
                return;
            }
        }

        // ============================================================
        // Aplicar cambios al diccionario
        // ============================================================
        for (int i = 0; i < newKeys.Count; i++)
        {
            string key = newKeys[i];
            // Buscar el valor del scanResult correspondiente
            for (int j = 0; j < _scanResults.Count; j++)
            {
                if (!_scanResults[j].excluded && GetOutputKey(_scanResults[j]) == key)
                {
                    translations[baseLang][key] = _scanResults[j].currentText;
                    break;
                }
            }
        }

        for (int i = 0; i < updatedKeys.Count; i++)
        {
            string key = updatedKeys[i];
            for (int j = 0; j < _scanResults.Count; j++)
            {
                if (!_scanResults[j].excluded && GetOutputKey(_scanResults[j]) == key)
                {
                    translations[baseLang][key] = _scanResults[j].currentText;
                    break;
                }
            }
        }

        // ============================================================
        // Escribir JSON al disco
        // ============================================================
        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(fullPath, newJson, Encoding.UTF8);

        AssetDatabase.Refresh();

        // ============================================================
        // Asignar el archivo al LocalizationManager si no tenia uno
        // ============================================================
        if (textAsset == null)
        {
            TextAsset newAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (newAsset != null && tfProp != null)
            {
                tfProp.objectReferenceValue = newAsset;
                mgrSO.ApplyModifiedProperties();
                Debug.Log($"[CanvasLocalizer] Archivo asignado automaticamente al LocalizationManager.");
            }
        }

        int added = newKeys.Count;
        int updated = updatedKeys.Count;
        Debug.Log($"[CanvasLocalizer] JSON exportado: {added} nuevas, {updated} actualizadas. " +
                  $"Archivo: {assetPath}");

        // Aplicar a los arrays serializados
        ApplyToArrays();
    }

    // =====================================================================
    // Aplicar resultados a los arrays serializados
    // =====================================================================

    private void ApplyToArrays()
    {
        if (_scanResults == null) return;

        List<TextMeshProUGUI> tmpList = new List<TextMeshProUGUI>();
        List<string> tmpKeyList = new List<string>();
        List<Text> legacyList = new List<Text>();
        List<string> legacyKeyList = new List<string>();
        List<GameObject> excludedList = new List<GameObject>();

        for (int i = 0; i < _scanResults.Count; i++)
        {
            ScanEntry entry = _scanResults[i];

            if (entry.excluded)
            {
                if (entry.component != null)
                    excludedList.Add(entry.component.gameObject);
                continue;
            }

            string outputKey = GetOutputKey(entry);
            if (string.IsNullOrEmpty(outputKey)) continue;

            if (entry.isTMP)
            {
                TextMeshProUGUI tmp = entry.component as TextMeshProUGUI;
                if (tmp != null)
                {
                    tmpList.Add(tmp);
                    tmpKeyList.Add(outputKey);
                }
            }
            else
            {
                Text txt = entry.component as Text;
                if (txt != null)
                {
                    legacyList.Add(txt);
                    legacyKeyList.Add(outputKey);
                }
            }
        }

        // Escribir arrays TMP
        _tmpTexts.arraySize = tmpList.Count;
        _tmpKeys.arraySize = tmpKeyList.Count;
        for (int i = 0; i < tmpList.Count; i++)
        {
            _tmpTexts.GetArrayElementAtIndex(i).objectReferenceValue = tmpList[i];
            _tmpKeys.GetArrayElementAtIndex(i).stringValue = tmpKeyList[i];
        }

        // Escribir arrays Legacy
        _legacyTexts.arraySize = legacyList.Count;
        _legacyKeys.arraySize = legacyKeyList.Count;
        for (int i = 0; i < legacyList.Count; i++)
        {
            _legacyTexts.GetArrayElementAtIndex(i).objectReferenceValue = legacyList[i];
            _legacyKeys.GetArrayElementAtIndex(i).stringValue = legacyKeyList[i];
        }

        // Escribir exclusiones
        _excludedObjects.arraySize = excludedList.Count;
        for (int i = 0; i < excludedList.Count; i++)
        {
            _excludedObjects.GetArrayElementAtIndex(i).objectReferenceValue = excludedList[i];
        }

        serializedObject.ApplyModifiedProperties();

        // Registrar este CanvasLocalizer en el LocalizationManager
        RegisterWithManager();

        int total = tmpList.Count + legacyList.Count;
        Debug.Log($"[CanvasLocalizer] Aplicado: {tmpList.Count} TMP + " +
                  $"{legacyList.Count} Legacy = {total} textos configurados.");
    }

    /// <summary>
    /// Registra este CanvasLocalizer en el array canvasLocalizers del LocalizationManager.
    /// </summary>
    private void RegisterWithManager()
    {
        Object managerObj = _manager.objectReferenceValue;
        if (managerObj == null) return;

        CanvasLocalizer cl = (CanvasLocalizer)target;
        SerializedObject mgrSO = new SerializedObject(managerObj);
        SerializedProperty clProp = mgrSO.FindProperty("canvasLocalizers");
        if (clProp == null) return;

        // Verificar si ya esta registrado
        for (int i = 0; i < clProp.arraySize; i++)
        {
            if (clProp.GetArrayElementAtIndex(i).objectReferenceValue == cl)
                return; // Ya registrado
        }

        // Agregar al final
        int index = clProp.arraySize;
        clProp.arraySize = index + 1;
        clProp.GetArrayElementAtIndex(index).objectReferenceValue = cl;
        mgrSO.ApplyModifiedProperties();

        Debug.Log($"[CanvasLocalizer] Registrado en LocalizationManager.");
    }

    // =====================================================================
    // Generacion de claves
    // =====================================================================

    /// <summary>
    /// Genera una clave de traduccion a partir de la jerarquia del GameObject.
    /// Formato: {canvasId}_{segmento1}_{segmento2}
    /// Nombres genericos como "Text", "Label" se saltan en favor del padre.
    /// Si hay duplicados se agrega _2, _3, etc.
    /// </summary>
    private static string GenerateKey(Transform root, Transform textTransform,
        string canvasId, HashSet<string> usedKeys)
    {
        // Construir segmentos de ruta desde root hasta el texto
        List<string> segments = new List<string>();
        Transform current = textTransform;

        while (current != null && current != root)
        {
            segments.Insert(0, current.name);
            current = current.parent;
        }

        // Si el ultimo segmento es un nombre generico, quitarlo
        // (el nombre del padre es mas descriptivo)
        if (segments.Count > 1)
        {
            string lastName = segments[segments.Count - 1].ToLower().Trim();
            if (GENERIC_NAMES.Contains(lastName))
            {
                segments.RemoveAt(segments.Count - 1);
            }
        }

        // Normalizar cada segmento
        for (int i = 0; i < segments.Count; i++)
        {
            segments[i] = IdiomasEditorUtils.NormalizeName(segments[i]);
        }

        // Quitar segmentos vacios
        segments.RemoveAll(s => string.IsNullOrEmpty(s));

        // Construir clave
        string key;
        if (segments.Count == 0)
        {
            key = canvasId + "_text";
        }
        else
        {
            key = canvasId + "_" + string.Join("_", segments);
        }

        // Resolver duplicados
        string baseKey = key;
        int counter = 2;
        while (usedKeys.Contains(key))
        {
            key = baseKey + "_" + counter;
            counter++;
        }

        return key;
    }

    private static void SortComponentsByStablePath<T>(
        T[] components, Transform root) where T : Component
    {
        System.Array.Sort(components, (a, b) =>
        {
            int pathComparison = string.Compare(
                IdiomasEditorUtils.GetStableHierarchyPath(root, a.transform),
                IdiomasEditorUtils.GetStableHierarchyPath(root, b.transform),
                System.StringComparison.Ordinal);
            if (pathComparison != 0) return pathComparison;
            return GetComponentOrder(a).CompareTo(GetComponentOrder(b));
        });
    }

    private static int GetComponentOrder(Component component)
    {
        Component[] sameType = component.GetComponents(component.GetType());
        for (int i = 0; i < sameType.Length; i++)
        {
            if (sameType[i] == component) return i;
        }
        return 0;
    }

    /// <summary>
    /// <summary>
    /// Obtiene la ruta relativa desde root hasta child como "Padre/Hijo/Nieto".
    /// </summary>
    private static string GetRelativePath(Transform root, Transform child)
    {
        List<string> parts = new List<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            parts.Insert(0, current.name);
            current = current.parent;
        }
        return string.Join("/", parts);
    }

    /// <summary>
    /// Detecta si un texto no deberia traducirse (numeros, signos, placeholders).
    /// Devuelve true si el texto parece no-traducible.
    /// </summary>
    private static bool IsNonTranslatable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        for (int i = 0; i < text.Length; i++)
            if (char.IsLetter(text[i])) return false;
        return true;
    }

    /// <summary>
    /// Comprueba si el texto contiene alguna de las palabras clave configuradas.
    /// La comparacion no distingue entre mayusculas y minusculas.
    /// </summary>
    private bool ContainsExcludedKeyword(string text)
    {
        if (string.IsNullOrEmpty(text) || _excludedKeywords == null) return false;

        string[] keywords = new string[_excludedKeywords.arraySize];
        for (int i = 0; i < _excludedKeywords.arraySize; i++)
            keywords[i] = _excludedKeywords.GetArrayElementAtIndex(i).stringValue;

        return ContainsExcludedKeyword(text, keywords);
    }

    /// <summary>
    /// Comprueba palabras clave desde una propiedad serializada durante la configuracion rapida.
    /// </summary>
    private static bool QuickSetup_ContainsExcludedKeyword(string text, SerializedProperty keywords)
    {
        if (string.IsNullOrEmpty(text) || keywords == null) return false;

        string[] values = new string[keywords.arraySize];
        for (int i = 0; i < keywords.arraySize; i++)
            values[i] = keywords.GetArrayElementAtIndex(i).stringValue;

        return ContainsExcludedKeyword(text, values);
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

    // =====================================================================
    // Configuracion Rapida (API estatica para uso externo)
    // =====================================================================

    /// <summary>
    /// Escanea un canvas y aplica los resultados a sus arrays serializados.
    /// Agrega las claves encontradas al diccionario de traducciones (sin escribir a disco).
    /// No registra el CanvasLocalizer con el manager (el llamador lo hace).
    ///
    /// Usado por LocalizationManagerEditor para procesamiento masivo.
    /// El CanvasLocalizer debe tener canvasId y manager ya asignados.
    /// </summary>
    /// <param name="cl">CanvasLocalizer ya configurado.</param>
    /// <param name="baseLang">Codigo del idioma base (ej: "es").</param>
    /// <param name="translations">Diccionario de traducciones donde agregar claves nuevas.</param>
    /// <returns>Numero de textos configurados, o -1 si hubo error.</returns>
    public static int QuickSetup(CanvasLocalizer cl, string baseLang,
        Dictionary<string, Dictionary<string, string>> translations)
    {
        if (cl == null) return -1;

        SerializedObject clSO = new SerializedObject(cl);
        string canvasId = clSO.FindProperty("canvasId").stringValue;
        if (string.IsNullOrEmpty(canvasId)) return -1;

        // Asignar idioma base
        clSO.FindProperty("baseLanguage").stringValue = baseLang;
        clSO.ApplyModifiedProperties();

        // Asegurar que el idioma base existe en el diccionario
        if (!translations.ContainsKey(baseLang))
            translations[baseLang] = new Dictionary<string, string>();

        // Reutilizar una unica clave para textos originales exactamente iguales.
        Dictionary<string, string> canonicalByText = new Dictionary<string, string>(
            System.StringComparer.Ordinal);
        List<string> existingCanonicalKeys = new List<string>(translations[baseLang].Keys);
        existingCanonicalKeys.Sort(System.StringComparer.Ordinal);
        for (int i = 0; i < existingCanonicalKeys.Count; i++)
        {
            string value = translations[baseLang][existingCanonicalKeys[i]];
            if (!string.IsNullOrEmpty(value) && !canonicalByText.ContainsKey(value))
                canonicalByText[value] = existingCanonicalKeys[i];
        }

        Transform root = cl.transform;

        // Buscar otros CanvasLocalizer hijos para no robar sus textos
        HashSet<Transform> childCanvasRoots = new HashSet<Transform>();
        CanvasLocalizer[] childCLs = root.GetComponentsInChildren<CanvasLocalizer>(true);
        for (int i = 0; i < childCLs.Length; i++)
        {
            if (childCLs[i] != cl)
                childCanvasRoots.Add(childCLs[i].transform);
        }

        // Listas para los resultados
        List<TextMeshProUGUI> tmpList = new List<TextMeshProUGUI>();
        List<string> tmpKeyList = new List<string>();
        List<Text> legacyList = new List<Text>();
        List<string> legacyKeyList = new List<string>();
        List<GameObject> excludedList = new List<GameObject>();
        // Reservar claves existentes para no sobrescribir una traduccion
        // distinta al recrear los componentes.
        HashSet<string> usedKeys = new HashSet<string>(
            translations[baseLang].Keys);
        SerializedProperty excludedKeywordsProp = clSO.FindProperty("excludedKeywords");

        // --- Escanear TextMeshProUGUI ---
        TextMeshProUGUI[] tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        SortComponentsByStablePath(tmps, root);
        for (int i = 0; i < tmps.Length; i++)
        {
            TextMeshProUGUI tmp = tmps[i];
            if (QuickSetup_IsUnderAny(tmp.transform, childCanvasRoots, root)) continue;
            if (tmp.GetComponent<TextLocalizer>() != null) continue;
            string text = tmp.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;
            if (IsNonTranslatable(text)) continue;
            if (QuickSetup_ContainsExcludedKeyword(text, excludedKeywordsProp))
            {
                excludedList.Add(tmp.gameObject);
                continue;
            }

            string key;
            if (!canonicalByText.TryGetValue(text, out key))
            {
                key = GenerateKey(root, tmp.transform, canvasId, usedKeys);
                canonicalByText[text] = key;
                translations[baseLang][key] = text;
            }
            usedKeys.Add(key);
            tmpList.Add(tmp);
            tmpKeyList.Add(key);
        }

        // --- Escanear Text (legacy) ---
        Text[] legacyTextsArr = root.GetComponentsInChildren<Text>(true);
        SortComponentsByStablePath(legacyTextsArr, root);
        for (int i = 0; i < legacyTextsArr.Length; i++)
        {
            Text txt = legacyTextsArr[i];
            if (QuickSetup_IsUnderAny(txt.transform, childCanvasRoots, root)) continue;
            if (txt.GetComponent<TextLocalizer>() != null) continue;
            string text = txt.text;
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text)) continue;
            if (IsNonTranslatable(text)) continue;
            if (QuickSetup_ContainsExcludedKeyword(text, excludedKeywordsProp))
            {
                excludedList.Add(txt.gameObject);
                continue;
            }

            string key;
            if (!canonicalByText.TryGetValue(text, out key))
            {
                key = GenerateKey(root, txt.transform, canvasId, usedKeys);
                canonicalByText[text] = key;
                translations[baseLang][key] = text;
            }
            usedKeys.Add(key);
            legacyList.Add(txt);
            legacyKeyList.Add(key);
        }

        int totalTexts = tmpList.Count + legacyList.Count;
        if (totalTexts == 0 && excludedList.Count == 0) return 0;

        // === Aplicar a los arrays serializados ===
        clSO = new SerializedObject(cl);
        SerializedProperty tmpTextsProp = clSO.FindProperty("tmpTexts");
        SerializedProperty tmpKeysProp = clSO.FindProperty("tmpKeys");
        SerializedProperty legacyTextsProp = clSO.FindProperty("legacyTexts");
        SerializedProperty legacyKeysProp = clSO.FindProperty("legacyKeys");
        SerializedProperty excludedObjectsProp = clSO.FindProperty("_excludedObjects");

        tmpTextsProp.arraySize = tmpList.Count;
        tmpKeysProp.arraySize = tmpKeyList.Count;
        for (int i = 0; i < tmpList.Count; i++)
        {
            tmpTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = tmpList[i];
            tmpKeysProp.GetArrayElementAtIndex(i).stringValue = tmpKeyList[i];
        }

        legacyTextsProp.arraySize = legacyList.Count;
        legacyKeysProp.arraySize = legacyKeyList.Count;
        for (int i = 0; i < legacyList.Count; i++)
        {
            legacyTextsProp.GetArrayElementAtIndex(i).objectReferenceValue = legacyList[i];
            legacyKeysProp.GetArrayElementAtIndex(i).stringValue = legacyKeyList[i];
        }

        excludedObjectsProp.arraySize = excludedList.Count;
        for (int i = 0; i < excludedList.Count; i++)
            excludedObjectsProp.GetArrayElementAtIndex(i).objectReferenceValue = excludedList[i];

        clSO.ApplyModifiedProperties();
        return totalTexts;
    }

    /// <summary>
    /// Verifica si un Transform esta bajo alguno de los roots dados.
    /// Version estatica para QuickSetup.
    /// </summary>
    private static bool QuickSetup_IsUnderAny(Transform t, HashSet<Transform> roots, Transform ownRoot)
    {
        Transform current = t;
        while (current != null && current != ownRoot)
        {
            if (roots.Contains(current)) return true;
            current = current.parent;
        }
        return false;
    }

}
