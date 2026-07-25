using UnityEditor;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
using VRC.SDK3.Data;
using System.Collections.Generic;
using System.Text;
using BenderDios.Idiomas;

/// <summary>
/// Utilidades compartidas para los editor scripts del sistema Idiomas.
/// Centraliza la logica de parseo/escritura de JSON y busqueda de UdonBehaviour
/// que antes estaba duplicada en AutoTranslateWindow, CsvExportImportWindow,
/// LocalizationManagerEditor e IdiomasPrefabCreator.
/// </summary>
public static class IdiomasEditorUtils
{
    // =====================================================================
    // JSON: Parseo
    // =====================================================================

    /// <summary>
    /// Parsea un string JSON de traducciones a un Dictionary editable.
    /// Formato esperado: { "lang": { "key": "value", ... }, ... }
    /// Retorna null si el JSON es invalido.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ParseJsonToDictionary(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken data)) return null;
        if (data.TokenType != TokenType.DataDictionary) return null;

        var result = new Dictionary<string, Dictionary<string, string>>();
        DataDictionary rootDict = data.DataDictionary;
        DataList langs = rootDict.GetKeys();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i].String;
            if (!rootDict.TryGetValue(lang, out DataToken langToken)) continue;
            if (langToken.TokenType != TokenType.DataDictionary) continue;

            var langDict = new Dictionary<string, string>();
            DataDictionary langData = langToken.DataDictionary;
            DataList keys = langData.GetKeys();
            for (int j = 0; j < keys.Count; j++)
            {
                string key = keys[j].String;
                if (langData.TryGetValue(key, out DataToken val))
                    langDict[key] = val.String;
            }
            result[lang] = langDict;
        }
        return result;
    }

    // =====================================================================
    // JSON: Escritura
    // =====================================================================

    /// <summary>
    /// Escribe un Dictionary de traducciones a formato JSON con indentacion.
    /// Ordena idiomas y claves alfabeticamente para consistencia.
    /// </summary>
    public static string WriteDictionaryToJson(Dictionary<string, Dictionary<string, string>> data)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("{");

        List<string> langs = new List<string>(data.Keys);
        langs.Sort();

        for (int i = 0; i < langs.Count; i++)
        {
            string lang = langs[i];
            sb.AppendLine($"    \"{EscapeJson(lang)}\": {{");

            List<string> keys = new List<string>(data[lang].Keys);
            keys.Sort();

            for (int j = 0; j < keys.Count; j++)
            {
                string key = keys[j];
                string value = data[lang][key];
                string comma = j < keys.Count - 1 ? "," : "";
                sb.AppendLine($"        \"{EscapeJson(key)}\": \"{EscapeJson(value)}\"{comma}");
            }

            string langComma = i < langs.Count - 1 ? "," : "";
            sb.AppendLine($"    }}{langComma}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Escapa caracteres especiales para insertar un string dentro de JSON.
    /// Maneja: backslash, comillas, newlines, tabs, carriage return.
    /// </summary>
    public static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // =====================================================================
    // Canvas: Normalizacion de nombres y generacion de IDs unicos
    // =====================================================================

    /// <summary>
    /// Normaliza un nombre de GameObject para usarlo como ID o segmento de clave.
    /// Quita sufijos de Unity (Clone, (1)), pasa a minusculas, reemplaza separadores
    /// por underscore, y elimina caracteres no alfanumericos.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(Clone\)\s*$", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)\s*$", "");
        name = name.ToLower();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[\s\-\.\,\;\:\+\=]+", "_");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-z0-9_]", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"_+", "_");
        name = name.Trim('_');
        return name;
    }

    /// <summary>
    /// Construye una ruta de jerarquia determinista para ordenar objetos.
    /// Los hermanos con el mismo nombre incluyen su indice de aparicion.
    /// </summary>
    public static string GetStableHierarchyPath(Transform root, Transform child)
    {
        if (child == null) return "";

        List<string> parts = new List<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            int occurrence = GetSameNameSiblingOccurrence(current);
            string name = current.name ?? "";
            parts.Insert(0, name.Length + ":" + name + "[" + occurrence + "]");
            current = current.parent;
        }

        string scenePath = child.gameObject.scene.path;
        return (scenePath ?? "") + "/" + string.Join("/", parts);
    }

    /// <summary>
    /// Obtiene la posicion de un componente entre los componentes del mismo
    /// tipo en el GameObject.
    /// </summary>
    public static int GetStableComponentOrder(Component component)
    {
        if (component == null) return 0;
        Component[] sameType = component.GetComponents(component.GetType());
        for (int i = 0; i < sameType.Length; i++)
        {
            if (sameType[i] == component) return i;
        }
        return 0;
    }

    /// <summary>
    /// Compara componentes por escena, jerarquia, tipo y orden local.
    /// </summary>
    public static int CompareStableComponents(Component a, Component b)
    {
        int comparison = string.Compare(
            GetStableHierarchyPath(null, a != null ? a.transform : null),
            GetStableHierarchyPath(null, b != null ? b.transform : null),
            System.StringComparison.Ordinal);
        if (comparison != 0) return comparison;

        comparison = string.Compare(
            a != null ? a.GetType().FullName : "",
            b != null ? b.GetType().FullName : "",
            System.StringComparison.Ordinal);
        if (comparison != 0) return comparison;

        return GetStableComponentOrder(a).CompareTo(
            GetStableComponentOrder(b));
    }

    /// <summary>
    /// Cuenta todas las referencias de una clave entre los Localizer de la escena.
    /// Incluye CanvasLocalizer, InteractionLocalizer y TextLocalizer.
    /// </summary>
    public static int CountSceneTranslationKeyReferences(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        int count = 0;
        CanvasLocalizer[] canvasLocalizers =
            Object.FindObjectsByType<CanvasLocalizer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvasLocalizers.Length; i++)
        {
            SerializedObject so = new SerializedObject(canvasLocalizers[i]);
            count += CountStringArrayValue(so.FindProperty("tmpKeys"), key);
            count += CountStringArrayValue(so.FindProperty("legacyKeys"), key);
        }

        InteractionLocalizer[] interactionLocalizers =
            Object.FindObjectsByType<InteractionLocalizer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < interactionLocalizers.Length; i++)
        {
            SerializedObject so =
                new SerializedObject(interactionLocalizers[i]);
            count += CountStringArrayValue(
                so.FindProperty("interactKeys"), key);
            count += CountStringArrayValue(
                so.FindProperty("pickupInteractionKeys"), key);
            count += CountStringArrayValue(
                so.FindProperty("pickupUseKeys"), key);
        }

        TextLocalizer[] textLocalizers =
            Object.FindObjectsByType<TextLocalizer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < textLocalizers.Length; i++)
        {
            if (textLocalizers[i].GetTranslationKey() == key) count++;
        }
        return count;
    }

    private static int CountStringArrayValue(
        SerializedProperty property, string value)
    {
        if (property == null) return 0;

        int count = 0;
        for (int i = 0; i < property.arraySize; i++)
        {
            if (property.GetArrayElementAtIndex(i).stringValue == value)
                count++;
        }
        return count;
    }

    private static int GetSameNameSiblingOccurrence(Transform target)
    {
        int occurrence = 0;
        if (target.parent != null)
        {
            for (int i = 0; i < target.GetSiblingIndex(); i++)
            {
                Transform sibling = target.parent.GetChild(i);
                if (string.Equals(
                    sibling.name, target.name, System.StringComparison.Ordinal))
                {
                    occurrence++;
                }
            }
            return occurrence;
        }

        GameObject[] roots = target.gameObject.scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].transform == target) break;
            if (string.Equals(
                roots[i].name, target.name, System.StringComparison.Ordinal))
            {
                occurrence++;
            }
        }
        return occurrence;
    }

    /// <summary>
    /// Genera un canvasId unico basado en el nombre del GameObject.
    /// Verifica todos los CanvasLocalizer de la escena para evitar colisiones.
    /// </summary>
    public static string GenerateUniqueCanvasId(string goName, CanvasLocalizer self)
    {
        string baseName = NormalizeName(goName);
        if (string.IsNullOrEmpty(baseName)) baseName = "canvas";

        List<Canvas> matchingCanvases = new List<Canvas>();
        Canvas[] allCanvases = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allCanvases.Length; i++)
        {
            if (NormalizeName(allCanvases[i].gameObject.name) == baseName)
                matchingCanvases.Add(allCanvases[i]);
        }
        matchingCanvases.Sort((a, b) => string.Compare(
            GetStableHierarchyPath(null, a.transform),
            GetStableHierarchyPath(null, b.transform),
            System.StringComparison.Ordinal));

        int deterministicIndex = 0;
        for (int i = 0; i < matchingCanvases.Count; i++)
        {
            if (matchingCanvases[i].gameObject == self.gameObject)
            {
                deterministicIndex = i;
                break;
            }
        }

        HashSet<string> usedIds = new HashSet<string>();
        CanvasLocalizer[] allLocalizers = Object.FindObjectsByType<CanvasLocalizer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allLocalizers.Length; i++)
        {
            if (allLocalizers[i] == self) continue;
            string otherId = allLocalizers[i].GetCanvasId();
            if (!string.IsNullOrEmpty(otherId))
                usedIds.Add(otherId);
        }

        int counter = deterministicIndex + 1;
        string finalId = counter == 1 ? baseName : baseName + "_" + counter;
        while (usedIds.Contains(finalId))
        {
            counter++;
            finalId = baseName + "_" + counter;
        }

        return finalId;
    }

    // =====================================================================
    // UdonSharp: Busqueda de UdonBehaviour
    // =====================================================================

    /// <summary>
    /// Busca el UdonBehaviour que respalda un UdonSharpBehaviour (proxy C#).
    /// Intenta tres metodos en cascada:
    ///   1. UdonSharpEditorUtility.GetBackingUdonBehaviour (oficial)
    ///   2. Propiedad serializada _udonSharpBackingUdonBehaviour
    ///   3. Primer UdonBehaviour del mismo GameObject (fallback)
    /// Retorna null si no se encuentra.
    /// </summary>
    public static UdonBehaviour FindUdonBehaviourFor(UdonSharpBehaviour proxy)
    {
        UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
        if (udon != null) return udon;

        SerializedObject so = new SerializedObject(proxy);
        SerializedProperty bp = so.FindProperty("_udonSharpBackingUdonBehaviour");
        if (bp != null && bp.objectReferenceValue != null)
        {
            udon = bp.objectReferenceValue as UdonBehaviour;
            if (udon != null) return udon;
        }

        UdonBehaviour[] udons = proxy.GetComponents<UdonBehaviour>();
        if (udons != null && udons.Length > 0) return udons[0];

        return null;
    }
}
