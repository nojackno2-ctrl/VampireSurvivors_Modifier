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
using VSModifier.Core.Saves;

namespace VSModifier.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private readonly GameProcessDetector _processDetector = new();
    private readonly SaveFileService _saveFileService;
    private readonly SavePathLocator _savePathLocator = new();
    private readonly DispatcherTimer _statusTimer;
    private SaveDocument? _document;
    private string? _lastBackupPath;

    public MainWindow()
    {
        InitializeComponent();
        _saveFileService = new SaveFileService(_processDetector);
        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, StatusTimer_Tick, Dispatcher);
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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

        UpdateGameStatus();
        _statusTimer.Start();
    }

    private void StatusTimer_Tick(object? sender, EventArgs e) => UpdateGameStatus();

    private void UpdateGameStatus()
    {
        bool running = _processDetector.IsGameRunning();
        GameStatusLight.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(running ? "#EF4444" : "#22C55E"));
        GameStatusLight.ToolTip = running ? "遊戲執行中：禁止寫入" : "遊戲已關閉：可安全寫入";
        SaveButton.IsEnabled = _document is not null && !running;
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
            string backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VSModifier",
                "backups");
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
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        new SaveEditor(_document).UnlockAll(new Dictionary<string, IReadOnlyCollection<string>>
        {
            [propertyName] = ids
        });
        RefreshUnlockEditor();
        RefreshJsonEditor();
        SetStatus($"{propertyName} 已套用 {ids.Length} 個 ID，尚未寫入磁碟。", false);
    }

    private void ApplyEggButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEditor(out SaveEditor? editor))
        {
            return;
        }

        try
        {
            string attribute = SelectedComboText(EggAttributeComboBox)
                ?? throw new InvalidOperationException("請選擇蛋屬性。");
            editor.SetEggAttribute(
                EggCharacterTextBox.Text.Trim(),
                attribute,
                ParseDouble(EggValueTextBox.Text, "蛋數"));
            RefreshJsonEditor();
            SetStatus("蛋屬性與 total 已套用，尚未寫入磁碟。", false);
        }
        catch (Exception exception)
        {
            ShowError("無法套用蛋屬性", exception);
        }
    }

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
        RefreshJsonEditor();
        BackupPathText.Text = _lastBackupPath ?? "尚未由本程式建立備份";
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
            : string.Join(Environment.NewLine, array.Select(node => node?.GetValue<string>()).Where(value => value is not null));
    }

    private void RefreshJsonEditor()
    {
        if (_document is not null)
        {
            JsonEditorTextBox.Text = _document.Root.ToJsonString(IndentedJson);
        }
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
}
