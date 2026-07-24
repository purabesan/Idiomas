# Idiomas - VRChat向けローカライズシステム

> [!IMPORTANT]
> この `develop` ブランチは、Idiomasの非公式な個人向け改修版です。
> 公式版には含まれていない追加機能があります。
>
> このForkのリリース：
> https://github.com/purabesan/Idiomas/releases

UdonSharpを使用した、VRChatワールド向けの独立したローカライズシステムです。
Canvas内のテキスト、およびUdonやPickupのInteraction Textを複数の言語へ翻訳します。

**完全に独立して動作します。** YamaPlayerなどの外部システムは必要ありません。  
**PC／Questの両方に対応しています。**

📖 **公式ドキュメント：** https://emerytheec.github.io/Idiomas-docs/  
📦 **Booth.pm（無料、.unitypackage）：** https://bender-dios.booth.pm/items/8201435
🇪🇸 **スペイン語版：** [README.md](README.md)

---

## インストール

### 方法A：VPM（Creator Companion）— 推奨

1. Creator Companionへ次のVPMリポジトリを追加します：  
   `https://emerytheec.github.io/vpm-listing/index.json`
2. パッケージ一覧から **Idiomas** を探します
3. **Add** をクリックします

### 方法B：手動（.unitypackage）

次のいずれかから `.unitypackage` をダウンロードします。

- **Booth.pm**（無料、0円）：https://bender-dios.booth.pm/items/8201435
- **GitHub Releases**：https://github.com/emerytheec/Idiomas/releases

Unityで `Assets > Import Package > Custom Package` を開き、ダウンロードしたファイルを選択します。

### CJKフォント（日本語、韓国語、中国語、ロシア語）

日本語、中国語、韓国語、ロシア語を含む11言語に対応しています。
これらの文字は、実行時にVRChatクライアント内蔵のNoto Sans CJKフォントを使用して
自動的に表示されます。

**追加のフォント設定は必要ありません。**

VRChatのフォントを使用するには、TMP Settings
（`Edit > Project Settings > TextMeshPro Settings`）の
**Fallback Font Assets** を空にする必要があります。
カスタムフォントが登録されている場合、VRChat内蔵フォントよりもそちらが優先されます。

**注意：** Unity EditorのローカルPlay Modeでは、CJK文字が四角形で表示される場合があります。
これは正常な挙動で、VRChatへアップロードすると正しく表示されます。

### シーンへPrefabを追加

1. Projectで `Packages/com.benderdios.idiomas/Prefabs/`（VPM）または
   `Assets/Idiomas/Prefabs/`（手動インストール）を開きます
2. **`LocalizationManager`** をHierarchyへドラッグします
3. PrefabにはLocalizationManagerと、言語選択用Dropdownが含まれています
4. 言語選択用Canvasをワールド内の任意の位置へ配置します

### Dropdownの接続（初回）

1. Hierarchyで **LocalizationManager** を選択します
2. Inspectorの **言語選択（Dropdown）** セクションを開きます
3. **Dropdown** が設定されていることを確認します
4. **言語コード** に11項目あることを確認します
5. 黄色の **「Dropdownを接続」** ボタンが表示されている場合はクリックします
6. 表示されていなければ接続済みです

---

## Canvasを翻訳

### 基本：CanvasLocalizer（Canvasごとに1コンポーネント）

1. Canvasの**ルートGameObject**を選択します
2. **Add Component** から **CanvasLocalizer** を追加します
3. 次の項目を設定します
   - **Manager**：シーン内のLocalizationManager
   - **Canvas ID**：一意の名前（例：`settings`、`lobby`、`hud`）
   - **ベース言語**：現在のテキストが書かれている言語（例：`ja`）
4. Inspectorで **「Canvasをスキャン」** をクリックします
   - TextMeshProとLegacy Textを自動検出します
   - 生成されたキーを一覧表示します
   - 数字やアイコンなど、翻訳しないテキストを除外できます
   - 翻訳キーを手動編集できます
5. **「JSONにエクスポートして適用」** をクリックします
   - 原文をベース言語としてJSONへ保存します
   - JSONが存在しない場合は自動作成します
   - CanvasLocalizerの配列へ対象を登録します

### クイックセットアップ

未ローカライズのCanvasやInteraction Textが複数ある場合：

