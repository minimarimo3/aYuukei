// ==========================================================================
// DaihonScenarioManager.cs
// 台本のパース・キャッシュ管理と、トリガー発火時の実行ルーティングを行う（§2.6準拠）。
// ==========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Daihon;
using Daihon.Unity;
using UnityEngine;

/// <summary>
/// 有効な全パッケージの台本ファイルを事前パースしてメタデータを管理し、
/// トリガーイベント発生時に DaihonRunner にルーティングするクラス。
/// 複数シーンの逐次実行、デフォルトシーンのフォールバック仕様を管理する。
/// </summary>
public class DaihonScenarioManager : MonoBehaviour
{
    public static DaihonScenarioManager Instance { get; private set; }

    [SerializeField] private YuukeiActionHandler actionHandler;

    private readonly Dictionary<string, PackageScenarioInfo> _packages = new();
    private readonly Dictionary<string, SimpleVariableStore> _variableStores = new();

    private bool _isScriptRunning;

    private class PackageScenarioInfo
    {
        public string PackageId;
        public List<ScenarioEntry> Scenarios = new();
    }

    private class ScenarioEntry
    {
        public string FilePath;
        public string ScriptText;
        public DaihonParser.DefaultsBlockContext DefaultsBlock;
        public List<DaihonSceneMetadata> Scenes;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (TriggerManager.Instance != null)
        {
            TriggerManager.Instance.OnSystemEventFired += HandleSystemEvent;
        }
        
        // 起動時にローカル台本を読み込む
        LoadLocalScripts();
    }

    private void OnDestroy()
    {
        if (TriggerManager.Instance != null)
        {
            TriggerManager.Instance.OnSystemEventFired -= HandleSystemEvent;
        }
    }

    // ── メタデータ解析 ──────────────────────────────────────────────────

    /// <summary>
    /// パッケージ内のすべての .daihon ファイルを列挙し、キャッシュする。
    /// （ PackageManager の RestorePackageAsync / InstallAsync などから呼ばれる想定 ）
    /// </summary>
    public void LoadPackageScenarios(string packageId, string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return;

        var info = new PackageScenarioInfo { PackageId = packageId };
        var files = Directory.GetFiles(installPath, "*.daihon", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            var fileMetadata = DaihonRunner.ExtractMetadata(text);
            
            if (fileMetadata.Scenes.Count > 0)
            {
                info.Scenarios.Add(new ScenarioEntry
                {
                    FilePath = file,
                    ScriptText = text,
                    DefaultsBlock = fileMetadata.DefaultsBlock,
                    Scenes = fileMetadata.Scenes
                });
            }
        }

        _packages[packageId] = info;
        Debug.Log($"[DaihonScenarioManager] パッケージ '{packageId}' の台本 {info.Scenarios.Count} 件を解析・登録しました。");
    }

    public void UnloadPackageScenarios(string packageId)
    {
        _packages.Remove(packageId);
        _variableStores.Remove(packageId);
    }

    public void LoadLocalScripts()
    {
        string localScriptsPath = Path.Combine(Application.persistentDataPath, "Daihon");
        if (!Directory.Exists(localScriptsPath)) return;

        // "LocalScripts" という特別なパッケージIDとして扱う
        string packageId = "LocalScripts";
        var info = new PackageScenarioInfo { PackageId = packageId };
        
        // .daihon と .txt の両方を検索
        var files = Directory.GetFiles(localScriptsPath, "*.*", SearchOption.AllDirectories)
                             .Where(s => s.EndsWith(".daihon") || s.EndsWith(".txt"));

        foreach (var file in files)
        {
            string text = File.ReadAllText(file);
            var fileMetadata = DaihonRunner.ExtractMetadata(text);
            
            if (fileMetadata.Scenes.Count > 0)
            {
                info.Scenarios.Add(new ScenarioEntry
                {
                    FilePath = file,
                    ScriptText = text,
                    DefaultsBlock = fileMetadata.DefaultsBlock,
                    Scenes = fileMetadata.Scenes
                });
            }
        }

        _packages[packageId] = info;
        Debug.Log($"[DaihonScenarioManager] ローカル台本 {info.Scenarios.Count} 件を解析・登録しました。");
    }

    // ── トリガー実行ルーティング (§2.6.5準拠) ──────────────────────────

    private void HandleSystemEvent(TriggerPayload payload)
    {
        // 多重実行制御: キャラクターは一度に一つのことしかしない
        if (_isScriptRunning)
        {
            Debug.Log($"[DaihonScenarioManager] 台本実行中のため、イベント「{payload.TriggerName}」を破棄します。");
            return;
        }

        RouteAndExecuteAsync(payload.TriggerName).Forget();
    }

