using System.Text.Json;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Phase 11 of a2a-native-messaging — refresh-fidelity integration test.
///
/// <para>
/// Drives a canned sequence of A2A payloads through a real
/// <see cref="A2AEventStore"/> + <see cref="A2AToAGUITranslator"/> +
/// <see cref="SessionEventIngestor"/>, captures the live envelopes broadcast
/// during playback, then reads the store directly and re-translates — asserting
/// the two envelope sequences are elementwise value-equal. This is the core
/// "live == refresh" invariant the whole change was built around.
/// </para>
/// </summary>
[TestFixture]
public class RefreshFidelityTests
{
    private const string ProjectId = "proj-refresh";
    private const string SessionId = "session-refresh";

    private string _baseDir = null!;
    private A2AEventStore _store = null!;
    private A2AToAGUITranslator _translator = null!;
    private Mock<IClientProxy> _groupClient = null!;
    private Mock<IHubContext<ClaudeCodeHub>> _hub = null!;
    private SessionEventIngestor _ingestor = null!;
    private List<SessionEventEnvelope> _liveEnvelopes = null!;

    [SetUp]
    public void SetUp()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "refresh-fidelity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);

        _store = new A2AEventStore(_baseDir, NullLogger<A2AEventStore>.Instance);
        _translator = new A2AToAGUITranslator(new PendingToolCallRegistry());

