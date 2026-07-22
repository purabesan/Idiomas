# Idiomas - Sistema de Localizacion para VRChat

Sistema de localizacion standalone para mundos de VRChat usando UdonSharp.
Traduce automaticamente todos los textos de un Canvas a multiples idiomas.

**100% independiente.** No requiere YamaPlayer ni ningun otro sistema externo.
**Compatible con PC y Quest.**

📖 **Documentacion completa:** https://emerytheec.github.io/Idiomas-docs/
📦 **Booth.pm (gratis, .unitypackage):** https://bender-dios.booth.pm/items/8201435

---

## Instalacion

### Opcion A: VPM (Creator Companion) — recomendado

1. Agrega el repositorio VPM en Creator Companion:
   `https://emerytheec.github.io/vpm-listing/index.json`
2. Busca **Idiomas** en la lista de paquetes
3. Clic en **Add**

### Opcion B: Manual (.unitypackage)

Descarga el `.unitypackage` desde cualquiera de estas fuentes:

- **Booth.pm** (gratis, 0 JPY): https://bender-dios.booth.pm/items/8201435
- **GitHub Releases**: https://github.com/emerytheec/Idiomas/releases

Luego en Unity: `Assets > Import Package > Custom Package` y selecciona el archivo.

### Fuentes CJK (japones, coreano, chino, ruso)

El sistema soporta 11 idiomas incluyendo japones, chino, coreano y ruso.
Estos caracteres se renderizan automaticamente con las fuentes internas
del cliente de VRChat (Noto Sans CJK) en runtime.

**No necesitas configurar fuentes adicionales.**

Para que las fuentes de VRChat funcionen, la lista de **Fallback Font Assets**
en TMP Settings (`Edit > Project Settings > TextMeshPro Settings`) debe estar
**vacia**. Si hay fuentes custom ahi, VRChat las usa en vez de las suyas.

**Nota:** En el editor de Unity (Play mode local), los caracteres CJK pueden
aparecer como cuadrados. Esto es normal — al subir el mundo a VRChat se
renderizan correctamente.

### Agregar el prefab a tu escena

1. En el Project, ve a `Packages/com.benderdios.idiomas/Prefabs/` (instalacion VPM) o `Assets/Idiomas/Prefabs/` (instalacion manual)
2. Arrastra **`LocalizationManager`** a tu escena (Hierarchy)
3. Esto incluye el LocalizationManager + el selector de idioma (dropdown)
4. Posiciona el selector de idioma (canvas) donde quieras en tu mundo

### Conectar el dropdown (primera vez)

1. Selecciona el **LocalizationManager** en la Hierarchy
2. En el Inspector, abre la seccion **Selector de Idioma (Dropdown)**
3. Verifica que **Dropdown** esta asignado
4. Verifica que **Codigos de Idioma** tiene 11 elementos (uno por idioma)
5. Si aparece el boton amarillo **"Conectar Dropdown"**, haz clic en el
6. Si no aparece, ya esta conectado

---

## Traducir un Canvas

### Metodo rapido: CanvasLocalizer (un componente por canvas)

1. Selecciona el **GameObject raiz** de tu Canvas
2. **Add Component** → busca **CanvasLocalizer**
3. Configura:
   - **Manager**: arrastra el LocalizationManager de la escena
   - **ID del Canvas**: un nombre unico (ej: `settings`, `lobby`, `hud`)
   - **Idioma Base**: el idioma en que estan escritos los textos actuales (ej: `en`)
4. En el Inspector, clic en **"Escanear Canvas"**
   - Detecta automaticamente todos los textos (TextMeshPro y Text legacy)
   - Muestra una tabla con las claves generadas
   - Puedes excluir textos que no deben traducirse (numeros, iconos)
   - Puedes editar las claves manualmente
5. Clic en **"Exportar al JSON y Aplicar"**
   - Guarda los textos originales al archivo JSON bajo el idioma base
   - Si no existe archivo JSON, lo crea automaticamente
   - Llena los arrays del componente

### Configuracion Rapida (multiples canvas de una vez)

Si tienes muchos canvas sin localizar:

1. Selecciona el **LocalizationManager** en la Hierarchy
2. En el Inspector, abre **Buscar Canvas sin Localizar**
3. Clic en **"Escanear Escena"**
4. Selecciona el **Idioma Base** de tus textos actuales
5. Clic en **"Configuracion Rapida: Localizar Todo"**
   - Anade CanvasLocalizer a todos los canvas candidatos
   - Escanea textos y exporta al JSON automaticamente

