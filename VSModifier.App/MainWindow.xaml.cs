using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VSModifier.App.Hotkeys;
using VSModifier.Core.Game;
using VSModifier.Core.Saves;
using VSModifier.Memory.Profiles;
using VSModifier.Memory.Trainer;

namespace VSModifier.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private readonly GameProcessDetector _processDetector = new();
    private readonly SaveFileService _saveFileService;
    private readonly SavePathLocator _savePathLocator = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<string, (CheckBox Toggle, TextBox Value)> _trainerAttributeControls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _additiveTrainerFeatures = new(StringComparer.Ordinal)
    {
        "stat.amount", "stat.armor", "stat.revivals", "stat.rerolls", "stat.skips", "stat.banish", "stat.charm"
    };
    private SaveDocument? _document;
    private string? _lastBackupPath;
    private GameInstallation? _gameInstallation;
    private OffsetCatalog? _offsetCatalog;
    private GameVersionProfile? _gameProfile;
    private TrainerSession? _trainerSession;
    private GlobalHotkeyService? _hotkeyService;
    private bool _updatingTrainerUi;
    private JsonTreeEntry? _selectedJsonTreeEntry;

    public MainWindow()
    {
        InitializeComponent();
        _saveFileService = new SaveFileService(_processDetector);
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, StatusTimer_Tick, Dispatcher);
        CreateTrainerAttributeControls();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _hotkeyService = new GlobalHotkeyService(this);
            _hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
        }
        catch (Exception exception)
        {
            EnableHotkeysCheckBox.IsEnabled = false;
            EnableHotkeysCheckBox.ToolTip = exception.Message;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshLatestBackupStatus();
        UnlockPropertyComboBox.SelectedIndex = 0;
        EggAttributeComboBox.SelectedIndex = 0;
        DetectSavePath();
        if (File.Exists(SavePathTextBox.Text))
        {
            await LoadSaveAsync();
        }
        else
        {
            MainTabs.IsEnabled = true;
            SetStatus("找不到存檔，請按「瀏覽」選擇 SaveData。", isError: true);
        }

        await InitializeTrainerAsync();
        UpdateGameStatus();
        _statusTimer.Start();
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        UpdateGameStatus();
        if (_trainerSession is not null && !_trainerSession.IsAttached)
        {
            await DetachTrainerAsync("遊戲行程已結束；Trainer 已中斷。", showErrors: false);
        }
    }

    private void UpdateGameStatus()
    {
        bool running = _processDetector.IsGameRunning();
        GameStatusLight.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#EF4444" : "#22C55E"));
        GameStatusLight.ToolTip = running ? "遊戲執行中：禁止寫入" : "遊戲已關閉：可安全寫入";
        GameStatusText.Text = running ? "遊戲執行中：禁止寫入" : "遊戲已關閉：可安全寫入";
        GameStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#FCA5A5" : "#86EFAC"));
        SaveButton.IsEnabled = _document is not null && !running;
        AttachTrainerButton.IsEnabled = running
            && _trainerSession is null
            && _gameProfile is { Verified: true, OnlineSession: not null };
    }

    private async void DetectSaveButton_Click(object sender, RoutedEventArgs e)
    {
        DetectSavePath();
        if (File.Exists(SavePathTextBox.Text))
        {
            await LoadSaveAsync();
        }
        else
        {
            SetStatus("未找到 Steam SaveData。", isError: true);
        }
    }

    private void DetectSavePath()
    {
        SaveCandidate? candidate = _savePathLocator.FindCandidates().FirstOrDefault();
        if (candidate is not null)
        {
            SavePathTextBox.Text = candidate.Path;
        }
    }

    private async void BrowseSaveButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "選擇 Vampire Survivors SaveData",
            FileName = "SaveData",
            Filter = "SaveData|SaveData|所有檔案|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            SavePathTextBox.Text = dialog.FileName;
            await LoadSaveAsync();
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) => await LoadSaveAsync();

    private async Task LoadSaveAsync()
    {
        try
        {
            SetBusy(true, "正在驗證並載入存檔…");
            _document = await _saveFileService.LoadAsync(SavePathTextBox.Text);
            string fullSavePath = Path.GetFullPath(SavePathTextBox.Text);
            CurrentSavePathStatusText.Text = $"存檔：{fullSavePath}";
            CurrentSavePathStatusText.ToolTip = fullSavePath;
            RefreshAllFields();
            MainTabs.IsEnabled = true;
            ReloadButton.IsEnabled = true;
            FileInfo info = new(SavePathTextBox.Text);
            SaveSizeText.Text = $"{info.Length:N0} bytes";
            ChecksumText.Text = "有效（SHA-256）";
            ChecksumText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
            SetStatus("存檔已載入；所有修改仍只在記憶體中。", isError: false);
        }
        catch (Exception exception)
        {
            _document = null;
            MainTabs.IsEnabled = true;
            ReloadButton.IsEnabled = File.Exists(SavePathTextBox.Text);
            SaveButton.IsEnabled = false;
            ChecksumText.Text = "無法驗證";
            ChecksumText.Foreground = Brushes.OrangeRed;
            ShowError("無法載入存檔", exception);
        }
        finally
        {
            SetBusy(false);
            UpdateGameStatus();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "即將先建立完整備份，再覆寫所選 SaveData。\n\n請確認遊戲已關閉。是否繼續？",
            "備份並寫入",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true, "正在備份並安全寫入…");
            string backupDirectory = GetBackupDirectory();
            SaveWriteResult result = await _saveFileService.SaveAsync(SavePathTextBox.Text, _document, backupDirectory);
            _lastBackupPath = result.BackupPath;
            BackupPathText.Text = result.BackupPath;
            BackupStatusText.Text = $"最近備份：{DateTime.Now:G}";
            RefreshJsonEditor();
            SetStatus("已成功備份並寫入存檔。", isError: false);
        }
        catch (Exception exception)
        {
            ShowError("寫入失敗；原始備份（若已建立）仍保留", exception);
        }
        finally
        {
            SetBusy(false);
            UpdateGameStatus();
        }
    }

    private void ApplyResourcesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor))
        {
            return;
        }

        try
        {
            editor.SetNumber("Coins", ParseDouble(CoinsTextBox.Text, "Coins"));
            editor.SetNumber("LifetimeCoins", ParseDouble(LifetimeCoinsTextBox.Text, "LifetimeCoins"));
            editor.SetNumber("TotalCoins", ParseDouble(TotalCoinsTextBox.Text, "TotalCoins"));
            editor.SetInteger("Seals", ParseInt(SealsTextBox.Text, "Seals"));
            editor.SetNumber("AdventureStars", ParseDouble(AdventureStarsTextBox.Text, "AdventureStars"));
            RefreshJsonEditor();
            SetStatus("資源欄位已套用，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("資源欄位格式錯誤", exception);
        }
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor)
            || MessageBox.Show(this, "將常用資源設為高值。是否套用？", "一鍵最大化", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        editor.MaximizeCommonResources();
        RefreshAllFields();
        SetStatus("已套用資源最大化，尚未寫入磁碟。", false);
    }

    private void UnlockPropertyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_document is not null)
        {
            RefreshUnlockEditor();
        }
    }

    private void ApplyUnlocksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || SelectedComboText(UnlockPropertyComboBox) is not string propertyName)
        {
            return;
        }

        string[] ids = UnlockIdsTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        JsonNode?[] values = string.Equals(propertyName, "UnlockedArcanas", StringComparison.Ordinal)
            ? ids.Select(id => JsonValue.Create(ParseInt(id, "Arcana ID")) as JsonNode).ToArray()
            : ids.Select(id => JsonValue.Create(id) as JsonNode).ToArray();
        new SaveEditor(_document).ReplaceArray(propertyName, values);
        RefreshUnlockEditor();
        RefreshJsonEditor();
        SetStatus($"{propertyName} 已套用 {ids.Length} 個 ID，尚未寫入磁碟。", false);
    }

    private void UnlockAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null
            || MessageBox.Show(this, "將安全合併目前遊戲版本的解鎖 ID，保留既有順序、重複等級與未收錄內容。這可能影響成就與 CheatCodeUsed。是否繼續？", "一鍵全解鎖", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "data", "ids", "unlocks.json");
            GameVersionProfile gameProfile = _gameProfile
                ?? throw new InvalidOperationException("目前安裝的遊戲版本尚未辨識，已拒絕套用版本解鎖表；仍可手動編輯個別陣列。");
            UnlockCatalog catalog = UnlockCatalog.Load(path);
            UnlockCatalogProfile unlockProfile = catalog.FindByProfileId(gameProfile.ProfileId)
                ?? throw new InvalidOperationException($"unlocks.json 沒有版本 Profile {gameProfile.ProfileId} 的資料。");
            new SaveEditor(_document).MergeUnlockCatalog(unlockProfile.Arrays);
            RefreshAllFields();
            SetStatus($"已安全合併 {unlockProfile.Arrays.Count} 組版本解鎖資料，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("無法套用版本解鎖表", exception);
        }
    }

    private void ApplyEggButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor))
        {
            return;
        }

        try
        {
            string attribute = SelectedComboKey(EggAttributeComboBox)
                ?? throw new InvalidOperationException("請選擇蛋屬性。");
            editor.SetEggAttribute(
                EggCharacterTextBox.Text.Trim(),
                attribute,
                ParseDouble(EggValueTextBox.Text, "蛋數"));
            RefreshEggCurrentValue();
            RefreshJsonEditor();
            string label = SelectedComboText(EggAttributeComboBox) ?? attribute;
            SetStatus($"只修改 {label}，其他金蛋屬性未變；total 已重算，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("無法套用蛋屬性", exception);
        }
    }

    private void EggCharacterTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshEggCurrentValue();

    private void EggAttributeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshEggCurrentValue();

    private void ApplyFlagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor))
        {
            return;
        }

        editor.SetFlag("AlwaysQuickTreasureAnim", QuickTreasureCheckBox.IsChecked == true);
        editor.SetFlag("CheatCodeUsed", CheatCodeUsedCheckBox.IsChecked == true);
        editor.SetFlag("HasKilledTheFinalBoss", FinalBossCheckBox.IsChecked == true);
        editor.SetFlag("SequentialChestMode", SequentialChestCheckBox.IsChecked == true);
        RefreshJsonEditor();
        SetStatus("旗標已套用，尚未寫入磁碟。", false);
    }

    private void SetProgressFlagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor)
            || MessageBox.Show(this, "將所有 HasSeen* / HasUsed* 與常用進度旗標設為 true。是否套用？", "進度旗標", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        editor.SetAllProgressFlags(true);
        RefreshAllFields();
        SetStatus("進度旗標已套用，尚未寫入磁碟。", false);
    }

    private void ApplyJsonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            _document.ReplaceJson(JsonEditorTextBox.Text, requireValidChecksum: false);
            string withChecksum = _document.SerializeWithChecksum();
            _document.ReplaceJson(withChecksum, requireValidChecksum: true);
            RefreshAllFields();
            SetStatus("JSON 結構有效且已套用，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("JSON 驗證失敗", exception);
        }
    }

    private void JsonTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedJsonTreeEntry = e.NewValue as JsonTreeEntry;
        JsonSelectedPathTextBox.Text = _selectedJsonTreeEntry?.Path ?? string.Empty;
        JsonSelectedTypeText.Text = _selectedJsonTreeEntry is null
            ? "未選取節點"
            : $"型別：{_selectedJsonTreeEntry.Kind}";
        JsonSelectedValueTextBox.Text = _selectedJsonTreeEntry?.EditableValue ?? string.Empty;
        bool canEdit = _selectedJsonTreeEntry?.CanEditValue == true;
        JsonSelectedValueTextBox.IsEnabled = canEdit;
        ApplySelectedJsonValueButton.IsEnabled = canEdit;
    }

    private void ApplySelectedJsonValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || _selectedJsonTreeEntry is not { CanEditValue: true } entry)
        {
            return;
        }

        try
        {
            entry.ReplaceValue(ParseTreeValue(entry.Kind, JsonSelectedValueTextBox.Text));
            RefreshAllFields();
            SetStatus($"{entry.Path} 已套用，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("JSON 節點值無效", exception);
        }
    }

    private void RefreshAllFields()
    {
        if (_document is null)
        {
            return;
        }

        CoinsTextBox.Text = ReadNumber("Coins");
        LifetimeCoinsTextBox.Text = ReadNumber("LifetimeCoins");
        TotalCoinsTextBox.Text = ReadNumber("TotalCoins");
        SealsTextBox.Text = ReadNumber("Seals");
        AdventureStarsTextBox.Text = ReadNumber("AdventureStars");
        QuickTreasureCheckBox.IsChecked = ReadFlag("AlwaysQuickTreasureAnim");
        CheatCodeUsedCheckBox.IsChecked = ReadFlag("CheatCodeUsed");
        FinalBossCheckBox.IsChecked = ReadFlag("HasKilledTheFinalBoss");
        SequentialChestCheckBox.IsChecked = ReadFlag("SequentialChestMode");
        RefreshUnlockEditor();
        RefreshEggAttributeOptions();
        RefreshEggCurrentValue();
        RefreshJsonEditor();
        BackupPathText.Text = _lastBackupPath ?? "尚未由本程式建立備份";
    }

    private void RefreshLatestBackupStatus()
    {
        try
        {
            DirectoryInfo directory = new(GetBackupDirectory());
            FileInfo? latest = directory.Exists
                ? directory.EnumerateFiles("SaveData_*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.LastWriteTime)
                    .FirstOrDefault()
                : null;
            if (latest is null)
            {
                BackupStatusText.Text = "最近備份：尚無";
                return;
            }

            _lastBackupPath = latest.FullName;
            BackupStatusText.Text = $"最近備份：{latest.LastWriteTime:G}";
            BackupStatusText.ToolTip = latest.FullName;
        }
        catch
        {
            BackupStatusText.Text = "最近備份：無法讀取";
        }
    }

    private static string GetBackupDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VSModifier",
            "backups");
    }

    private void RefreshUnlockEditor()
    {
        if (_document is null || SelectedComboText(UnlockPropertyComboBox) is not string propertyName)
        {
            return;
        }

        JsonArray? array = _document.Root[propertyName] as JsonArray;
        UnlockIdsTextBox.Text = array is null
            ? string.Empty
            : string.Join(Environment.NewLine, array.Select(node => node?.ToString()).Where(value => value is not null));
    }

    private void RefreshEggCurrentValue()
    {
        if (_document is null || EggCurrentValueText is null || EggAttributeComboBox is null)
        {
            return;
        }

        string? attribute = SelectedComboKey(EggAttributeComboBox);
        if (attribute is null || string.IsNullOrWhiteSpace(EggCharacterTextBox.Text))
        {
            EggCurrentValueText.Text = "請輸入角色 ID 並選擇屬性";
            return;
        }

        SaveEditor editor = new(_document);
        EggCurrentValueText.Text = editor.TryGetEggAttribute(EggCharacterTextBox.Text, attribute, out double value)
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : "此角色尚無該屬性資料（目前視為 0）";
    }

    private void RefreshEggAttributeOptions()
    {
        if (_document is null || EggAttributeComboBox is null)
        {
            return;
        }

        HashSet<string> existing = EggAttributeComboBox.Items
            .OfType<ComboBoxItem>()
            .Select(item => item.Tag?.ToString())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string attribute in new SaveEditor(_document).GetEggAttributeNames())
        {
            if (existing.Add(attribute))
            {
                EggAttributeComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{attribute}（此版本額外屬性）",
                    Tag = attribute
                });
            }
        }
    }

    private void RefreshJsonEditor()
    {
        if (_document is not null)
        {
            JsonEditorTextBox.Text = _document.Root.ToJsonString(IndentedJson);
            JsonTreeView.ItemsSource = new[] { JsonTreeEntry.CreateRoot(_document.Root) };
            _selectedJsonTreeEntry = null;
            JsonSelectedPathTextBox.Text = string.Empty;
            JsonSelectedTypeText.Text = "未選取節點";
            JsonSelectedValueTextBox.Text = string.Empty;
            JsonSelectedValueTextBox.IsEnabled = false;
            ApplySelectedJsonValueButton.IsEnabled = false;
        }
    }

    private static JsonNode? ParseTreeValue(JsonValueKind kind, string text)
    {
        return kind switch
        {
            JsonValueKind.String => JsonValue.Create(text),
            JsonValueKind.Number => ParseScalar(text, JsonValueKind.Number),
            JsonValueKind.True or JsonValueKind.False => bool.TryParse(text, out bool value)
                ? JsonValue.Create(value)
                : throw new FormatException("布林值只能輸入 true 或 false。"),
            JsonValueKind.Null => ParseScalar(text, expectedKind: null),
            _ => throw new InvalidOperationException("物件與陣列請在原始 JSON 模式編輯。")
        };
    }

    private static JsonNode? ParseScalar(string text, JsonValueKind? expectedKind)
    {
        JsonNode? parsed = JsonNode.Parse(text);
        JsonValueKind actualKind;
        if (parsed is null)
        {
            actualKind = JsonValueKind.Null;
        }
        else
        {
            using JsonDocument document = JsonDocument.Parse(parsed.ToJsonString());
            actualKind = document.RootElement.ValueKind;
        }

        if (actualKind is JsonValueKind.Object or JsonValueKind.Array
            || expectedKind.HasValue && actualKind != expectedKind.Value)
        {
            throw new FormatException(expectedKind.HasValue
                ? $"必須輸入 {expectedKind.Value} 型別。"
                : "只能輸入 JSON 純量值。");
        }

        return parsed;
    }

    private string ReadNumber(string propertyName)
    {
        JsonValue? value = _document?.Root[propertyName] as JsonValue;
        return value?.ToJsonString() ?? "0";
    }

    private bool ReadFlag(string propertyName)
    {
        return _document?.Root[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out bool result)
            && result;
    }

    private bool TryGetEditor([NotNullWhen(true)] out SaveEditor? editor)
    {
        if (_document is null)
        {
            editor = null;
            SetStatus("請先載入存檔。", true);
            return false;
        }

        editor = new SaveEditor(_document);
        return true;
    }

    private static string? SelectedComboText(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
    }

    private static string? SelectedComboKey(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
        {
            return null;
        }

        return item.Tag?.ToString() ?? item.Content?.ToString();
    }

    private static double ParseDouble(string text, string field)
    {
        if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            && !double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
        {
            throw new FormatException($"{field} 不是有效數值。");
        }

        return value;
    }

    private static int ParseInt(string text, string field)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            throw new FormatException($"{field} 不是有效整數。");
        }

        return value;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
        if (message is not null)
        {
            SetStatus(message, false);
        }

        ReloadButton.IsEnabled = !busy && File.Exists(SavePathTextBox.Text);
        MainTabs.IsEnabled = !busy;
        if (busy)
        {
            SaveButton.IsEnabled = false;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#FCA5A5" : "#CBD5E1"));
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus($"{title}：{exception.Message}", true);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async Task InitializeTrainerAsync()
    {
        try
        {
            TrainerStatusText.Text = "正在辨識遊戲版本…";
            _gameInstallation = new GameInstallationLocator().FindInstallations().FirstOrDefault();
            if (_gameInstallation is null)
            {
                TrainerGamePathText.Text = "找不到完整遊戲安裝";
                TrainerVersionText.Text = "無";
                TrainerStatusText.Text = "Trainer 不可用。";
                return;
            }

            TrainerGamePathText.Text = _gameInstallation.RootPath;
            string offsetsPath = Path.Combine(AppContext.BaseDirectory, "data", "offsets.json");
            _offsetCatalog = OffsetCatalog.Load(offsetsPath);
            GameVersionFingerprint fingerprint = await Task.Run(() => GameVersionFingerprint.Calculate(
                _gameInstallation.GameAssemblyPath,
                _gameInstallation.UnityPlayerPath,
                _gameInstallation.MetadataPath));
            ProfileMatchResult match = _offsetCatalog.Match(fingerprint);
            _gameProfile = match.Profile;
            if (_gameProfile is null)
            {
                TrainerVersionText.Text = $"未知組合（GA {fingerprint.GameAssemblySha256[..12]}… / UP {fingerprint.UnityPlayerSha256[..12]}… / MD {fingerprint.Il2CppMetadataSha256[..12]}…）";
                TrainerStatusText.Text = $"{match.Error} 已拒絕附加。";
                TrainerStatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            TrainerVersionText.Text = $"{_gameProfile.Label}（GA {fingerprint.GameAssemblySha256[..12]}… / UP {fingerprint.UnityPlayerSha256[..12]}… / MD {fingerprint.Il2CppMetadataSha256[..12]}…）";
            if (!_gameProfile.Verified)
            {
                TrainerStatusText.Text = "版本已辨識，但偏移尚未實機驗證；已拒絕附加。";
                TrainerStatusText.Foreground = Brushes.Orange;
                return;
            }

            if (_gameProfile.OnlineSession is null)
            {
                TrainerStatusText.Text = "缺少線上會話防護偏移；已拒絕附加。";
                TrainerStatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            TrainerStatusText.Text = "版本與線上防護已驗證；啟動遊戲後可附加。";
            TrainerStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            TrainerVersionText.Text = "無法辨識";
            TrainerStatusText.Text = exception.Message;
            TrainerStatusText.Foreground = Brushes.OrangeRed;
        }
        finally
        {
            UpdateGameStatus();
        }
    }

    private async void AttachTrainerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_offsetCatalog is null || _gameInstallation is null)
        {
            return;
        }

        try
        {
            AttachTrainerButton.IsEnabled = false;
            TrainerStatusText.Text = "正在安全附加…";
            _trainerSession = await Task.Run(() => TrainerSession.Attach(
                _offsetCatalog,
                _gameInstallation.GameAssemblyPath,
                _gameInstallation.UnityPlayerPath,
                _gameInstallation.MetadataPath));
            _trainerSession.SafetyStopped += TrainerSession_SafetyStopped;
            DetachTrainerButton.IsEnabled = true;
            TrainerControlsPanel.IsEnabled = true;
            UpdateTrainerFeatureAvailability();
            TrainerStatusText.Text = "已附加；線上 guard 每 100ms 持續監控，異常時會停止全部功能並還原。";
            TrainerStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            TrainerStatusText.Text = exception.Message;
            TrainerStatusText.Foreground = Brushes.OrangeRed;
        }
    }

    private async void DetachTrainerButton_Click(object sender, RoutedEventArgs e)
    {
        await DetachTrainerAsync("已中斷 Trainer 並還原原始值。", showErrors: true);
    }

    private async Task DetachTrainerAsync(string status, bool showErrors)
    {
        TrainerSession? session = _trainerSession;
        _trainerSession = null;
        TrainerControlsPanel.IsEnabled = false;
        DetachTrainerButton.IsEnabled = false;
        ResetTrainerToggles();
        if (session is not null)
        {
            session.SafetyStopped -= TrainerSession_SafetyStopped;
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception exception)
            {
                TrainerStatusText.Text = exception.Message;
                TrainerStatusText.Foreground = Brushes.OrangeRed;
                if (showErrors)
                {
                    MessageBox.Show(this, exception.Message, "Trainer 還原失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                UpdateGameStatus();
                return;
            }
        }

        TrainerStatusText.Text = status;
        TrainerStatusText.Foreground = Brushes.LightGray;
        UpdateGameStatus();
    }

    private void TrainerSession_SafetyStopped(object? sender, TrainerSafetyStopEventArgs args)
    {
        if (sender is not TrainerSession session)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() => _ = HandleTrainerSafetyStopAsync(session, args));
    }

    private async Task HandleTrainerSafetyStopAsync(TrainerSession session, TrainerSafetyStopEventArgs args)
    {
        if (!ReferenceEquals(_trainerSession, session))
        {
            return;
        }

        string status = args.RestorationError is null
            ? $"安全防護已停止 Trainer 並還原全部功能：{args.Cause.Message}"
            : $"安全防護已停止 Trainer，但還原時發生錯誤：{args.RestorationError.Message}";
        await DetachTrainerAsync(status, showErrors: args.RestorationError is not null);
        TrainerStatusText.Foreground = args.RestorationError is null ? Brushes.OrangeRed : Brushes.Red;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _hotkeyService?.Dispose();
        _hotkeyService = null;
        if (_trainerSession is not null)
        {
            await DetachTrainerAsync("Trainer 已關閉。", showErrors: false);
        }
    }

    private void EnableHotkeysCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_hotkeyService is null)
        {
            EnableHotkeysCheckBox.IsChecked = false;
            TrainerStatusText.Text = "全域熱鍵服務尚未就緒。";
            TrainerStatusText.Foreground = Brushes.OrangeRed;
            return;
        }

        try
        {
            if (EnableHotkeysCheckBox.IsChecked == true)
            {
                _hotkeyService.RegisterDefaults();
                TrainerStatusText.Text = "全域熱鍵已啟用；Ctrl+Shift+F12 可緊急中斷並還原。";
                TrainerStatusText.Foreground = Brushes.LightGreen;
            }
            else
            {
                _hotkeyService.UnregisterAll();
                TrainerStatusText.Text = "全域熱鍵已停用。";
                TrainerStatusText.Foreground = Brushes.LightGray;
            }
        }
        catch (Exception exception)
        {
            EnableHotkeysCheckBox.IsChecked = false;
            TrainerStatusText.Text = exception.Message;
            TrainerStatusText.Foreground = Brushes.OrangeRed;
        }
    }

    private void HotkeyService_HotkeyPressed(object? sender, TrainerHotkeyCommand command)
    {
        if (command == TrainerHotkeyCommand.EmergencyDetach)
        {
            _ = DetachTrainerAsync("已由緊急熱鍵中斷 Trainer 並還原原始值。", showErrors: true);
            return;
        }

        if (_trainerSession is null)
        {
            TrainerStatusText.Text = "熱鍵未執行：Trainer 尚未附加。";
            TrainerStatusText.Foreground = Brushes.Orange;
            return;
        }

        CheckBox toggle = command switch
        {
            TrainerHotkeyCommand.ToggleInvulnerability => InvulnerabilityCheckBox,
            TrainerHotkeyCommand.ToggleQuickTreasure => QuickTreasureRuntimeCheckBox,
            TrainerHotkeyCommand.ToggleMaxTreasure => MaxTreasureCheckBox,
            TrainerHotkeyCommand.ToggleDamageMultiplier => DamageMultiplierCheckBox,
            TrainerHotkeyCommand.ToggleGameSpeed => GameSpeedCheckBox,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        if (!toggle.IsEnabled)
        {
            TrainerStatusText.Text = $"熱鍵未執行：此版本不支援 {toggle.Content}。";
            TrainerStatusText.Foreground = Brushes.Orange;
            return;
        }

        toggle.IsChecked = toggle.IsChecked != true;
        if (command is TrainerHotkeyCommand.ToggleInvulnerability or TrainerHotkeyCommand.ToggleQuickTreasure)
        {
            TrainerDirectValueCheckBox_Click(toggle, new RoutedEventArgs());
        }
        else if (command == TrainerHotkeyCommand.ToggleMaxTreasure)
        {
            TrainerPatchCheckBox_Click(toggle, new RoutedEventArgs());
        }
        else
        {
            TrainerMultiplierCheckBox_Click(toggle, new RoutedEventArgs());
        }
    }

    private void TrainerDirectValueCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingTrainerUi || sender is not CheckBox toggle || toggle.Tag is not string key || _trainerSession is null)
        {
            return;
        }

        ApplyTrainerAction(toggle, () =>
        {
            if (toggle.IsChecked == true)
            {
                _trainerSession.EnableValueLock(key, 1);
            }
            else
            {
                _trainerSession.DisableValueLock(key);
            }
        });
    }

    private void TrainerPatchCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingTrainerUi || sender is not CheckBox toggle || toggle.Tag is not string key || _trainerSession is null)
        {
            return;
        }

        ApplyTrainerAction(toggle, () =>
        {
            if (toggle.IsChecked == true)
            {
                _trainerSession.EnablePatch(key);
            }
            else
            {
                _trainerSession.DisablePatch(key);
            }
        });
    }

    private void TrainerMultiplierCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingTrainerUi || sender is not CheckBox toggle || toggle.Tag is not string key || _trainerSession is null)
        {
            return;
        }

        TextBox valueBox = key switch
        {
            "damageMultiplier" => DamageMultiplierTextBox,
            "gameSpeed" => GameSpeedTextBox,
            _ when _trainerAttributeControls.TryGetValue(key, out var control) => control.Value,
            _ => throw new InvalidOperationException($"找不到 {key} 的倍率欄位。")
        };
        ApplyTrainerAction(toggle, () =>
        {
            if (toggle.IsChecked == true)
            {
                double adjustment = ParseDouble(valueBox.Text, _additiveTrainerFeatures.Contains(key) ? "增加量" : "倍率");
                if (_additiveTrainerFeatures.Contains(key))
                {
                    _trainerSession.EnableAdditiveLock(key, adjustment);
                }
                else
                {
                    _trainerSession.EnableMultiplierLock(key, adjustment);
                }
            }
            else
            {
                _trainerSession.DisableValueLock(key);
            }
        });
    }

    private void ApplyTrainerAction(CheckBox toggle, Action action)
    {
        try
        {
            action();
            TrainerStatusText.Text = $"{toggle.Content}：{(toggle.IsChecked == true ? "已啟用" : "已還原")}。";
            TrainerStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            _updatingTrainerUi = true;
            toggle.IsChecked = false;
            _updatingTrainerUi = false;
            TrainerStatusText.Text = exception.Message;
            TrainerStatusText.Foreground = Brushes.OrangeRed;
        }
    }

    private void CreateTrainerAttributeControls()
    {
        (string Key, string Label, string DefaultValue)[] definitions =
        [
            ("growth", "經驗倍率", "5"), ("greed", "金幣倍率", "5"),
            ("luck", "運氣", "5"), ("cooldown", "冷卻倍率", "0.1"),
            ("moveSpeed", "移動速度", "2"), ("area", "攻擊範圍", "3"),
            ("amount", "投射物數量 +", "5"), ("duration", "持續時間", "3"),
            ("speed", "彈速", "2"), ("armor", "護甲 +", "999"),
            ("regen", "回血", "5"), ("magnet", "磁吸範圍", "5"),
            ("revivals", "復活次數 +", "99"), ("rerolls", "Reroll +", "99"),
            ("skips", "Skip +", "99"), ("banish", "Banish +", "99"),
            ("curse", "詛咒／敵人密度", "2"), ("charm", "魅惑 +", "10"),
            ("defang", "拔牙", "2")
        ];

        foreach ((string key, string label, string defaultValue) in definitions)
        {
            string featureKey = $"stat.{key}";
            Grid row = new() { Margin = new Thickness(6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            CheckBox toggle = new() { Content = label, Tag = featureKey, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += TrainerMultiplierCheckBox_Click;
            TextBox value = new() { Text = defaultValue, ToolTip = "倍率" };
            Grid.SetColumn(value, 1);
            row.Children.Add(toggle);
            row.Children.Add(value);
            TrainerAttributePanel.Children.Add(row);
            _trainerAttributeControls.Add(featureKey, (toggle, value));
        }
    }

    private void UpdateTrainerFeatureAvailability()
    {
        if (_trainerSession is null)
        {
            return;
        }

        IReadOnlyDictionary<string, FeatureDefinition> features = _trainerSession.Profile.Features;
        InvulnerabilityCheckBox.IsEnabled = features.ContainsKey("invulnerability");
        QuickTreasureRuntimeCheckBox.IsEnabled = features.ContainsKey("quickTreasure");
        MaxTreasureCheckBox.IsEnabled = features.ContainsKey("maxTreasure");
        DamageMultiplierCheckBox.IsEnabled = features.ContainsKey("damageMultiplier");
        GameSpeedCheckBox.IsEnabled = features.ContainsKey("gameSpeed");
        foreach ((string key, (CheckBox toggle, _)) in _trainerAttributeControls)
        {
            toggle.IsEnabled = features.ContainsKey(key);
        }
    }

    private void ResetTrainerToggles()
    {
        _updatingTrainerUi = true;
        InvulnerabilityCheckBox.IsChecked = false;
        QuickTreasureRuntimeCheckBox.IsChecked = false;
        MaxTreasureCheckBox.IsChecked = false;
        DamageMultiplierCheckBox.IsChecked = false;
        GameSpeedCheckBox.IsChecked = false;
        foreach ((_, (CheckBox toggle, _)) in _trainerAttributeControls)
        {
            toggle.IsChecked = false;
        }

        _updatingTrainerUi = false;
    }
}
