#if DEBUG
using System;
using System.IO;
using System.Text;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Jaunt.Core.Capture;
using Lumina.Excel.Sheets;

namespace Jaunt.Capture;

/// <summary>
/// Dev builds only: writes every shout and yell line the player hears to
/// <c>chat-capture/shout-yell-&lt;start time&gt;.jsonl</c> in the plugin's config folder, for the
/// shout/yell data-source spike. Senders are replaced by per-session pseudonyms.
/// </summary>
internal sealed class ShoutYellCapture : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly SenderKeys senderKeys = new();
    private readonly JsonLinesWriter<CapturedChatLine> writer;

    public ShoutYellCapture(IChatGui chatGui, IClientState clientState, IPlayerState playerState,
        IDataManager dataManager, IPluginLog log, string configDirectory)
    {
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.dataManager = dataManager;
        this.log = log;

        var directory = Path.Combine(configDirectory, "chat-capture");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"shout-yell-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        writer = new JsonLinesWriter<CapturedChatLine>(
            new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));

        chatGui.ChatMessage += OnChatMessage;
        log.Information($"Recording shout and yell to {path}");
    }

    // Runs on the game thread, and Dalamud reuses the message object: copy the fields out now and
    // queue them; the writer writes elsewhere
    private void OnChatMessage(IHandleableChatMessage message)
    {
        var kind = message.LogKind;
        if (kind is not (XivChatType.Shout or XivChatType.Yell))
            return;

        try
        {
            var world = playerState.CurrentWorld;
            var territoryId = clientState.TerritoryType;
            var territoryName = dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?
                .PlaceName.ValueNullable?.Name.ExtractText() ?? "";

            writer.TryWrite(new CapturedChatLine(
                CapturedAt: DateTimeOffset.Now,
                Channel: kind.ToString(),
                SenderKey: senderKeys.For(message.Sender.TextValue),
                WorldId: world.RowId,
                WorldName: world.ValueNullable?.Name.ExtractText() ?? "",
                TerritoryId: territoryId,
                TerritoryName: territoryName,
                Message: message.Message.TextValue,
                MessageRaw: message.Message.Encode()));
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't record a shout or yell line");
        }
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;

        try
        {
#pragma warning disable VSTHRD002 // Blocks unload briefly to write what's still queued; worth it for a dev-only capture
            writer.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception e)
        {
            log.Error(e, "Shout/yell capture failed; the file may be missing lines");
        }
    }
}
#endif