### Metodo individual: TextLocalizer (un componente por texto)

Para textos sueltos que no estan en un canvas:

1. Selecciona el GameObject con Text o TextMeshProUGUI
2. **Add Component** → busca **TextLocalizer**
3. Asigna el **LocalizationManager**
4. Escribe la **clave de traduccion** (ej: `btn_start`)

---

## Traducir a otros idiomas

### Automatico: MyMemory API (gratis)

1. Selecciona el **LocalizationManager** en la Hierarchy
2. En el Inspector, abre **Traduccion** y clic en **"Auto-Traducir Idiomas Faltantes"**
3. Se abre una ventana:
   - Marca los idiomas destino
   - Clic en **"Traducir"**
   - Para cada clave, detecta automaticamente el idioma fuente
4. Revisa las traducciones en la vista previa
5. Clic en **"Guardar al JSON"**

API MyMemory: gratis, sin registro, 5000 caracteres por dia.
Lingva Translate se usa como fallback si MyMemory falla.
Las traducciones marcadas [TODO:xx] fallaron y necesitan traduccion manual.

### Manual

Edita directamente el archivo JSON de traducciones.
Formato:

```json
{
    "en": {
        "mi_clave": "English text"
    },
    "es": {
        "mi_clave": "Texto en espanol"
    },
    "ja": {
        "mi_clave": "日本語テキスト"
    }
}
```

### Exportar/Importar CSV

Menu: `Tools > Idiomas > Exportar-Importar CSV`

Permite exportar las traducciones a CSV para editarlas en Google Sheets o Excel,
y luego importarlas de vuelta al JSON. Util para colaborar con traductores.

---

## Idiomas soportados

| Codigo | Idioma | Caracteres especiales |
|--------|--------|----------------------|
| `en` | English | No |
| `es` | Espanol | No |
| `ja` | Japanese | Si (fuentes VRChat automaticas) |
| `ko` | Korean | Si (fuentes VRChat automaticas) |
| `zh-CN` | Chinese Simplified | Si (fuentes VRChat automaticas) |
| `zh-TW` | Chinese Traditional | Si (fuentes VRChat automaticas) |
| `ru` | Russian | Si (fuentes VRChat automaticas) |
| `pt-BR` | Portuguese | No |
| `fr` | French | No |
| `de` | German | No |
| `ca` | Catalan | No |

Puedes agregar cualquier idioma editando el JSON. El sistema detecta
automaticamente los idiomas disponibles.

---

## Componentes

### LocalizationManager
El cerebro del sistema. Solo necesitas **uno por escena**.

- Carga el JSON de traducciones
- Detecta automaticamente el idioma del jugador (VRChat API)
- Fallback inteligente: idioma del jugador → variantes → zona horaria → fallback
- Gestiona el dropdown de seleccion de idioma
- Notifica a todos los CanvasLocalizer y TextLocalizer cuando cambia el idioma

### CanvasLocalizer
Se coloca en el **raiz de un Canvas** y traduce todos los textos hijos.

- Escanea automaticamente TMP_Text y Text legacy
- Genera claves de traduccion desde la jerarquia de GameObjects
- Exporta textos al JSON automaticamente
- Un solo componente por canvas (no uno por texto)
- Gizmo en Scene View: muestra `[Idiomas:canvasId] N textos`

### TextLocalizer
Se coloca en **un solo texto** para traduccion individual.

- Para textos sueltos fuera de canvas
- Soporta parametros dinamicos: `{0}`, `{1}`, `{2}`
- Soporta prefijo, sufijo y rich text (`{t}` como placeholder)
- Se puede cambiar la clave en runtime

---

## Deteccion Automatica de Idioma

Al iniciar, el sistema detecta el idioma del jugador:

1. **API de VRChat**: `VRCPlayerApi.GetCurrentLanguage()`
2. **Variantes**: `es-CL` → busca `es` si no existe `es-CL`
3. **Zona horaria**: Tokyo → `ja`, Madrid → `es`, etc. (soporta Windows y IANA/Quest)
4. **Fallback**: usa el idioma por defecto (`en`)

---

## Herramientas del Editor

