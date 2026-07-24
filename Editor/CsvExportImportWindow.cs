using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VRC.SDK3.Data;

/// <summary>
/// Ventana de Editor para exportar e importar traducciones en formato CSV.
/// Permite colaborar con traductores que no usan Unity (ej: Google Sheets).
///
/// Formato CSV:
///   key,en,es,ja,ko,...
///   btn_start,Start,Inicio,スタート,시작,...
///   btn_close,Close,Cerrar,閉じる,닫기,...
///
/// Flujo:
///   1. Exportar CSV desde el JSON actual
///   2. Compartir CSV con traductores (Google Sheets, Excel, etc.)
///   3. Importar CSV de vuelta al JSON
/// </summary>
public class CsvExportImportWindow : EditorWindow
{
    private static string S(string key) => IdiomasEditorStrings.Get(key);
    [SerializeField] private TextAsset _jsonFile;
    [SerializeField] private string _jsonPath;
    private string _statusMessage = "";
    private Vector2 _scrollPos;

    [MenuItem("Tools/Idiomas/Exportar-Importar CSV", false, 100)]
    public static void OpenWindow()
    {
        CsvExportImportWindow window = GetWindow<CsvExportImportWindow>(
            true, S("csv_window_title"), true);
        window.minSize = new Vector2(450, 350);
        window.FindJsonPath();
        window.Show();
    }

    private void FindJsonPath()
    {
        // Preferir los datos del usuario y conservar las rutas antiguas
        // como fallback para instalaciones existentes.
        string[] candidatePaths = {
            "Assets/Idiomas_Data/translation.json",
            "Assets/Idiomas/Data/translation.json",
            "Packages/com.benderdios.idiomas/Data/translation.json"
        };

        for (int i = 0; i < candidatePaths.Length; i++)
        {
            TextAsset candidate =
                AssetDatabase.LoadAssetAtPath<TextAsset>(candidatePaths[i]);
            if (candidate != null)
            {
                SetJsonFile(candidate);
                return;
            }
        }

        _jsonFile = null;
        _jsonPath = "";
    }

    private bool SetJsonFile(TextAsset jsonFile)
    {
        if (jsonFile == null)
        {
            _jsonFile = null;
            _jsonPath = "";
            _statusMessage = "";
            return true;
        }

        string assetPath = AssetDatabase.GetAssetPath(jsonFile);
        if (string.IsNullOrEmpty(assetPath) ||
            Path.GetExtension(assetPath).ToLowerInvariant() != ".json")
        {
            _statusMessage = S("csv_invalid_json");
            return false;
        }

        _jsonFile = jsonFile;
        _jsonPath = Path.GetFullPath(assetPath);
        _statusMessage = "";
        return true;
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField(S("csv_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_desc"),
            EditorStyles.miniLabel);
        EditorGUILayout.Space(8);

        // --- Archivo JSON ---
        EditorGUILayout.LabelField(S("csv_json_file"), EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        TextAsset selectedJson = (TextAsset)EditorGUILayout.ObjectField(
            _jsonFile, typeof(TextAsset), false);
        if (EditorGUI.EndChangeCheck())
        {
            SetJsonFile(selectedJson);
        }

        string assetPath = _jsonFile != null
            ? AssetDatabase.GetAssetPath(_jsonFile)
            : "";
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(assetPath) ? S("csv_not_found") : assetPath,
            EditorStyles.helpBox);

        if (string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath))
        {
            EditorGUILayout.HelpBox(S("csv_no_json_warning"), MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // === EXPORTAR ===
        EditorGUILayout.LabelField(S("csv_export_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_export_desc"),
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath));
        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button(S("csv_export_btn"), GUILayout.Height(28)))
        {
            ExportCsv();
        }
        GUI.backgroundColor = Color.white;
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(12);