1. Hierarchyで **LocalizationManager** を選択します
2. Interaction Textを翻訳する場合は **Interaction Text** を有効にします
3. 必要に応じて **除外条件** を設定します
4. **未ローカライズのテキストを検索** を開きます
5. **「シーンをスキャン」** をクリックします
6. **Canvas** と **InteractionText** の一覧をそれぞれ確認します
7. **ベース言語** を選択します
8. **「クイックセットアップ：すべてローカライズ」** をクリックします

クイックセットアップでは、次の処理が行われます。

- 翻訳対象テキストが存在するCanvasだけに `CanvasLocalizer` を追加
- 既存の `InteractionLocalizer` を再利用し、存在しない場合は作成
- 有効なテキストを各Localizerへ登録
- すべての翻訳キーを同じJSONファイルへ出力

処理後は、`CanvasLocalizer` と `InteractionLocalizer` のInspectorから、
対象テキストの含める／除外や翻訳キーを確認・編集できます。

### 個別設定：TextLocalizer（テキストごとに1コンポーネント）

Canvas外の個別テキストに使用します。

1. TextまたはTextMeshProUGUIを持つGameObjectを選択します
2. **Add Component** から **TextLocalizer** を追加します
3. **LocalizationManager** を設定します
4. 翻訳キー（例：`btn_start`）を入力します

### UdonとPickupのInteraction Text

次のプロパティを翻訳できます。

- `UdonSharpBehaviour.InteractionText`
- `VRCPickup.InteractionText`
- `VRCPickup.UseText`

クイックセットアップに含める場合：

1. **LocalizationManager** を選択します
2. **Interaction Text** を有効にします
3. デフォルト値の `Use` も翻訳する場合だけ **Default "Use"** を有効にします
4. **「シーンをスキャン」** を実行します
5. **クイックセットアップ** を実行します

初期値と完全に一致する `Use` は、不要な翻訳対象が大量に登録されることを
避けるため、通常はスキャン結果から除外されます。

個別の登録内容を確認・編集する場合：

1. `InteractionLocalizer` を選択します
2. **「Interaction Textをスキャン」** をクリックします
3. フィルターや一括ボタンを使って対象を含める／除外します
4. 必要に応じて翻訳キーを編集します
5. **「JSONにエクスポートして適用」** をクリックします

`InteractionLocalizer` は、翻訳対象となるUdonやPickupを一か所で管理します。
各 `UdonSharpBehaviour` や `VRCPickup` にLocalizerコンポーネントを追加することはありません。
言語が変更されると、登録されたInteraction Textも自動更新されます。

---

## 他言語への翻訳

### 自動翻訳：MyMemory API（無料）

1. Hierarchyで **LocalizationManager** を選択します
2. Inspectorの **翻訳** を開き、**「未翻訳言語を自動翻訳」** をクリックします
3. 表示されたウィンドウで：
   - 翻訳先の言語を選択します
   - **「翻訳」** をクリックします
   - キーごとに翻訳元言語が自動判定されます
4. プレビューで翻訳結果を確認します
5. **「JSONへ保存」** をクリックします

MyMemory APIは登録不要で、1日5,000文字まで無料です。
MyMemoryが失敗した場合はLingva Translateをフォールバックとして使用します。
`[TODO:xx]` が付いた翻訳は失敗しているため、手動での翻訳が必要です。

### 手動翻訳

翻訳JSONを直接編集します。

```json
{
    "en": {
        "mi_clave": "English text"
    },
    "es": {
        "mi_clave": "Texto en español"
    },
    "ja": {
        "mi_clave": "日本語テキスト"
    }
}
```

### CSVエクスポート／インポート

メニュー：`Tools > Idiomas > Exportar-Importar CSV`

翻訳データをCSVへエクスポートしてGoogle SheetsやExcelで編集し、
編集後のCSVをJSONへインポートできます。翻訳者との共同作業に利用できます。

### シーンコンポーネントのクリーンアップ

メニュー：`Tools > Idiomas > Cleanup Scene Components`

開かれているすべてのシーンに対して、次の処理を行います。

- 非アクティブなオブジェクトを含む `CanvasLocalizer` を削除
- `CanvasLocalizer` に関連する `UdonBehaviour` を削除
- `LocalizationManager` から対応する参照を削除
- `InteractionLocalizer` に登録されたInteraction Textをクリア

