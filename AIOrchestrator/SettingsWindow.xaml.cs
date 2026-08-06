using System.Windows;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestrator;

/// <summary>Manages the Telegram credentials and model choices (config.json + secrets.json).</summary>
public partial class SettingsWindow : Window
{
    readonly ISupervisionPaths _paths;
    readonly IOrchestratorConfig _config;

    public SettingsWindow(ISupervisionPaths paths, IOrchestratorConfig config)
    {
        _paths = paths;
        _config = config;

        InitializeComponent();
        Views.DarkTitleBar_Enabler.Apply(this);

        BotTokenTextBox.Text = config.TelegramBotToken ?? "";
        ChatIdTextBox.Text = config.TelegramSupergroupChatId?.ToString() ?? "";
        OwnerIdTextBox.Text = config.TelegramOwnerUserId?.ToString() ?? "";
        SupervisorModelTextBox.Text = config.SupervisorModel ?? "";
        ImplementerModelTextBox.Text = config.ImplementerModel ?? "";
        GeneralModelTextBox.Text = config.GeneralSupervisorModel ?? "";
    }

    void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        long? chatId = null;
        long? ownerId = null;

        if (ChatIdTextBox.Text.Trim().Length > 0)
        {
            if (!long.TryParse(ChatIdTextBox.Text.Trim(), out var parsedChatId))
            {
                MessageBox.Show("Supergroup chat id must be a number (like -1001234567890).", "Settings");
                return;
            }

            chatId = parsedChatId;
        }

        if (OwnerIdTextBox.Text.Trim().Length > 0)
        {
            if (!long.TryParse(OwnerIdTextBox.Text.Trim(), out var parsedOwnerId))
            {
                MessageBox.Show("Your Telegram user id must be a number.", "Settings");
                return;
            }

            ownerId = parsedOwnerId;
        }

        var updated = OrchestratorConfig_Factory.Create(
            _config.Repos,
            Null_IfEmpty(SupervisorModelTextBox.Text),
            Null_IfEmpty(ImplementerModelTextBox.Text),
            Null_IfEmpty(GeneralModelTextBox.Text),
            chatId,
            ownerId,
            Null_IfEmpty(BotTokenTextBox.Text));

        OrchestratorConfig_Loader.Save(updated, _paths);
        Close();
    }

    void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    static string? Null_IfEmpty(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