### Inspector del LocalizationManager
- **Condiciones de exclusion**: define palabras clave y GameObjects raiz que deben quedar fuera de la busqueda y la creacion automatica de CanvasLocalizer
- **Buscar Canvas sin Localizar**: escanea la escena y muestra candidatos
- **Configuracion Rapida**: localiza todos los canvas de una vez
- **Palabras clave de exclusion**: permite definir una lista comun que se copia a los nuevos CanvasLocalizer durante la configuracion rapida
- **Auto-Traducir**: traduce idiomas faltantes con MyMemory API
- **Vista Previa**: muestra traducciones sin entrar en Play Mode
- **Dropdown**: configuracion del selector de idioma
- **Listeners**: scripts externos que reaccionan al cambio de idioma

Los GameObjects excluidos y todos sus descendientes se omiten por completo durante la busqueda. Los canvas sin textos traducibles tampoco reciben un CanvasLocalizer. Un CanvasLocalizer existente puede seguir utilizandose sin agregar su GameObject a la lista de exclusion.

### Inspector del CanvasLocalizer
- **Escanear Canvas**: detecta todos los textos automaticamente
- **Tabla de resultados**: editar claves, excluir textos, detectar duplicados
- **Palabras clave de exclusion**: marca automaticamente como excluidos los textos que contengan alguna palabra configurada (coincidencia parcial, sin distinguir mayusculas y minusculas)
- **Inclusion manual**: un texto excluido por palabra clave permanece visible en los resultados y se puede volver a incluir antes de exportar
- **Exportar al JSON**: guarda textos y crea el archivo si no existe

Las palabras clave solo controlan el registro y la actualizacion desde el Canvas al JSON. No eliminan claves que ya existan en el archivo de traducciones.

---

## Estructura de Archivos

```
Idiomas/
├── Runtime/
│   ├── LocalizationManager.cs       # Cerebro del sistema
│   ├── CanvasLocalizer.cs           # Localizador de canvas completo
│   ├── TextLocalizer.cs             # Localizador de texto individual
│   └── Idiomas.Runtime.asmdef       # Assembly definition
├── Editor/
│   ├── LocalizationManagerEditor.cs # Inspector del Manager
│   ├── CanvasLocalizerEditor.cs     # Inspector del CanvasLocalizer
│   ├── CanvasLocalizerGizmo.cs      # Gizmo en Scene View
│   ├── AutoTranslateWindow.cs       # Ventana de auto-traduccion
│   ├── CsvExportImportWindow.cs     # Exportar/importar CSV
│   ├── IdiomasLanguages.cs          # Definiciones centralizadas de idiomas
│   ├── IdiomasEditorUtils.cs        # Utilidades compartidas
│   ├── IdiomasEditorStrings.cs      # Traducciones del propio inspector (11 idiomas)
│   ├── IdiomasPrefabCreator.cs      # Creador de demos
│   └── Idiomas.Editor.asmdef        # Assembly definition
├── Prefabs/
│   └── LocalizationManager.prefab   # Prefab listo para usar
├── package.json                     # Metadatos VPM
└── README.md
```

---

## API Publica (para otros scripts UdonSharp)

Namespace: `BenderDios.Idiomas`

### LocalizationManager

| Metodo | Descripcion |
|--------|-------------|
| `SetLanguage(string lang)` | Cambia el idioma activo. `null` = auto-detectar. |
| `GetValue(string key)` | Obtiene la traduccion de una clave. Fallback en cascada. |
| `GetPluralValue(string key, int count)` | Traduccion con pluralizacion (`_zero`, `_one`, `_other`). `{n}` se reemplaza por el numero. |
| `GetCurrentLanguage()` | Devuelve el codigo del idioma activo (ej: `"es"`). |
| `GetLanguageCount()` | Numero de idiomas disponibles. |
| `GetAvailableLanguages()` | Array de codigos de idioma. |
| `HasLanguage(string lang)` | `true` si el idioma existe en el JSON. |
| `IsReady()` | `true` cuando el sistema esta inicializado. |
| `RegisterListener(UdonSharpBehaviour)` | Registra un listener que recibe `_OnLanguageChanged` al cambiar idioma. |
| `RegisterLocalizer(TextLocalizer)` | Registra un TextLocalizer en runtime. |
| `RegisterCanvasLocalizer(CanvasLocalizer)` | Registra un CanvasLocalizer en runtime. |
| `OnLanguageDropdownChanged()` | Callback que el TMP_Dropdown dispara via `SendCustomEvent` al cambiar la seleccion. |

#### Metodos sin parametros (para `SendCustomEvent`)

