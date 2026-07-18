using System;
using System.IO;

namespace STUWard;

internal static class ManagedWardReportService
{
    private const string RequestWardReportRpc = "STUWard_RequestWardReport";
    private const string ReceiveWardReportRpc = "STUWard_ReceiveWardReport";
    private const string WardReportConsoleCommand = "stuw_wardreport";

    private static bool _rpcsRegistered;
    private static bool _consoleCommandRegistered;

    internal static void OnZNetAwake()
    {
        _rpcsRegistered = false;
    }

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register(RequestWardReportRpc, new Action<long>(HandleRequestWardReport));
        routedRpc.Register<ZPackage>(ReceiveWardReportRpc, HandleReceiveWardReport);
        _rpcsRegistered = true;
    }

    internal static bool TryHandleConsoleCommand(Terminal? terminal, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text?.Trim() ?? string.Empty;
        if (!trimmed.Equals(WardReportConsoleCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ZNet.instance == null)
        {
            terminal?.AddString($"{Plugin.ModName}: ward report is not available right now.");
            return true;
        }

        if (ZNet.instance.IsServer())
        {
            WriteWardReportToTerminal(terminal);
            return true;
        }

        WardOwnership.RegisterRpcs();
        terminal?.AddString($"{Plugin.ModName}: requested ward report generation on the server.");
        ZRoutedRpc.instance?.InvokeRoutedRPC(RequestWardReportRpc);
        return true;
    }

    internal static void EnsureConsoleCommandRegistered(Terminal? terminal)
    {
        if (_consoleCommandRegistered)
        {
            AddCommandToAutocomplete(terminal);
            return;
        }

        _ = new Terminal.ConsoleCommand(
            WardReportConsoleCommand,
            "Generate the STUWard ward ownership/count report.",
            new Terminal.ConsoleEvent(args => { TryHandleConsoleCommand(args.Context, args.FullLine); }));
        _consoleCommandRegistered = true;
        AddCommandToAutocomplete(terminal);
    }

    private static void AddCommandToAutocomplete(Terminal? terminal)
    {
        if (terminal == null || terminal.m_commandList == null)
        {
            return;
        }

        if (terminal.m_commandList.Contains(WardReportConsoleCommand))
        {
            return;
        }

        terminal.m_commandList.Add(WardReportConsoleCommand);
        terminal.m_commandList.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteWardReportToTerminal(Terminal? terminal)
    {
        if (WardOwnership.TryWriteWardCountReport(out var reportPath, out var trackedAccounts, out var totalWards, out var unresolvedOwners))
        {
            terminal?.AddString($"{Plugin.ModName}: wrote ward report to {reportPath}");
            terminal?.AddString($"{Plugin.ModName}: tracked accounts={trackedAccounts}, total wards={totalWards}, unresolved owner wards={unresolvedOwners}");
        }
        else
        {
            terminal?.AddString($"{Plugin.ModName}: failed to write ward report. Check the log for details.");
        }
    }

    private static void HandleRequestWardReport(long sender)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out var playerId))
        {
            SendWardReportResponse(sender, success: false, string.Empty, 0, 0, 0, "Could not resolve the requesting player on the server.");
            return;
        }

        var accountId = WardOwnership.GetPlayerAccountId(playerId);
        if (!WardAdminDebugAccess.IsAdminAccountId(accountId))
        {
            Plugin.Log.LogWarning($"Rejected ward report request from non-admin playerId={playerId} accountId='{accountId}'.");
            SendWardReportResponse(sender, success: false, string.Empty, 0, 0, 0, "Ward report is only available to server admins.");
            return;
        }

        if (WardOwnership.TryBuildWardCountReport(out var reportContents, out var trackedAccounts, out var totalWards, out var unresolvedOwners))
        {
            Plugin.Log.LogInfo(
                $"Prepared ward report for admin playerId={playerId}. tracked accounts={trackedAccounts}, total wards={totalWards}, unresolved owner wards={unresolvedOwners}");
            SendWardReportResponse(sender, success: true, reportContents, trackedAccounts, totalWards, unresolvedOwners, string.Empty);
        }
        else
        {
            Plugin.Log.LogWarning($"Failed to build ward report for admin playerId={playerId}.");
            SendWardReportResponse(sender, success: false, string.Empty, 0, 0, 0, "Failed to generate ward report on the server. Check the server log for details.");
        }
    }

    private static void HandleReceiveWardReport(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            return;
        }

        bool success;
        int trackedAccounts;
        int totalWards;
        int unresolvedOwners;
        string message;
        string reportContents;
        try
        {
            success = pkg.ReadBool();
            trackedAccounts = pkg.ReadInt();
            totalWards = pkg.ReadInt();
            unresolvedOwners = pkg.ReadInt();
            message = pkg.ReadString();
            reportContents = pkg.ReadString();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"{Plugin.ModName}: failed to read ward report response: {exception.Message}");
            return;
        }

        if (!success)
        {
            Plugin.Log.LogWarning($"{Plugin.ModName}: {message}");
            return;
        }

        var reportPath = WardOwnership.GetReportFilePath();
        try
        {
            File.WriteAllText(reportPath, reportContents);
            Plugin.Log.LogInfo($"{Plugin.ModName}: wrote ward report to {reportPath}");
            Plugin.Log.LogInfo($"{Plugin.ModName}: tracked accounts={trackedAccounts}, total wards={totalWards}, unresolved owner wards={unresolvedOwners}");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"{Plugin.ModName}: failed to write ward report to {reportPath}: {exception.Message}");
        }
    }

    private static void SendWardReportResponse(long receiverUid, bool success, string reportContents, int trackedAccounts, int totalWards, int unresolvedOwners, string message)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(success);
        pkg.Write(trackedAccounts);
        pkg.Write(totalWards);
        pkg.Write(unresolvedOwners);
        pkg.Write(message ?? string.Empty);
        pkg.Write(reportContents ?? string.Empty);
        routedRpc.InvokeRoutedRPC(receiverUid, ReceiveWardReportRpc, pkg);
    }
}
