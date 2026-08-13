using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace STUWard;

internal static class ManagedWardReportService
{
    private const string RequestWardReportRpc = "STUWard_RequestWardReport";
    private const string ReceiveWardReportRpc = "STUWard_ReceiveWardReport";
    private const string WardReportConsoleCommand = "stuw_wardreport";
    private const int MaxReportBytes = 2 * 1024 * 1024;
    private const int MaxResponseMessageLength = 1024;
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<long, DateTime> LastRequestUtcBySender = new();

    private static bool _rpcsRegistered;
    private static bool _consoleCommandRegistered;

    internal static void ResetRuntimeState()
    {
        _rpcsRegistered = false;
        LastRequestUtcBySender.Clear();
    }

    internal static void ForgetSender(long senderUid)
    {
        if (senderUid != 0L)
        {
            LastRequestUtcBySender.Remove(senderUid);
        }
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

        RegisterRpcs();
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
            return;
        }

        var now = DateTime.UtcNow;
        if (LastRequestUtcBySender.TryGetValue(sender, out var lastRequestUtc) &&
            now >= lastRequestUtc &&
            now - lastRequestUtc < MinimumRequestInterval)
        {
            return;
        }

        LastRequestUtcBySender[sender] = now;

        var accountId = WardOwnership.GetPlayerAccountId(playerId);
        if (!WardAdminDebugAccess.IsAdminAccountId(accountId))
        {
            Plugin.Log.LogWarning($"Rejected ward report request from non-admin playerId={playerId} accountId='{accountId}'.");
            SendWardReportResponse(sender, success: false, string.Empty, 0, 0, 0, "Ward report is only available to server admins.");
            return;
        }

        if (WardOwnership.TryBuildWardCountReport(out var reportContents, out var trackedAccounts, out var totalWards, out var unresolvedOwners))
        {
            reportContents ??= string.Empty;
            if (Encoding.UTF8.GetByteCount(reportContents) > MaxReportBytes)
            {
                Plugin.Log.LogWarning(
                    $"Ward report for admin playerId={playerId} exceeds the {MaxReportBytes}-byte response limit.");
                SendWardReportResponse(
                    sender,
                    success: false,
                    string.Empty,
                    0,
                    0,
                    0,
                    "The ward report is too large to transfer. Generate it from the server console instead.");
                return;
            }

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

        if (message.Length > MaxResponseMessageLength || Encoding.UTF8.GetByteCount(reportContents) > MaxReportBytes)
        {
            Plugin.Log.LogWarning($"{Plugin.ModName}: rejected an oversized ward report response.");
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

        reportContents ??= string.Empty;
        message ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(reportContents) > MaxReportBytes || message.Length > MaxResponseMessageLength)
        {
            success = false;
            reportContents = string.Empty;
            trackedAccounts = 0;
            totalWards = 0;
            unresolvedOwners = 0;
            message = "The ward report response exceeded its transfer limit.";
        }

        var pkg = new ZPackage();
        pkg.Write(success);
        pkg.Write(trackedAccounts);
        pkg.Write(totalWards);
        pkg.Write(unresolvedOwners);
        pkg.Write(message);
        pkg.Write(reportContents);
        routedRpc.InvokeRoutedRPC(receiverUid, ReceiveWardReportRpc, pkg);
    }
}