    private async UniTaskVoid RouteAndExecuteAsync(string systemEventName)
    {
        _isScriptRunning = true;
        try
        {
            var activePackages = ConfigManager.Instance?.Settings?.installedPackages
                ?.Where(p => p.enabled)
                .Select(p => p.id)
                .ToList() ?? new List<string>();

            // ローカルスクリプト（単体配布物など）も評価対象に含める
            if (!activePackages.Contains("LocalScripts"))
            {
                activePackages.Add("LocalScripts");
            }

            // §2.6.5 複数シーンの同時成立 / デフォルトシーンのフォールバック
            // Daihon 言語の仕様上、評価は「1つのイベント（ファイル）内」で上から順に行われます。
            // しかし、どのファイルを起動するかはシステム側が「合図」によって見つける必要があります。

            foreach (var packageId in activePackages)
            {
                if (!_packages.TryGetValue(packageId, out var info)) continue;

                var store = GetOrCreateVariableStore(packageId);
                var handler = actionHandler != null ? actionHandler : FindObjectOfType<YuukeiActionHandler>();

                foreach (var scenario in info.Scenarios)
                {
                    var targetScenes = new List<DaihonSceneMetadata>();
                    var monitorScenes = new List<DaihonSceneMetadata>();
                    var defaultScenes = new List<DaihonSceneMetadata>();

                    // 1. 各ファイルのシーンを分類
                    foreach (var sceneMeta in scenario.Scenes)
                    {
                        bool hasSignal = sceneMeta.SystemEvents.Length > 0;

                        if (hasSignal)
                        {
                            // ＠マークの有無にかかわらずマッチ判定を行う（§2.6.1）
                            bool matched = Array.Exists(sceneMeta.SystemEvents, e => 
                                e == systemEventName || 
                                e == "@" + systemEventName || 
                                e == "＠" + systemEventName);

                            if (matched)
                            {
                                targetScenes.Add(sceneMeta);
                            }
                        }
                        else
                        {
                            if (sceneMeta.HasCondition)
                            {
                                monitorScenes.Add(sceneMeta);
                            }
                            else
                            {
                                defaultScenes.Add(sceneMeta);
                            }
                        }
                    }

                    // 2. targetScenes を上から順に評価
                    bool anyTargetSceneExecuted = false;
                    foreach (var scene in targetScenes)
                    {
                        bool conditionResult = true; // 条件がない場合は真とする
                        
                        // 条件式（AST）が存在する場合は評価する
                        if (scene.HasCondition && scene.ConditionContext != null)
                        {
                            conditionResult = await DaihonRunner.EvaluateConditionAsync(scene.ConditionContext, handler, store, scenario.DefaultsBlock);
                        }

                        // 評価結果が true の場合のみ実行
                        if (conditionResult)
                        {
                            await DaihonRunner.RunSceneAsync(scenario.ScriptText, scene.SceneName, handler, store);
                            anyTargetSceneExecuted = true;
                        }
                    }

                    // 3. 【重要】targetScenes.Count > 0 なのに1つも実行されなかった場合のみ、デフォルトシーンからランダムに1つ実行
                    if (targetScenes.Count > 0 && !anyTargetSceneExecuted && defaultScenes.Count > 0)
                    {
                        var chosenDefault = defaultScenes[UnityEngine.Random.Range(0, defaultScenes.Count)];
                        await DaihonRunner.RunSceneAsync(scenario.ScriptText, chosenDefault.SceneName, handler, store);
                    }

                    // 4. monitorScenes はシステムイベントの種別に関係なく毎回評価
                    foreach (var scene in monitorScenes)
                    {
                        bool conditionResult = true;
                        
                        // 条件式（AST）が存在する場合は評価する
                        if (scene.ConditionContext != null)
                        {
                            conditionResult = await DaihonRunner.EvaluateConditionAsync(scene.ConditionContext, handler, store, scenario.DefaultsBlock);
                        }

                        if (conditionResult)
                        {
                            await DaihonRunner.RunSceneAsync(scenario.ScriptText, scene.SceneName, handler, store);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DaihonScenarioManager] 実行中にエラーが発生しました: {ex}");
        }
        finally
        {
            _isScriptRunning = false;
        }
    }

    private SimpleVariableStore GetOrCreateVariableStore(string packageId)
    {
        if (!_variableStores.TryGetValue(packageId, out var store))
        {
            store = new SimpleVariableStore();
            RegisterBuiltinTimeVariables(store);
            _variableStores[packageId] = store;
        }
        return store;
    }

    private static void RegisterBuiltinTimeVariables(SimpleVariableStore store)
    {
        store.RegisterDynamicGetter("年",   () => DaihonValue.FromNumber(DateTime.Now.Year));
        store.RegisterDynamicGetter("月",   () => DaihonValue.FromNumber(DateTime.Now.Month));
        store.RegisterDynamicGetter("日",   () => DaihonValue.FromNumber(DateTime.Now.Day));
        store.RegisterDynamicGetter("時",   () => DaihonValue.FromNumber(DateTime.Now.Hour));
        store.RegisterDynamicGetter("分",   () => DaihonValue.FromNumber(DateTime.Now.Minute));
        store.RegisterDynamicGetter("秒",   () => DaihonValue.FromNumber(DateTime.Now.Second));
        store.RegisterDynamicGetter("ミリ秒", () => DaihonValue.FromNumber(DateTime.Now.Millisecond));
        store.RegisterDynamicGetter("曜日", () => DaihonValue.FromString(
            new[] { "日", "月", "火", "水", "木", "金", "土" }[(int)DateTime.Now.DayOfWeek]));
        store.RegisterDynamicGetter("週",   () => DaihonValue.FromNumber(
            System.Globalization.CultureInfo.CurrentCulture.Calendar
                .GetWeekOfYear(DateTime.Now,
                    System.Globalization.CalendarWeekRule.FirstDay,
                    DayOfWeek.Monday)));
    }

    private class ExecutionTarget
    {
        public string PackageId;
        public ScenarioEntry Scenario;
        public DaihonSceneMetadata SceneMeta;
    }
}
