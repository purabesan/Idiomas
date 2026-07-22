using System;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace BenderDios.Idiomas
{
    /// <summary>
    /// Sistema de localizacion standalone para VRChat.
    /// Carga un JSON de traducciones, detecta el idioma del jugador automaticamente,
    /// y notifica a todos los TextLocalizer registrados cuando cambia el idioma.
    ///
    /// Uso:
    ///   1. Colocar este componente en un GameObject de la escena.
    ///   2. Asignar el TextAsset con el JSON de traducciones.
    ///   3. Registrar los TextLocalizer en el array 'localizers' (o usar el boton del Editor).
    ///
    /// Formato del JSON:
    ///   {
    ///     "en": { "clave": "valor", ... },
    ///     "es": { "clave": "valor", ... },
    ///     "ja": { "clave": "valor", ... }
    ///   }
    ///
    /// Independiente de YamaPlayer. No requiere Controller, UIController, ni ningun otro sistema.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LocalizationManager : UdonSharpBehaviour
{
    // =====================================================================
    // Configuracion (Inspector)
    // =====================================================================

    [Tooltip("Archivo JSON con las traducciones. Formato: { \"en\": { \"key\": \"value\" }, ... }")]
    [SerializeField] private TextAsset translationFile;

    [Tooltip("Idioma de fallback si el idioma del jugador no existe en el JSON. Normalmente 'en'.")]
    [SerializeField] private string fallbackLanguage = "en";

    [Tooltip("Todos los TextLocalizer de la escena. Usar el boton 'Auto-buscar' del Inspector.")]
    [SerializeField] private TextLocalizer[] localizers = new TextLocalizer[0];

    [Tooltip("CanvasLocalizer que gestionan canvas completos. Usar el boton 'Auto-buscar' del Inspector.")]
    [SerializeField] private CanvasLocalizer[] canvasLocalizers = new CanvasLocalizer[0];

    // =====================================================================
    // Condiciones de exclusion del Editor
    // =====================================================================

    [HideInInspector]
    // Estos objetos y todos sus descendientes se omiten durante la busqueda
    // y la creacion automatica de CanvasLocalizer.
    [SerializeField] private GameObject[] _excludedLocalizationRoots = new GameObject[0];

    [Tooltip("TMP_Dropdown para cambiar idioma. Opcional. Se configura automaticamente al crear el selector.")]
    [SerializeField] private TMPro.TMP_Dropdown _languageDropdown;

    [Tooltip("Codigos de idioma en el MISMO ORDEN que las opciones del dropdown.")]
    [SerializeField] private string[] _dropdownLanguageCodes = new string[0];

    [Tooltip("UdonSharpBehaviours que reciben '_OnLanguageChanged' cuando cambia el idioma.\n" +
             "Util para scripts externos que necesitan reaccionar al cambio (cambiar imagenes, etc).")]
    [SerializeField] private UdonSharpBehaviour[] _listeners = new UdonSharpBehaviour[0];

    // =====================================================================
    // Estado interno
    // =====================================================================

    private DataDictionary _translationData;
    private DataDictionary _currentLangDict;  // Cache del diccionario del idioma activo
    private DataDictionary _fallbackLangDict;  // Cache del diccionario del fallback
    private string _currentLanguage;
    private bool _initialized;
    private string[] _availableLanguages = new string[0];

    // =====================================================================
    // Inicializacion
    // =====================================================================

    private void Start()
    {
        Initialize();
        SyncDropdownToCurrentLanguage();
        ApplyToAll();
    }

    private void Initialize()
    {
        if (_initialized) return;

        // --- Cargar JSON ---
        if (!Utilities.IsValid(translationFile))
        {
            Debug.LogError("[LocalizationManager] No se asigno translationFile. Asigna un TextAsset JSON.");
            _initialized = true;
            return;
        }

        if (!VRCJson.TryDeserializeFromJson(translationFile.text, out DataToken data))
        {
            Debug.LogError("[LocalizationManager] Error al parsear el JSON de traducciones.");
            _initialized = true;
            return;
        }

        if (data.TokenType != TokenType.DataDictionary)
        {
            Debug.LogError("[LocalizationManager] El JSON de traducciones debe ser un objeto raiz {}.");
            _initialized = true;
            return;
        }

        _translationData = data.DataDictionary;

        // --- Extraer lista de idiomas disponibles ---
        DataList keys = _translationData.GetKeys();
        _availableLanguages = new string[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            _availableLanguages[i] = keys[i].String;
        }

        // --- Detectar idioma del jugador ---
        _currentLanguage = DetectLanguage();
        CacheLangDictionaries();

        _initialized = true;

        Debug.Log($"[LocalizationManager] Inicializado. Idioma: {_currentLanguage}, Idiomas disponibles: {_availableLanguages.Length}");
    }

    // =====================================================================
    // Deteccion automatica de idioma
    // =====================================================================

    /// <summary>
    /// Detecta el idioma del jugador usando la API de VRChat.
    /// Si el idioma no existe en el JSON, intenta variantes (es-CL -> es).
    /// Si no encuentra nada, usa zona horaria como fallback.
    /// </summary>
    private string DetectLanguage()
    {
        // 1. Obtener idioma de VRChat
        string vrcLang = VRCPlayerApi.GetCurrentLanguage();

        // 2. Verificar si existe directamente en el JSON
        if (!string.IsNullOrEmpty(vrcLang) && HasLanguage(vrcLang))
        {
            return vrcLang;
        }

        // 3. Intentar variantes del idioma
        //    VRChat puede devolver "es-CL" pero el JSON tiene "es", o viceversa
        if (!string.IsNullOrEmpty(vrcLang))
        {
            // Intentar sin region: "es-CL" -> "es"
            if (vrcLang.Contains("-"))
            {
                string baseLang = vrcLang.Substring(0, vrcLang.IndexOf('-'));
                if (HasLanguage(baseLang)) return baseLang;
            }

            // Intentar buscar cualquier variante que empiece con el mismo codigo base
            for (int i = 0; i < _availableLanguages.Length; i++)
            {
                if (_availableLanguages[i].StartsWith(vrcLang) ||
                    (vrcLang.Contains("-") && _availableLanguages[i].StartsWith(vrcLang.Substring(0, vrcLang.IndexOf('-')))))
                {
                    return _availableLanguages[i];
                }
            }
        }

        // 4. Fallback por zona horaria
        string tzLang = GetLanguageByTimeZone();
        if (HasLanguage(tzLang)) return tzLang;

        // 5. Fallback final
        if (HasLanguage(fallbackLanguage)) return fallbackLanguage;

        // 6. Si nada funciona, usar el primer idioma disponible
        if (_availableLanguages.Length > 0) return _availableLanguages[0];

        return "en";
    }

    /// <summary>
    /// Detecta idioma por zona horaria del sistema (fallback para cuando VRChat
    /// no devuelve un idioma valido o no existe en el JSON).
    ///
    /// Soporta dos formatos de ID:
    ///   - Windows: "Tokyo Standard Time", "Romance Standard Time", etc.
    ///   - IANA (Quest/Android/Linux): "Asia/Tokyo", "Europe/Madrid", etc.
    ///
    /// Cuando una zona horaria cubre multiples paises con idiomas diferentes
    /// (ej: "Romance Standard Time" = Espana + Francia + Italia + Belgica),
    /// se prefiere el idioma mas hablado en VRChat para esa zona,
    /// o se devuelve el fallback si la ambiguedad es muy alta.
    /// </summary>
    private string GetLanguageByTimeZone()
    {
        string tzId = TimeZoneInfo.Local.Id;

        // Intentar primero match exacto (cubre Windows y IANA)
        string result = MatchTimeZone(tzId);
        if (result != null) return result;

        // En algunos sistemas el ID puede contener el formato largo,
        // intentar match parcial con Contains para IANA
        result = MatchTimeZonePartial(tzId);
        if (result != null) return result;

        return "en";
    }

    /// <summary>
    /// Match exacto del ID de zona horaria.
    /// Retorna null si no hay match.
    /// </summary>
    private string MatchTimeZone(string tzId)
    {
        switch (tzId)
        {
            // === Japones (ja) ===
            case "Tokyo Standard Time":       // Windows
            case "Asia/Tokyo":                // IANA
                return "ja";

            // === Chino Tradicional (zh-TW) ===
            case "Taipei Standard Time":      // Windows
            case "Asia/Taipei":               // IANA
                return "zh-TW";

            // === Chino Simplificado (zh-CN) ===
            case "China Standard Time":       // Windows
            case "Asia/Shanghai":             // IANA
            case "Asia/Hong_Kong":            // IANA (Hong Kong usa simplificado mayormente)
                return "zh-CN";

            // === Coreano (ko) ===
            case "Korea Standard Time":       // Windows
            case "North Korea Standard Time": // Windows
            case "Asia/Seoul":                // IANA
            case "Asia/Pyongyang":            // IANA
                return "ko";

            // === Espanol (es) ===
            // NOTA: "Romance Standard Time" (Windows) cubre Espana, Francia, Italia, Belgica.
            // Es demasiado ambiguo — se omite para que caiga al fallback.
            // IANA: Espana
            case "Europe/Madrid":
            case "Atlantic/Canary":
                return "es";
            // IANA: Latinoamerica hispanohablante
            case "America/Mexico_City":
            case "America/Bogota":
            case "America/Lima":
            case "America/Santiago":
            case "America/Argentina/Buenos_Aires":
            case "America/Caracas":
            case "America/Guayaquil":
            case "America/Montevideo":
            case "America/Asuncion":
            case "America/La_Paz":
            case "America/Panama":
            case "America/Costa_Rica":
            case "America/El_Salvador":
            case "America/Guatemala":
            case "America/Tegucigalpa":
            case "America/Managua":
            // Windows: Latinoamerica
            case "Central America Standard Time":
            case "SA Pacific Standard Time":
            case "SA Western Standard Time":
            case "SA Eastern Standard Time":
            case "Mexico Standard Time":
            case "Central Standard Time (Mexico)":
            case "Mountain Standard Time (Mexico)":
            case "Pacific Standard Time (Mexico)":
            case "Argentina Standard Time":
            case "Venezuela Standard Time":
            case "Paraguay Standard Time":
            case "Montevideo Standard Time":
            case "Pacific SA Standard Time":
                return "es";

            // === Frances (fr) ===
            case "Europe/Paris":              // IANA
            case "Europe/Brussels":           // IANA (Belgica - mayormente frances)
            case "Africa/Casablanca":         // IANA (Marruecos - frances comun)
            case "America/Montreal":          // IANA (Quebec)
            case "Morocco Standard Time":     // Windows
                return "fr";

            // === Aleman (de) ===
            case "W. Europe Standard Time":   // Windows (Alemania/Paises Bajos/Austria/Suiza)
            case "Central Europe Standard Time": // Windows (Europa Central)
            case "Central European Standard Time": // Windows variante
            case "Europe/Berlin":             // IANA
            case "Europe/Vienna":             // IANA
            case "Europe/Zurich":             // IANA
            // NOTA: Europe/Amsterdam (Paises Bajos) se omite — hablan neerlandes, no aleman.
                return "de";

            // === Ruso (ru) ===
            case "Russian Standard Time":     // Windows
            case "Moscow Standard Time":      // Windows
            case "Russia Time Zone 3":        // Windows (Samara)
            case "Russia Time Zone 10":       // Windows (Magadan)
            case "Russia Time Zone 11":       // Windows (Kamchatka)
            case "Ekaterinburg Standard Time":// Windows
            case "N. Central Asia Standard Time": // Windows (Novosibirsk)
            case "North Asia Standard Time":  // Windows (Krasnoyarsk)
            case "North Asia East Standard Time": // Windows (Irkutsk)
            case "Yakutsk Standard Time":     // Windows
            case "Vladivostok Standard Time": // Windows
            case "Europe/Moscow":             // IANA
            case "Europe/Samara":             // IANA
            case "Asia/Yekaterinburg":        // IANA
            case "Asia/Novosibirsk":          // IANA
            case "Asia/Krasnoyarsk":          // IANA
            case "Asia/Irkutsk":              // IANA
            case "Asia/Yakutsk":              // IANA
            case "Asia/Vladivostok":          // IANA
            case "Asia/Kamchatka":            // IANA
                return "ru";

            // === Portugues (pt-BR) ===
            case "E. South America Standard Time": // Windows (Brasil)
            case "Bahia Standard Time":       // Windows (Brasil - Bahia)
            case "Tocantins Standard Time":   // Windows (Brasil - Tocantins)
            case "America/Sao_Paulo":         // IANA
            case "America/Rio_Branco":        // IANA
            case "America/Fortaleza":         // IANA
            case "America/Manaus":            // IANA
            case "America/Belem":             // IANA
            case "America/Recife":            // IANA
            case "Europe/Lisbon":             // IANA (Portugal)
            // NOTA: "GMT Standard Time" (Windows) cubre UK + Irlanda + Portugal.
            // Es demasiado ambiguo — se omite para que caiga al fallback "en".
                return "pt-BR";

            // === Catalan (ca) ===
            // No tiene zona horaria propia, comparte con Espana.
            // Solo se activa por IANA si estamos en Cataluna (no hay forma real de distinguir).

            default:
                return null; // Sin match exacto
        }
    }

    /// <summary>
    /// Match parcial para IDs IANA que no estan en la lista exacta.
    /// Usa el prefijo del continente/region para adivinar.
    /// Retorna null si no hay match.
    /// </summary>
    private string MatchTimeZonePartial(string tzId)
    {
        // Asia
        if (tzId.StartsWith("Asia/Tokyo") || tzId.StartsWith("Japan"))
            return "ja";
        if (tzId.StartsWith("Asia/Seoul") || tzId.StartsWith("ROK"))
            return "ko";
        if (tzId.StartsWith("Asia/Shanghai") || tzId.StartsWith("PRC") ||
            tzId.StartsWith("Asia/Chongqing") || tzId.StartsWith("Asia/Harbin"))
            return "zh-CN";
        if (tzId.StartsWith("Asia/Taipei") || tzId.StartsWith("ROC"))
            return "zh-TW";

        // Rusia (muchas zonas)
        if (tzId.StartsWith("Europe/Moscow") || tzId.StartsWith("Europe/Samara") ||
            tzId.StartsWith("Asia/Yekaterinburg") || tzId.StartsWith("Asia/Novosibirsk") ||
            tzId.StartsWith("Asia/Omsk") || tzId.StartsWith("Asia/Krasnoyarsk") ||
            tzId.StartsWith("Asia/Irkutsk"))
            return "ru";

        // America Latina - Espanol
        if (tzId.StartsWith("America/Mexico") || tzId.StartsWith("America/Bogota") ||
            tzId.StartsWith("America/Lima") || tzId.StartsWith("America/Santiago") ||
            tzId.StartsWith("America/Argentina") || tzId.StartsWith("America/Caracas"))
            return "es";

        // Brasil
        if (tzId.StartsWith("America/Sao_Paulo") || tzId.StartsWith("America/Fortaleza") ||
            tzId.StartsWith("America/Manaus") || tzId.StartsWith("America/Recife") ||
            tzId.StartsWith("Brazil"))
            return "pt-BR";

        return null;
    }

    // =====================================================================
    // Cambio de idioma
    // =====================================================================

    /// <summary>
    /// Cambia el idioma activo. Si el idioma no existe, no hace nada.
    /// Notifica a todos los TextLocalizer registrados.
    /// </summary>
    public void SetLanguage(string language)
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(_translationData)) return;

        if (string.IsNullOrEmpty(language))
        {
            // Auto-detectar
            language = DetectLanguage();
        }

        // Verificar que el idioma existe (con variantes)
        string resolved = ResolveLanguage(language);
        if (string.IsNullOrEmpty(resolved))
        {
            Debug.LogWarning($"[LocalizationManager] Idioma '{language}' no encontrado. Disponibles: {string.Join(", ", _availableLanguages)}");
            return;
        }

        if (_currentLanguage == resolved) return; // Sin cambio

        _currentLanguage = resolved;
        CacheLangDictionaries();
        SyncDropdownToCurrentLanguage();

        Debug.Log($"[LocalizationManager] Idioma cambiado a: {resolved}");
        ApplyToAll();
        NotifyListeners();
    }

    /// <summary>
    /// Llamado automaticamente por VRChat cuando el jugador cambia su idioma
    /// en la configuracion de VRChat mientras esta en el mundo.
    /// Re-ejecuta la deteccion de idioma y aplica los cambios.
    /// </summary>
    public override void OnLanguageChanged(string language)
    {
        if (!_initialized) return;

        string resolved = ResolveLanguage(language);
        if (string.IsNullOrEmpty(resolved))
        {
            // Si el nuevo idioma no esta en el JSON, re-detectar con cascada completa
            resolved = DetectLanguage();
        }

        if (_currentLanguage == resolved) return;

        _currentLanguage = resolved;
        CacheLangDictionaries();
        SyncDropdownToCurrentLanguage();

        Debug.Log($"[LocalizationManager] Idioma cambiado por VRChat a: {resolved}");
        ApplyToAll();
        NotifyListeners();
    }

    /// <summary>
    /// Resuelve un codigo de idioma a uno que exista en el JSON.
    /// Maneja variantes como "es-CL" -> "es".
    /// </summary>
    private string ResolveLanguage(string language)
    {
        // Directo
        if (HasLanguage(language)) return language;

        // Sin region
        if (language.Contains("-"))
        {
            string baseLang = language.Substring(0, language.IndexOf('-'));
            if (HasLanguage(baseLang)) return baseLang;
        }

        // Buscar variante
        for (int i = 0; i < _availableLanguages.Length; i++)
        {
            if (_availableLanguages[i].StartsWith(language)) return _availableLanguages[i];
        }

        return null;
    }

    // =====================================================================
    // Dropdown de idioma
    // =====================================================================

    /// <summary>
    /// Llamado por TMP_Dropdown.OnValueChanged via SendCustomEvent("OnLanguageDropdownChanged").
    /// Lee el indice seleccionado y cambia el idioma.
    /// </summary>
    public void OnLanguageDropdownChanged()
    {
        if (!Utilities.IsValid(_languageDropdown)) return;
        if (_dropdownLanguageCodes == null) return;

        int index = _languageDropdown.value;
        if (index < 0 || index >= _dropdownLanguageCodes.Length) return;

        string code = _dropdownLanguageCodes[index];
        if (string.IsNullOrEmpty(code))
        {
            SetLanguage(null); // Auto-detectar
        }
        else
        {
            SetLanguage(code);
        }
    }

    /// <summary>
    /// Sincroniza el dropdown para que muestre el idioma actual.
    /// Se llama automaticamente en Start() si hay dropdown asignado.
    /// </summary>
    private void SyncDropdownToCurrentLanguage()
    {
        if (!Utilities.IsValid(_languageDropdown)) return;
        if (_dropdownLanguageCodes == null) return;

        for (int i = 0; i < _dropdownLanguageCodes.Length; i++)
        {
            if (_dropdownLanguageCodes[i] == _currentLanguage)
            {
                _languageDropdown.SetValueWithoutNotify(i);
                return;
            }
        }
    }

    // =====================================================================
    // Metodos de conveniencia para botones de UI
    // (SendCustomEvent solo acepta metodos sin parametros)
    // =====================================================================

    public void SetLanguageAuto() => SetLanguage(null);
    public void SetLanguageJapanese() => SetLanguage("ja");
    public void SetLanguageEnglish() => SetLanguage("en");
    public void SetLanguageSpanish() => SetLanguage("es");
    public void SetLanguageKorean() => SetLanguage("ko");
    public void SetLanguageChineseSimplified() => SetLanguage("zh-CN");
    public void SetLanguageChineseTraditional() => SetLanguage("zh-TW");
    public void SetLanguageRussian() => SetLanguage("ru");
    public void SetLanguagePortuguese() => SetLanguage("pt-BR");
    public void SetLanguageCatalan() => SetLanguage("ca");
    public void SetLanguageFrench() => SetLanguage("fr");
    public void SetLanguageGerman() => SetLanguage("de");

    // =====================================================================
    // Obtencion de traducciones
    // =====================================================================

    /// <summary>
    /// Obtiene la traduccion de una clave en el idioma actual.
    /// Si no existe en el idioma actual, busca en el fallback (normalmente ingles).
    /// Si tampoco existe en el fallback, devuelve la propia clave entre corchetes: [clave].
    /// Usa cache interno del diccionario del idioma activo para rendimiento.
    /// </summary>
    public string GetValue(string key)
    {
        if (!_initialized) Initialize();
        if (!Utilities.IsValid(_translationData)) return $"[{key}]";
        if (string.IsNullOrEmpty(key)) return string.Empty;

        // 1. Buscar en cache del idioma actual (rapido, sin lookup en _translationData)
        if (Utilities.IsValid(_currentLangDict))
        {
            if (_currentLangDict.TryGetValue(key, out DataToken valueToken))
                return valueToken.String;
        }

        // 2. Buscar en cache del fallback
        if (Utilities.IsValid(_fallbackLangDict) && _currentLanguage != fallbackLanguage)
        {
            if (_fallbackLangDict.TryGetValue(key, out DataToken valueToken))
                return valueToken.String;
        }

        // 3. Buscar en cualquier idioma que tenga la clave (ultimo recurso, sin cache)
        for (int i = 0; i < _availableLanguages.Length; i++)
        {
            if (_availableLanguages[i] == _currentLanguage) continue;
            if (_availableLanguages[i] == fallbackLanguage) continue;
            string result = GetValueForLanguage(_availableLanguages[i], key);
            if (!string.IsNullOrEmpty(result)) return result;
        }

        // 4. No se encontro en ningun idioma
        return $"[{key}]";
    }

    /// <summary>
    /// Obtiene la traduccion correcta segun la cantidad (pluralizacion).
    /// Busca claves con sufijos: {key}_zero, {key}_one, {key}_other.
    ///
    /// Convencion:
    ///   - {key}_zero:  cuando count == 0 (opcional, cae a _other si no existe)
    ///   - {key}_one:   cuando count == 1
    ///   - {key}_other: para todo lo demas (2, 3, 100, etc.)
    ///   - Si no existen las variantes, usa {key} directamente.
    ///
    /// Ejemplo en JSON:
    ///   "players_zero": "Sin jugadores"
    ///   "players_one": "1 jugador"
    ///   "players_other": "{n} jugadores"
    ///
    /// En runtime: GetPluralValue("players", 5) -> "5 jugadores"
    /// El {n} se reemplaza automaticamente por el numero.
    /// </summary>
    public string GetPluralValue(string key, int count)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string pluralKey;
        if (count == 0)
        {
            // Intentar _zero, si no existe usar _other
            pluralKey = key + "_zero";
            if (HasKey(pluralKey))
            {
                return GetValue(pluralKey).Replace("{n}", count.ToString());
            }
            pluralKey = key + "_other";
        }
        else if (count == 1)
        {
            pluralKey = key + "_one";
        }
        else
        {
            pluralKey = key + "_other";
        }

        string result;

        // Si la variante plural existe, usarla; si no, intentar la clave base
        if (HasKey(pluralKey))
        {
            result = GetValue(pluralKey);
        }
        else
        {
            result = GetValue(key);
        }

        return result.Replace("{n}", count.ToString());
    }

    /// <summary>
    /// Busca una clave en un idioma especifico (sin cache, para busqueda en cascada).
    /// </summary>
    private string GetValueForLanguage(string language, string key)
    {
        if (!Utilities.IsValid(_translationData)) return null;
        if (string.IsNullOrEmpty(language)) return null;

        if (_translationData.TryGetValue(language, out DataToken langToken))
        {
            if (langToken.TokenType == TokenType.DataDictionary)
            {
                if (langToken.DataDictionary.TryGetValue(key, out DataToken valueToken))
                {
                    return valueToken.String;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Cachea los diccionarios del idioma actual y fallback para evitar lookups repetidos.
    /// Se llama en Initialize() y en SetLanguage().
    /// </summary>
    private void CacheLangDictionaries()
    {
        _currentLangDict = null;
        _fallbackLangDict = null;

        if (!Utilities.IsValid(_translationData)) return;

        // Cache idioma actual
        if (!string.IsNullOrEmpty(_currentLanguage) &&
            _translationData.TryGetValue(_currentLanguage, out DataToken currentToken) &&
            currentToken.TokenType == TokenType.DataDictionary)
        {
            _currentLangDict = currentToken.DataDictionary;
        }

        // Cache fallback
        if (!string.IsNullOrEmpty(fallbackLanguage) && fallbackLanguage != _currentLanguage &&
            _translationData.TryGetValue(fallbackLanguage, out DataToken fallbackToken) &&
            fallbackToken.TokenType == TokenType.DataDictionary)
        {
            _fallbackLangDict = fallbackToken.DataDictionary;
        }
    }

    // =====================================================================
    // Notificacion a TextLocalizers
    // =====================================================================

    /// <summary>
    /// Recorre todos los TextLocalizer y CanvasLocalizer registrados y les ordena actualizarse.
    /// </summary>
    public void ApplyToAll()
    {
        // Actualizar TextLocalizers individuales
        if (localizers != null)
        {
            for (int i = 0; i < localizers.Length; i++)
            {
                if (Utilities.IsValid(localizers[i]))
                {
                    localizers[i].UpdateText();
                }
            }
        }

        // Actualizar CanvasLocalizers (canvas completos)
        if (canvasLocalizers != null)
        {
            for (int i = 0; i < canvasLocalizers.Length; i++)
            {
                if (Utilities.IsValid(canvasLocalizers[i]))
                {
                    canvasLocalizers[i].UpdateAllTexts();
                }
            }
        }
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    /// <summary>
    /// Verifica si una clave de traduccion existe en el idioma actual o en el fallback.
    /// Usa los diccionarios cacheados para rendimiento.
    /// </summary>
    private bool HasKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (Utilities.IsValid(_currentLangDict) && _currentLangDict.ContainsKey(key))
            return true;

        if (Utilities.IsValid(_fallbackLangDict) && _fallbackLangDict.ContainsKey(key))
            return true;

        // Buscar en otros idiomas (sin cache, ultimo recurso)
        for (int i = 0; i < _availableLanguages.Length; i++)
        {
            if (_availableLanguages[i] == _currentLanguage) continue;
            if (_availableLanguages[i] == fallbackLanguage) continue;

            if (Utilities.IsValid(_translationData) &&
                _translationData.TryGetValue(_availableLanguages[i], out DataToken langToken) &&
                langToken.TokenType == TokenType.DataDictionary &&
                langToken.DataDictionary.ContainsKey(key))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Verifica si un idioma existe en el JSON de traducciones.</summary>
    public bool HasLanguage(string language)
    {
        if (!Utilities.IsValid(_translationData)) return false;
        if (string.IsNullOrEmpty(language)) return false;
        return _translationData.ContainsKey(language);
    }

    /// <summary>Devuelve el idioma activo actualmente.</summary>
    public string GetCurrentLanguage()
    {
        return _currentLanguage;
    }

    /// <summary>Devuelve el numero de idiomas disponibles en el JSON.</summary>
    public int GetLanguageCount()
    {
        return _availableLanguages != null ? _availableLanguages.Length : 0;
    }

    /// <summary>True cuando el sistema esta listo para usar.</summary>
    public bool IsReady()
    {
        return _initialized && Utilities.IsValid(_translationData);
    }

    /// <summary>Devuelve un array con los codigos de idioma disponibles.</summary>
    public string[] GetAvailableLanguages()
    {
        return _availableLanguages;
    }

    // =====================================================================
    // Listeners externos (mejora #8)
    // =====================================================================

    /// <summary>
    /// Notifica a todos los listeners externos que el idioma cambio.
    /// Los listeners reciben SendCustomEvent("_OnLanguageChanged").
    /// </summary>
    private void NotifyListeners()
    {
        if (_listeners == null) return;
        for (int i = 0; i < _listeners.Length; i++)
        {
            if (Utilities.IsValid(_listeners[i]))
            {
                _listeners[i].SendCustomEvent("_OnLanguageChanged");
            }
        }
    }

    /// <summary>
    /// Registra un listener externo en runtime.
    /// El listener recibira SendCustomEvent("_OnLanguageChanged") cuando cambie el idioma.
    /// Debe tener un metodo publico "_OnLanguageChanged()" para recibir la notificacion.
    /// Nota: Crea un nuevo array cada vez. Usar con moderacion.
    /// </summary>
    public void RegisterListener(UdonSharpBehaviour listener)
    {
        if (!Utilities.IsValid(listener)) return;

        // Verificar que no este ya registrado
        if (_listeners != null)
        {
            for (int i = 0; i < _listeners.Length; i++)
            {
                if (_listeners[i] == listener) return;
            }
        }

        int oldLen = _listeners != null ? _listeners.Length : 0;
        UdonSharpBehaviour[] newArr = new UdonSharpBehaviour[oldLen + 1];
        for (int i = 0; i < oldLen; i++)
        {
            newArr[i] = _listeners[i];
        }
        newArr[oldLen] = listener;
        _listeners = newArr;
    }

    // =====================================================================
    // Registro de localizers
    // =====================================================================

    /// <summary>
    /// Registra un TextLocalizer adicional en runtime.
    /// Util para objetos creados dinamicamente.
    /// Nota: En UdonSharp no se puede redimensionar arrays facilmente,
    /// asi que este metodo crea un nuevo array cada vez. Usar con moderacion.
    /// </summary>
    public void RegisterLocalizer(TextLocalizer localizer)
    {
        if (!Utilities.IsValid(localizer)) return;

        // Verificar que no este ya registrado
        if (localizers != null)
        {
            for (int i = 0; i < localizers.Length; i++)
            {
                if (localizers[i] == localizer) return;
            }
        }

        // Crear nuevo array con un espacio mas
        int oldLen = localizers != null ? localizers.Length : 0;
        TextLocalizer[] newArr = new TextLocalizer[oldLen + 1];
        for (int i = 0; i < oldLen; i++)
        {
            newArr[i] = localizers[i];
        }
        newArr[oldLen] = localizer;
        localizers = newArr;

        // Aplicar idioma actual al nuevo localizer
        localizer.UpdateText();
    }

    /// <summary>
    /// Registra un CanvasLocalizer adicional en runtime.
    /// Llamado automaticamente por CanvasLocalizer.Start().
    /// Nota: Crea un nuevo array cada vez. Usar con moderacion.
    /// </summary>
    public void RegisterCanvasLocalizer(CanvasLocalizer canvasLocalizer)
    {
        if (!Utilities.IsValid(canvasLocalizer)) return;

        // Verificar que no este ya registrado
        if (canvasLocalizers != null)
        {
            for (int i = 0; i < canvasLocalizers.Length; i++)
            {
                if (canvasLocalizers[i] == canvasLocalizer) return;
            }
        }

        // Crear nuevo array con un espacio mas
        int oldLen = canvasLocalizers != null ? canvasLocalizers.Length : 0;
        CanvasLocalizer[] newArr = new CanvasLocalizer[oldLen + 1];
        for (int i = 0; i < oldLen; i++)
        {
            newArr[i] = canvasLocalizers[i];
        }
        newArr[oldLen] = canvasLocalizer;
        canvasLocalizers = newArr;

        // Aplicar idioma actual al nuevo canvas localizer
        canvasLocalizer.UpdateAllTexts();
    }
}
}