        _groupClient = new Mock<IClientProxy>();
        _liveEnvelopes = new List<SessionEventEnvelope>();
        _groupClient
            .Setup(c => c.SendCoreAsync(
                AGUIEventType.ReceiveSessionEvent,
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) =>
            {
                // args: [sessionId, envelope]
                if (args.Length >= 2 && args[1] is SessionEventEnvelope env)
                {
                    _liveEnvelopes.Add(env);
                }
            })
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupClient.Object);
        _hub = new Mock<IHubContext<ClaudeCodeHub>>();
        _hub.SetupGet(h => h.Clients).Returns(hubClients.Object);

        _ingestor = new SessionEventIngestor(
            _store, _translator, _hub.Object, NullLogger<SessionEventIngestor>.Instance,
            new NullServiceProvider(),
            SessionEventIngestorTests.BuildDebugOptions(fullMessages: false));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    /// <summary>A small representative turn — Task submitted, agent text, tool use, tool result, status completed.</summary>
    private static (string Kind, JsonElement Payload)[] CannedTurn()
    {
        JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        return new[]
        {
            ("task", Parse("""
            {
                "id": "task-1",
                "contextId": "ctx-1",
                "status": { "state": "submitted" },
                "kind": "task"
            }
            """)),

            ("message", Parse("""
            {
                "kind": "message",
                "messageId": "msg-assistant-1",
                "role": "agent",
                "parts": [ { "kind": "text", "text": "On it." } ],
                "contextId": "ctx-1",
                "taskId": "task-1"
            }
            """)),

            ("message", Parse("""
            {
                "kind": "message",
                "messageId": "msg-tool-use-1",
                "role": "agent",
                "parts": [
                    {
                        "kind": "data",
                        "metadata": { "homespunBlockType": "tool_use" },
                        "data": {
                            "toolUseId": "tool-1",
                            "toolName": "Read",
                            "input": { "path": "README.md" }
                        }
                    }
                ],
                "contextId": "ctx-1",
                "taskId": "task-1"
            }
            """)),

            ("message", Parse("""
            {
                "kind": "message",
                "messageId": "msg-tool-result-1",
                "role": "user",
                "parts": [
                    {
                        "kind": "data",
                        "metadata": { "homespunBlockType": "tool_result" },
                        "data": {
                            "toolUseId": "tool-1",
                            "content": "# Readme\n\nHello world."
                        }
                    }
                ],
                "contextId": "ctx-1",
                "taskId": "task-1"
            }
            """)),

            ("status-update", Parse("""
            {
                "taskId": "task-1",
                "contextId": "ctx-1",
                "status": { "state": "completed" },
                "final": true,
                "kind": "status-update"
            }
            """)),
        };
    }

    [Test]
    public async Task Live_Equals_Replay_After_Full_Playback()
    {
        foreach (var (kind, payload) in CannedTurn())
        {
            await _ingestor.IngestAsync(ProjectId, SessionId, kind, payload);
        }

        // Replay: re-read the store and re-translate, producing the same envelope sequence.
        var replay = await ReplayFromStoreAsync(since: 0);

        AssertEnvelopesEquivalent(_liveEnvelopes, replay);
    }

    [Test]
    public async Task MidPlayback_Refresh_Reconstructs_Full_Stream()
    {
        var payloads = CannedTurn();

        // Feed the first two events, then simulate a mid-playback refresh that captures
        // the live envelopes so far and fetches seq > 2 from the replay endpoint.
        foreach (var (kind, payload) in payloads.Take(2))
        {
            await _ingestor.IngestAsync(ProjectId, SessionId, kind, payload);
        }

        var liveBeforeRefresh = _liveEnvelopes.ToList();
        var lastSeenSeq = liveBeforeRefresh.Max(e => e.Seq);

        // Continue the playback — live broadcasts keep coming while the replay fetch is
        // in flight. The refresh is asked for seq > lastSeenSeq, so it must see only the
        // tail envelopes.
        foreach (var (kind, payload) in payloads.Skip(2))
        {
            await _ingestor.IngestAsync(ProjectId, SessionId, kind, payload);
        }

        var replayTail = await ReplayFromStoreAsync(since: lastSeenSeq);
        var reconstructed = liveBeforeRefresh.Concat(replayTail).ToList();

        AssertEnvelopesEquivalent(_liveEnvelopes, reconstructed);
    }

    [Test]
    public async Task Live_Equals_Replay_When_Worker_Omits_ToolUseId()
    {
        // Regression for issue rqyT8G: prior to the fix the translator minted fresh Guids
        // for missing toolUseId on every translation pass, so live and replay envelopes
        // for the same A2A record diverged on the toolCallId and (cascaded) on
        // parentMessageId-derived ids. This payload deliberately omits toolUseId so the
        // FallbackToolCallId path (derived from the stored EventId + part index) is the
        // one under test. (messageId is required by the A2A SDK schema, so its fallback
        // is unreachable in practice — the relevant reachable cases are toolUseId on
        // tool_use blocks, and the always-synthetic toolCallId / question ids inside
        // BuildInputRequired.)
        //
        // The `input` value is written without internal whitespace so that the live
        // payload (parsed with whitespace-preserving JsonDocument.Parse) and the replay
        // payload (round-tripped through canonical JsonSerializer.Serialize on disk)
        // produce a byte-identical TOOL_CALL_ARGS delta. That whitespace canonicalisation
        // is independent of this issue.
        JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        var payloads = new (string Kind, JsonElement Payload)[]
        {
            ("task", Parse("""
            {
                "id": "task-1",
                "contextId": "ctx-1",
                "status": { "state": "submitted" },
                "kind": "task"
            }
            """)),

            ("message", Parse("""
            {
                "kind": "message",
                "messageId": "msg-tool-use",
                "role": "agent",
                "parts": [
                    {
                        "kind": "data",
                        "metadata": { "kind": "tool_use" },
                        "data": { "toolName": "Read", "input":{"path":"README.md"} }
                    }
                ],
                "contextId": "ctx-1",
                "taskId": "task-1",
                "metadata": { "sdkMessageType": "assistant" }
            }
            """)),

            ("status-update", Parse("""
            {
                "taskId": "task-1",
                "contextId": "ctx-1",
                "status": { "state": "completed" },
                "final": true,
                "kind": "status-update"
            }
            """)),
        };

        foreach (var (kind, payload) in payloads)
        {
            await _ingestor.IngestAsync(ProjectId, SessionId, kind, payload);
        }

        var replay = await ReplayFromStoreAsync(since: 0);
        AssertEnvelopesEquivalent(_liveEnvelopes, replay);

        // Sanity: confirm the live==replay equivalence is the intended effect — the
        // toolCallId on the synthesised TOOL_CALL_START must be a derived string keyed
        // off the stored EventId, not a fresh Guid that just happened to coincide.
        var toolStart = _liveEnvelopes
            .Select(e => e.Event)
            .OfType<ToolCallStartEvent>()
            .Single();
        Assert.That(toolStart.ToolCallId, Is.Not.Empty);
        Assert.That(Guid.TryParse(toolStart.ToolCallId, out _), Is.False,
            $"fallback toolCallId must be a derived string, not a fresh Guid (got '{toolStart.ToolCallId}')");
        Assert.That(toolStart.ToolCallId, Does.Contain("-tool-"),
            "fallback toolCallId must include the part-index suffix from FallbackToolCallId");
    }

    [Test]
    public async Task ModeFull_Replay_Yields_Identical_Envelopes_As_Incremental_From_Zero()
    {
        foreach (var (kind, payload) in CannedTurn())
        {
            await _ingestor.IngestAsync(ProjectId, SessionId, kind, payload);
        }

        var incremental = await ReplayFromStoreAsync(since: 0);
        var full = await ReplayFromStoreAsync(since: null); // mirrors ?mode=full

        AssertEnvelopesEquivalent(incremental, full);
        // And both are equivalent to the live stream — client dedup by eventId keeps
        // rendering pixel-identical even under the Full replay mode.
        AssertEnvelopesEquivalent(_liveEnvelopes, full);
    }

    [Test]
    public async Task Missing_MessageId_Produces_Identical_Live_And_Replay_Payloads()
    {
        // An agent message with no messageId triggers the Guid.NewGuid() fallback path.
        // The deterministic fallback must produce the same id on live and replay.
        JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        var agentMessageNoId = Parse("""
        {
            "kind": "message",
            "role": "agent",
            "parts": [ { "kind": "text", "text": "Hello with no id." } ],
            "contextId": "ctx-1",
            "taskId": "task-1"
        }
        """);

        await _ingestor.IngestAsync(ProjectId, SessionId, "message", agentMessageNoId);

        var replay = await ReplayFromStoreAsync(since: 0);

        AssertEnvelopesEquivalent(_liveEnvelopes, replay);
    }

    [Test]
    public async Task Missing_ToolUseId_Produces_Identical_Live_And_Replay_Payloads()
    {
        // An agent message with a tool_use block but no toolUseId triggers the fallback.
        // The deterministic fallback must produce the same toolCallId on live and replay.
        JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        var toolUseNoId = Parse("""
        {
            "kind": "message",
            "messageId": "msg-tool-no-id",
            "role": "agent",
            "parts": [
                {
                    "kind": "data",
                    "metadata": { "homespunBlockType": "tool_use" },
                    "data": {
                        "toolName": "Read",
                        "input": { "path": "README.md" }
                    }
                }
            ],
            "contextId": "ctx-1",
            "taskId": "task-1"
        }
        """);

        await _ingestor.IngestAsync(ProjectId, SessionId, "message", toolUseNoId);

        var replay = await ReplayFromStoreAsync(since: 0);

        AssertEnvelopesEquivalent(_liveEnvelopes, replay);
    }

    // ------------------ Helpers ------------------

    private async Task<List<SessionEventEnvelope>> ReplayFromStoreAsync(long? since)
    {
        var records = await _store.ReadAsync(SessionId, since);
        Assert.That(records, Is.Not.Null, "ReadAsync should not return null for an existing session");

        var envelopes = new List<SessionEventEnvelope>();
        foreach (var record in records!)
        {
            var parsed = A2AMessageParser.ParseSseEvent(record.EventKind, record.Payload.GetRawText());
            var ctx = new TranslationContext(SessionId, RunId: SessionId, EventId: record.EventId);
            IEnumerable<AGUIBaseEvent> aguiEvents = parsed is null
                ? new[] { AGUIEventFactory.CreateCustomEvent(AGUICustomEventName.Raw, new { original = record.Payload }) }
                : _translator.Translate(parsed, ctx);

            foreach (var agui in aguiEvents)
            {
                envelopes.Add(new SessionEventEnvelope(record.Seq, record.SessionId, record.EventId, agui));
            }
        }
        return envelopes;
    }

    private static void AssertEnvelopesEquivalent(
        IReadOnlyList<SessionEventEnvelope> left,
        IReadOnlyList<SessionEventEnvelope> right)
    {
        Assert.That(right, Has.Count.EqualTo(left.Count),
            $"Envelope count mismatch: live={left.Count}, replay={right.Count}");
        for (var i = 0; i < left.Count; i++)
        {
            Assert.That(right[i].Seq, Is.EqualTo(left[i].Seq), $"seq mismatch at index {i}");
            Assert.That(right[i].EventId, Is.EqualTo(left[i].EventId), $"eventId mismatch at index {i}");
            Assert.That(right[i].SessionId, Is.EqualTo(left[i].SessionId), $"sessionId mismatch at index {i}");
            // Compare the translated AG-UI event payload structurally. Timestamps on the
            // translated AG-UI events are generated at translation time (once for live,
            // again for replay) so they will differ by construction — strip before
            // comparing. Everything else (type, messageId, text, toolCallId, args, …)
            // must match byte-for-byte.
            Assert.That(
                CanonicalEventJson(right[i].Event),
                Is.EqualTo(CanonicalEventJson(left[i].Event)),
                $"event payload mismatch at index {i}");
        }
    }

    private static string CanonicalEventJson(AGUIBaseEvent evt)
    {
        var raw = JsonSerializer.Serialize(evt);
        using var doc = JsonDocument.Parse(raw);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            WriteStripped(doc.RootElement, writer);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static readonly HashSet<string> NondeterministicFields =
        new(StringComparer.Ordinal) { "timestamp" };

    private static void WriteStripped(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    if (NondeterministicFields.Contains(prop.Name)) continue;
                    writer.WritePropertyName(prop.Name);
                    WriteStripped(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteStripped(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