        // === IMPORTAR ===
        EditorGUILayout.LabelField(S("csv_import_title"), EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            S("csv_import_desc"),
            EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space(3);

        GUI.backgroundColor = new Color(0.2f, 0.8f, 0.3f);
        if (GUILayout.Button(S("csv_import_btn"), GUILayout.Height(28)))
        {
            ImportCsv();
        }
        GUI.backgroundColor = Color.white;

        // --- Status ---
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.helpBox);
        }

        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    // Exportar
    // =====================================================================

    private void ExportCsv()
    {
        string json = File.ReadAllText(_jsonPath, Encoding.UTF8);
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data) ||
            data.TokenType != TokenType.DataDictionary)
        {
            _statusMessage = S("error_parse_json");
            return;
        }

        DataDictionary root = data.DataDictionary;

        // Recolectar idiomas y claves
        DataList langKeys = root.GetKeys();
        List<string> languages = new List<string>();
        for (int i = 0; i < langKeys.Count; i++)
            languages.Add(langKeys[i].String);
        languages.Sort();

        HashSet<string> allKeysSet = new HashSet<string>();
        for (int i = 0; i < languages.Count; i++)
        {
            if (root.TryGetValue(languages[i], out DataToken lt) &&
                lt.TokenType == TokenType.DataDictionary)
            {
                DataList keys = lt.DataDictionary.GetKeys();
                for (int k = 0; k < keys.Count; k++)
                    allKeysSet.Add(keys[k].String);
            }
        }
        List<string> allKeys = new List<string>(allKeysSet);
        allKeys.Sort();

        // Construir CSV
        StringBuilder sb = new StringBuilder();

        // Cabecera
        sb.Append("key");
        for (int i = 0; i < languages.Count; i++)
        {
            sb.Append(",");
            sb.Append(languages[i]);
        }
        sb.AppendLine();

        // Filas
        for (int k = 0; k < allKeys.Count; k++)
        {
            sb.Append(CsvEscape(allKeys[k]));
            for (int i = 0; i < languages.Count; i++)
            {
                sb.Append(",");
                string value = "";
                if (root.TryGetValue(languages[i], out DataToken lt) &&
                    lt.TokenType == TokenType.DataDictionary &&
                    lt.DataDictionary.TryGetValue(allKeys[k], out DataToken vt))
                {
                    value = vt.String;
                }
                sb.Append(CsvEscape(value));
            }
            sb.AppendLine();
        }

        // Guardar
        string csvDir = Path.GetDirectoryName(_jsonPath);
        string defaultCsvName = Path.GetFileNameWithoutExtension(_jsonPath);
        if (string.IsNullOrEmpty(defaultCsvName))
            defaultCsvName = "translation";

        string savePath = EditorUtility.SaveFilePanel(
            S("csv_save_dialog"), csvDir, defaultCsvName, "csv");
        if (string.IsNullOrEmpty(savePath)) return;

        File.WriteAllText(savePath, sb.ToString(), Encoding.UTF8);
        AssetDatabase.Refresh();

        _statusMessage = string.Format(S("csv_exported"), allKeys.Count, languages.Count, Path.GetFileName(savePath));
        Debug.Log($"[Idiomas CSV] {_statusMessage}");
    }

    // =====================================================================
    // Importar
    // =====================================================================

    private void ImportCsv()
    {
        string csvPath = EditorUtility.OpenFilePanel(S("csv_open_dialog"), "", "csv");
        if (string.IsNullOrEmpty(csvPath)) return;

        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length < 2)
        {
            _statusMessage = S("csv_empty");
            return;
        }

        // Parsear cabecera
        string[] header = ParseCsvLine(lines[0]);
        if (header.Length < 2 || header[0].Trim().ToLower() != "key")
        {
            _statusMessage = S("csv_bad_header");
            return;
        }

        string[] languages = new string[header.Length - 1];
        for (int i = 1; i < header.Length; i++)
            languages[i - 1] = header[i].Trim();

        // Cargar JSON existente
        Dictionary<string, Dictionary<string, string>> translations;
        if (!string.IsNullOrEmpty(_jsonPath) && File.Exists(_jsonPath))
        {
            string json = File.ReadAllText(_jsonPath, Encoding.UTF8);
            translations = IdiomasEditorUtils.ParseJsonToDictionary(json);
            if (translations == null)
                translations = new Dictionary<string, Dictionary<string, string>>();
        }
        else
        {
            translations = new Dictionary<string, Dictionary<string, string>>();
        }

        // Parsear filas
        int imported = 0;
        for (int row = 1; row < lines.Length; row++)
        {
            if (string.IsNullOrEmpty(lines[row].Trim())) continue;

            string[] cols = ParseCsvLine(lines[row]);
            if (cols.Length < 2) continue;

            string key = cols[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            for (int c = 0; c < languages.Length && c + 1 < cols.Length; c++)
            {
                string lang = languages[c];
                string value = cols[c + 1];

                if (string.IsNullOrEmpty(value)) continue;

                if (!translations.ContainsKey(lang))
                    translations[lang] = new Dictionary<string, string>();

                translations[lang][key] = value;
                imported++;
            }
        }

        // Escribir JSON
        if (string.IsNullOrEmpty(_jsonPath))
            _jsonPath = Path.GetFullPath("Assets/Idiomas_Data/translation.json");

        string dir = Path.GetDirectoryName(_jsonPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string newJson = IdiomasEditorUtils.WriteDictionaryToJson(translations);
        File.WriteAllText(_jsonPath, newJson, Encoding.UTF8);
        AssetDatabase.Refresh();

        string importedAssetPath = FileUtil.GetProjectRelativePath(_jsonPath);
        if (!string.IsNullOrEmpty(importedAssetPath))
            _jsonFile = AssetDatabase.LoadAssetAtPath<TextAsset>(importedAssetPath);

        _statusMessage = string.Format(S("csv_imported"), imported, Path.GetFileName(csvPath));
        Debug.Log($"[Idiomas CSV] {_statusMessage}");

        EditorUtility.DisplayDialog(S("csv_import_done_title"),
            string.Format(S("csv_import_done_msg"), imported, string.Join(", ", languages), lines.Length - 1),
            S("ok"));
    }

    // =====================================================================
    // Utilidades CSV
    // =====================================================================

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Si contiene coma, comillas o salto de linea, envolver en comillas
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    /// <summary>
    /// Parsea una linea CSV respetando comillas (campos con comas dentro de comillas).
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        StringBuilder current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Comilla doble escapada ""
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

}
