using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;

namespace BenderDios.Idiomas
{
/// <summary>
/// Localiza automaticamente TODOS los textos de un Canvas completo.
/// Se coloca en el GameObject raiz del Canvas (o cualquier nivel alto de la jerarquia).
///
/// A diferencia de TextLocalizer (que requiere un componente por cada texto),
/// CanvasLocalizer gestiona todos los textos hijos desde un unico punto.
///
/// Flujo de trabajo en el Editor:
///   1. Colocar este componente en el raiz del Canvas.
///   2. Asignar el LocalizationManager de la escena.
///   3. Configurar 'canvasId' (ej: "settings") y 'baseLanguage' (ej: "es").
///   4. En el Inspector, clic en "Escanear Canvas" para detectar todos los textos.
///   5. Revisar la tabla de resultados: ajustar claves, excluir textos dinamicos.
///   6. Clic en "Exportar al JSON y Aplicar":
///      - Los textos originales se guardan automaticamente en el JSON bajo el idioma base.
///      - Los arrays internos se llenan automaticamente.
///   7. No se necesita configuracion manual por cada texto.
///
/// En Runtime:
///   - Se registra automaticamente con el LocalizationManager en Start().
///   - UpdateAllTexts() se llama cuando cambia el idioma.
///
/// Compatible con TextMeshProUGUI (recomendado) y UnityEngine.UI.Text (legacy).
/// Ignora textos que ya tengan un TextLocalizer asignado (evita conflictos).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CanvasLocalizer : UdonSharpBehaviour
{
    // =====================================================================
    // Configuracion (Inspector)
    // =====================================================================

    [Header("Referencia al Manager")]
    [Tooltip("El LocalizationManager de la escena. Obligatorio.")]
    [SerializeField] private LocalizationManager manager;

    [Header("Configuracion del Canvas")]
    [Tooltip("Identificador unico del canvas. Se usa como prefijo para las claves de traduccion.\n" +
             "Ejemplos: 'settings', 'main_menu', 'hud', 'lobby'.")]
    [SerializeField] private string canvasId = "";

    [Tooltip("Idioma en el que estan escritos los textos originales del canvas.\n" +
             "Se usa al exportar al JSON para saber bajo que idioma guardar el texto.\n" +
             "Ejemplos: 'es', 'en', 'ja'.")]
    [SerializeField] private string baseLanguage = "en";

    // =====================================================================
    // Configuracion de exclusion automatica
    // =====================================================================

    [Header("Exclusion automatica del JSON")]
    // Las coincidencias se excluyen inicialmente, pero se pueden volver a incluir
    // manualmente desde los resultados del escaneo.
    [SerializeField] private string[] excludedKeywords = new string[0];

    // =====================================================================
    // Arrays de textos (llenados automaticamente por el Editor)
    // =====================================================================

    [Header("Textos TextMeshPro")]
    [SerializeField] private TextMeshProUGUI[] tmpTexts = new TextMeshProUGUI[0];
    [SerializeField] private string[] tmpKeys = new string[0];

    [Header("Textos Legacy UI")]
    [SerializeField] private Text[] legacyTexts = new Text[0];
    [SerializeField] private string[] legacyKeys = new string[0];

    // =====================================================================
    // Metadatos del Editor (no se usan en runtime, persisten entre escaneos)
    // =====================================================================

    [HideInInspector]
    [SerializeField] private GameObject[] _excludedObjects = new GameObject[0];

    // =====================================================================
    // Estado interno
    // =====================================================================

    private bool _initialized;

    // =====================================================================
    // Inicializacion
    // =====================================================================

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        if (!Utilities.IsValid(manager))
        {
            Debug.LogWarning($"[CanvasLocalizer] '{gameObject.name}': No se asigno LocalizationManager.");
            return;
        }

        // Registrarse con el manager para recibir notificaciones de cambio de idioma
        manager.RegisterCanvasLocalizer(this);
    }

    // =====================================================================
    // Actualizacion de textos
    // =====================================================================

    /// <summary>
    /// Actualiza todos los textos del canvas con las traducciones del idioma actual.
    /// Llamado automaticamente por el LocalizationManager cuando cambia el idioma.
    /// Tambien se puede llamar manualmente si es necesario.
    /// </summary>
    public void UpdateAllTexts()
    {
        if (!Utilities.IsValid(manager)) return;

        // --- Actualizar textos TextMeshPro ---
        if (tmpTexts != null && tmpKeys != null)
        {
            int len = tmpTexts.Length;
            if (tmpKeys.Length < len) len = tmpKeys.Length;

            for (int i = 0; i < len; i++)
            {
                if (Utilities.IsValid(tmpTexts[i]) && !string.IsNullOrEmpty(tmpKeys[i]))
                {
                    tmpTexts[i].text = manager.GetValue(tmpKeys[i]);
                }
            }
        }

        // --- Actualizar textos Legacy UI ---
        if (legacyTexts != null && legacyKeys != null)
        {
            int len = legacyTexts.Length;
            if (legacyKeys.Length < len) len = legacyKeys.Length;

            for (int i = 0; i < len; i++)
            {
                if (Utilities.IsValid(legacyTexts[i]) && !string.IsNullOrEmpty(legacyKeys[i]))
                {
                    legacyTexts[i].text = manager.GetValue(legacyKeys[i]);
                }
            }
        }
    }

    // =====================================================================
    // Propiedades publicas (para uso desde otros scripts y el Editor)
    // =====================================================================

    /// <summary>Devuelve el ID del canvas.</summary>
    public string GetCanvasId()
    {
        return canvasId;
    }

    /// <summary>Devuelve el idioma base configurado.</summary>
    public string GetBaseLanguage()
    {
        return baseLanguage;
    }

    /// <summary>Devuelve el LocalizationManager asignado.</summary>
    public LocalizationManager GetManager()
    {
        return manager;
    }

    /// <summary>Devuelve el numero total de textos gestionados.</summary>
    public int GetTextCount()
    {
        int count = 0;
        if (tmpTexts != null) count += tmpTexts.Length;
        if (legacyTexts != null) count += legacyTexts.Length;
        return count;
    }
}
}