`InteractionLocalizer` のGameObjectとコンポーネント自体は削除されません。
再利用できるよう、Interaction Textの配列だけを空にします。

処理前には、削除する `CanvasLocalizer` の数、クリアするInteraction Textの件数、
対象GameObjectのHierarchyパスが表示されます。

翻訳JSON、`LocalizationManager`、Prefab、`InteractionLocalizer`、
`TextLocalizer` は削除されません。

---

## 対応言語

| コード | 言語 | 特殊文字 |
|--------|------|----------|
| `en` | English | なし |
| `es` | Español | なし |
| `ja` | Japanese | あり（VRChatフォントを自動使用） |
| `ko` | Korean | あり（VRChatフォントを自動使用） |
| `zh-CN` | Chinese Simplified | あり（VRChatフォントを自動使用） |
| `zh-TW` | Chinese Traditional | あり（VRChatフォントを自動使用） |
| `ru` | Russian | あり（VRChatフォントを自動使用） |
| `pt-BR` | Portuguese | なし |
| `fr` | French | なし |
| `de` | German | なし |
| `ca` | Catalan | なし |

JSONを編集すれば、任意の言語を追加できます。
利用可能な言語は自動検出されます。

---

## コンポーネント

### LocalizationManager

システム全体を管理します。シーンごとに**1つだけ**必要です。

- 翻訳JSONを読み込み
- VRChat APIからプレイヤーの言語を自動検出
- プレイヤー言語 → 言語バリエーション → タイムゾーン → フォールバックの順で判定
- 言語選択Dropdownを管理
- 言語変更時にCanvasLocalizer、TextLocalizer、InteractionLocalizerへ通知
- UdonSharpBehaviourとVRCPickupのInteraction Textを更新

### CanvasLocalizer

Canvasの**ルート**へ配置し、子階層のテキストを翻訳します。

- TMP_TextとLegacy Textを自動スキャン
- GameObjectのHierarchyから翻訳キーを生成
- テキストをJSONへ自動出力
- Canvasごとに1コンポーネント
- Scene Viewに `[Idiomas:canvasId] N texts` のGizmoを表示

### TextLocalizer

個別のテキストへ配置します。

- Canvas外のテキストに使用
- `{0}`、`{1}`、`{2}` の動的パラメーターに対応
- 接頭辞、接尾辞、リッチテキストに対応（`{t}` をプレースホルダーとして使用）
- 実行時に翻訳キーを変更可能

### InteractionLocalizer

UdonやPickupのInteraction Textを一か所で管理します。

- `UdonSharpBehaviour.InteractionText` に対応
- `VRCPickup.InteractionText` と `VRCPickup.UseText` に対応
- 各項目を個別に含める／除外可能
- フィルターによる検索と翻訳キーの編集に対応
- 他のLocalizerと同じJSONファイルへ出力
- クイックセットアップ時に既存コンポーネントがあれば再利用

---

## 言語の自動検出

起動時に、次の順序でプレイヤーの言語を判定します。

1. **VRChat API**：`VRCPlayerApi.GetCurrentLanguage()`
2. **言語バリエーション**：`es-CL` がない場合は `es` を検索
3. **タイムゾーン**：Tokyo → `ja`、Madrid → `es` など
   （WindowsおよびIANA／Questに対応）
4. **フォールバック**：デフォルト言語（`en`）を使用

---

## Editorツール

### LocalizationManager Inspector

- **Interaction Text**：UdonSharpBehaviourとVRCPickupのInteraction Textを対象に追加
- **除外条件**：検索とローカライズ処理から除外するキーワードとルートGameObjectを指定
- **未ローカライズのテキストを検索**：シーンをスキャンし、CanvasとInteractionTextを個別表示
- **クイックセットアップ**：CanvasとInteraction Textをまとめて設定
- **除外キーワード**：新しいCanvasLocalizerへコピーする共通キーワード一覧
- **自動翻訳**：MyMemory APIで未翻訳言語を翻訳
- **プレビュー**：Play Modeを使用せず翻訳を確認
- **Dropdown**：言語選択Dropdownを設定
- **Listeners**：言語変更を受け取る外部スクリプトを登録

除外GameObjectとそのすべての子階層は、検索およびローカライズ処理から完全に除外されます。
翻訳可能なテキストがないCanvasには `CanvasLocalizer` を追加しません。
既存の `CanvasLocalizer` は、GameObjectを除外一覧へ追加しなくても引き続き利用できます。

