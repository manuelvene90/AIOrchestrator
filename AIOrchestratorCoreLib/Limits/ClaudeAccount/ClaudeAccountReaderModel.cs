namespace AIOrchestratorCoreLib.Limits.ClaudeAccount;

internal sealed class ClaudeAccountReaderModel(string? globalConfigPath) : IClaudeAccountReader
{
    readonly string? _globalConfigPath = globalConfigPath;

    public string? Read_AccountId_OrNull()
    {
        if (_globalConfigPath == null)
            return null;

        return ClaudeAccount_Parser.Read_AccountId_OrNull(Usage.UsageTotals_Reader.Read_Text_Safe(_globalConfigPath));
    }
}