Ideales para conectar botones de UI directamente, sin script intermedio.

| Metodo | Idioma |
|--------|--------|
| `SetLanguageAuto()` | Deteccion automatica (equivalente a `SetLanguage(null)`) |
| `SetLanguageEnglish()` | `en` — English |
| `SetLanguageSpanish()` | `es` — Espanol |
| `SetLanguageJapanese()` | `ja` — 日本語 |
| `SetLanguageKorean()` | `ko` — 한국어 |
| `SetLanguageChineseSimplified()` | `zh-CN` — 中文 (简体) |
| `SetLanguageChineseTraditional()` | `zh-TW` — 中文 (繁體) |
| `SetLanguageRussian()` | `ru` — Русский |
| `SetLanguagePortuguese()` | `pt-BR` — Portugues |
| `SetLanguageFrench()` | `fr` — Francais |
| `SetLanguageGerman()` | `de` — Deutsch |
| `SetLanguageCatalan()` | `ca` — Catala |

### TextLocalizer

| Metodo | Retorno | Descripcion |
|--------|---------|-------------|
| `UpdateText()` | `void` | Actualiza el texto con la traduccion actual. Se llama automaticamente. |
| `SetTranslationKey(string key)` | `void` | Cambia la clave de traduccion y actualiza el texto. |
| `GetTranslationKey()` | `string` | Devuelve la clave de traduccion actual. |
| `SetParams(string p0)` | `void` | Reemplaza `{0}` en la traduccion y actualiza. |
| `SetParams2(string p0, string p1)` | `void` | Reemplaza `{0}` y `{1}`. |
| `SetParams3(string p0, string p1, string p2)` | `void` | Reemplaza `{0}`, `{1}` y `{2}`. |
| `GetManager()` | `LocalizationManager` | Devuelve el manager asignado. |
| `SetManager(LocalizationManager m)` | `void` | Establece el manager manualmente. |

### CanvasLocalizer

| Metodo | Retorno | Descripcion |
|--------|---------|-------------|
| `UpdateAllTexts()` | `void` | Actualiza todos los textos del Canvas. Se llama automaticamente al cambiar idioma. |
| `GetCanvasId()` | `string` | Devuelve el ID del canvas. |
| `GetBaseLanguage()` | `string` | Devuelve el idioma base configurado. |
| `GetManager()` | `LocalizationManager` | Devuelve el manager asignado. |
| `GetTextCount()` | `int` | Numero total de textos gestionados (TMP + Legacy). |

### Pluralizacion

Convencion de claves en el JSON:

```json
{
    "en": {
        "players_zero": "No players",
        "players_one": "1 player",
        "players_other": "{n} players"
    },
    "es": {
        "players_zero": "Sin jugadores",
        "players_one": "1 jugador",
        "players_other": "{n} jugadores"
    }
}
```

En tu script: `manager.GetPluralValue("players", count)`

### Texto con parametros

En el JSON: `"welcome": "Hola {0}, tienes {1} mensajes"`

En tu script con TextLocalizer:
```
textLocalizer.SetParams2("Bender", "5");
// Resultado: "Hola Bender, tienes 5 mensajes"
```

### Listener de cambio de idioma

Para que tu script reaccione cuando el jugador cambia de idioma:

1. Agrega tu UdonSharpBehaviour al array "Listeners" del LocalizationManager.
2. Crea un metodo publico `_OnLanguageChanged()` en tu script.
3. Se llamara automaticamente cada vez que cambie el idioma.

---

## Persistencia de seleccion de idioma

Actualmente, si un jugador elige un idioma manualmente y vuelve a entrar al mundo,
el sistema auto-detecta de nuevo. Para persistir la eleccion:

1. Registra un listener con `manager.RegisterListener(this)`.
2. En `_OnLanguageChanged()`, guarda `manager.GetCurrentLanguage()` con tu sistema de persistencia.
3. En `Start()`, lee el idioma guardado y llama `manager.SetLanguage(idiomaGuardado)`.

VRChat ofrece PlayerData para persistencia entre sesiones.

---

## Compatibilidad

- Unity 2022.3.x (requerido por VRChat)
- VRChat SDK Worlds >= 3.8.1
- UdonSharp (incluido en VRChat SDK)
- TextMeshPro (incluido en Unity)
- Compatible con Quest y PC

---

## Licencia

MIT. Libre para uso personal y comercial. Creditos apreciados pero no requeridos.