### CanvasLocalizer Inspector

- **Canvasをスキャン**：すべてのテキストを自動検出
- **結果一覧**：キーの編集、テキストの除外、重複の検出
- **除外キーワード**：指定したキーワードを含むテキストを自動的に除外
  （部分一致、大文字と小文字を区別しない）
- **手動で含める**：キーワードで除外されたテキストを、出力前に再度含めることが可能
- **JSONにエクスポート**：テキストを保存し、必要に応じてJSONを作成

除外キーワードが制御するのは、CanvasからJSONへの登録と更新だけです。
JSONにすでに存在するキーは削除しません。

同じベース言語で原文が完全に一致する場合、複数のテキストで正規キーを共有できます。
これによりJSONとCSVでは1件の翻訳として扱われ、自動翻訳でも同じテキストを
繰り返し処理することを防げます。

文脈によって異なる翻訳が必要な場合は、項目ごとに正規キーの共有を解除できます。
共有しているテキストが変更された場合は、他の使用箇所に影響しない独立したキーへ分離されます。

Canvas IDと新しい翻訳キーは、シーンのHierarchyに基づく一定の順序で生成されます。
Hierarchyと関連オブジェクトの構成が変わらなければ、Localizerを作り直しても
同じ翻訳キーが生成されます。

既存JSONのベース言語に登録済みのキーは予約済みとして扱われるため、
再スキャンによって別のテキストの翻訳が上書きされることを防げます。

---

## ファイル構成

```text
Idiomas/
├── Runtime/
│   ├── LocalizationManager.cs       # システム全体の管理
│   ├── CanvasLocalizer.cs           # Canvas全体のローカライズ
│   ├── TextLocalizer.cs             # 個別テキストのローカライズ
│   ├── InteractionLocalizer.cs      # Udon／PickupのInteraction Text
│   └── Idiomas.Runtime.asmdef       # Assembly definition
├── Editor/
│   ├── LocalizationManagerEditor.cs # Manager Inspector
│   ├── CanvasLocalizerEditor.cs     # CanvasLocalizer Inspector
│   ├── CanvasLocalizerGizmo.cs      # Scene View Gizmo
│   ├── AutoTranslateWindow.cs       # 自動翻訳ウィンドウ
│   ├── CsvExportImportWindow.cs     # CSV Export／Import
│   ├── IdiomasLanguages.cs          # 言語定義
│   ├── IdiomasEditorUtils.cs        # 共通ユーティリティ
│   ├── IdiomasEditorStrings.cs      # Inspectorの11言語表示
│   ├── IdiomasPrefabCreator.cs      # デモ作成
│   ├── IdiomasSceneCleanup.cs       # シーンコンポーネントのクリーンアップ
│   └── Idiomas.Editor.asmdef        # Assembly definition
├── Prefabs/
│   └── LocalizationManager.prefab   # 設定済みPrefab
├── package.json                     # VPMメタデータ
├── README.md                        # スペイン語README
└── README_jp.md                     # 日本語README
```

---

## 公開API（他のUdonSharpスクリプト向け）

Namespace：`BenderDios.Idiomas`

### LocalizationManager

| メソッド | 説明 |
|----------|------|
| `SetLanguage(string lang)` | 使用言語を変更します。`null` で自動検出します。 |
| `GetValue(string key)` | キーの翻訳をフォールバック付きで取得します。 |
| `GetPluralValue(string key, int count)` | `_zero`、`_one`、`_other` を使用して複数形を取得します。`{n}` は数値へ置換されます。 |
| `GetCurrentLanguage()` | 現在の言語コード（例：`"ja"`）を返します。 |
| `GetLanguageCount()` | 利用可能な言語数を返します。 |
| `GetAvailableLanguages()` | 言語コードの配列を返します。 |
| `HasLanguage(string lang)` | JSONに言語が存在する場合は `true` を返します。 |
| `IsReady()` | 初期化済みの場合は `true` を返します。 |
| `RegisterListener(UdonSharpBehaviour)` | 言語変更時に `_OnLanguageChanged` を受け取るListenerを登録します。 |
| `RegisterLocalizer(TextLocalizer)` | 実行時にTextLocalizerを登録します。 |
| `RegisterCanvasLocalizer(CanvasLocalizer)` | 実行時にCanvasLocalizerを登録します。 |
| `RegisterInteractionLocalizer(InteractionLocalizer)` | 実行時にInteractionLocalizerを登録します。 |
| `OnLanguageDropdownChanged()` | TMP_Dropdownが `SendCustomEvent` で呼び出すコールバックです。 |

#### 引数なしメソッド（`SendCustomEvent`向け）

UIボタンからスクリプトを介さず直接呼び出せます。

| メソッド | 言語 |
|----------|------|
| `SetLanguageAuto()` | 自動検出（`SetLanguage(null)` と同等） |
| `SetLanguageEnglish()` | `en` — English |
| `SetLanguageSpanish()` | `es` — Español |
| `SetLanguageJapanese()` | `ja` — 日本語 |
| `SetLanguageKorean()` | `ko` — 한국어 |
| `SetLanguageChineseSimplified()` | `zh-CN` — 中文（简体） |
| `SetLanguageChineseTraditional()` | `zh-TW` — 中文（繁體） |
| `SetLanguageRussian()` | `ru` — Русский |
| `SetLanguagePortuguese()` | `pt-BR` — Português |
| `SetLanguageFrench()` | `fr` — Français |
| `SetLanguageGerman()` | `de` — Deutsch |
| `SetLanguageCatalan()` | `ca` — Català |

### TextLocalizer

| メソッド | 戻り値 | 説明 |
|----------|--------|------|
| `UpdateText()` | `void` | 現在の翻訳でテキストを更新します。 |
| `SetTranslationKey(string key)` | `void` | 翻訳キーを変更してテキストを更新します。 |
| `GetTranslationKey()` | `string` | 現在の翻訳キーを返します。 |
| `SetParams(string p0)` | `void` | `{0}` を置換して更新します。 |
| `SetParams2(string p0, string p1)` | `void` | `{0}` と `{1}` を置換します。 |
| `SetParams3(string p0, string p1, string p2)` | `void` | `{0}`、`{1}`、`{2}` を置換します。 |
| `GetManager()` | `LocalizationManager` | 設定されているManagerを返します。 |
| `SetManager(LocalizationManager m)` | `void` | Managerを設定します。 |

### CanvasLocalizer

| メソッド | 戻り値 | 説明 |
|----------|--------|------|
| `UpdateAllTexts()` | `void` | Canvas内の全テキストを更新します。言語変更時に自動実行されます。 |
| `GetCanvasId()` | `string` | Canvas IDを返します。 |
| `GetBaseLanguage()` | `string` | 設定されたベース言語を返します。 |
| `GetManager()` | `LocalizationManager` | 設定されているManagerを返します。 |
| `GetTextCount()` | `int` | 管理しているテキスト数（TMP＋Legacy）を返します。 |

### 複数形

JSONでは次のキー規則を使用します。

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

スクリプトから `manager.GetPluralValue("players", count)` を呼び出します。

### パラメーター付きテキスト

JSON：

```json
"welcome": "Hola {0}, tienes {1} mensajes"
```

TextLocalizer：

```csharp
textLocalizer.SetParams2("Bender", "5");
// 結果："Hola Bender, tienes 5 mensajes"
```

### 言語変更Listener

1. UdonSharpBehaviourをLocalizationManagerの **Listeners** 配列へ追加します
2. スクリプトへpublicメソッド `_OnLanguageChanged()` を作成します
3. 言語が変更されるたびに自動的に呼び出されます

---

## 言語選択の永続化

現在、プレイヤーが手動で言語を選択しても、ワールドへ入り直すと再度自動検出されます。
選択を保持する場合：

1. `manager.RegisterListener(this)` でListenerを登録します
2. `_OnLanguageChanged()` 内で `manager.GetCurrentLanguage()` を保存します
3. `Start()` で保存済み言語を読み込み、`manager.SetLanguage(savedLanguage)` を呼び出します

VRChatでは、セッション間の永続化にPlayerDataを利用できます。

---

## 動作環境

- Unity 2022.3.22f1（VRChat指定バージョン）
- VRChat SDK Worlds 3.8.1以上
- UdonSharp（VRChat SDKに同梱）
- TextMeshPro（Unityに同梱）
- Quest／PC対応

---

## ライセンス

MITライセンスです。個人利用・商用利用ともに自由です。
クレジット表記は歓迎されますが、必須ではありません。
