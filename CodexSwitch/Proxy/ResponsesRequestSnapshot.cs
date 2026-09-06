using System.Text;

namespace CodexSwitch.Proxy;

public sealed class ResponsesRequestSnapshot : IDisposable
{
    private JsonDocument? _document;

    private ResponsesRequestSnapshot(
        byte[] body,
        string? requestModel,
        bool stream,
        string? previousResponseId,
        string? eventType,
        bool store,
        bool isObject)
    {
        BodyBytes = body;
        RequestModel = requestModel;
        Stream = stream;
        PreviousResponseId = previousResponseId;
        EventType = eventType;
        Store = store;
        IsObject = isObject;
    }

    public byte[] BodyBytes { get; }

    public ReadOnlyMemory<byte> Body => BodyBytes;

    public string? RequestModel { get; }

    public bool Stream { get; }

    public string? PreviousResponseId { get; }

    public string? EventType { get; }

    public bool Store { get; }

    public bool IsObject { get; }

    public JsonDocument RequestDocument => _document ??= JsonDocument.Parse(Body);

    public JsonElement RootElement => RequestDocument.RootElement;

    public static async Task<ResponsesRequestSnapshot> ReadAsync(
        Stream body,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.TryGetBuffer(out var segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length
            ? segment.Array
            : buffer.ToArray();

        return Parse(bytes);
    }

    public static ResponsesRequestSnapshot Parse(string json)
    {
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    public static ResponsesRequestSnapshot Parse(byte[] body)
    {
        var metadata = ExtractMetadata(body);
        return new ResponsesRequestSnapshot(
            body,
            metadata.RequestModel,
            metadata.Stream,
            metadata.PreviousResponseId,
            metadata.EventType,
            metadata.Store,
            metadata.IsObject);
    }

    public static ResponsesRequestSnapshot FromDocument(JsonDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            document.RootElement.WriteTo(writer);
        }

        return Parse(stream.ToArray());
    }

    public void Dispose()
    {
        _document?.Dispose();
        _document = null;
    }

    private static SnapshotMetadata ExtractMetadata(ReadOnlySpan<byte> body)
    {
        var reader = new Utf8JsonReader(body, isFinalBlock: true, state: default);
        if (!reader.Read())
            throw new JsonException("Invalid JSON body.");

        string? requestModel = null;
        string? previousResponseId = null;
        string? eventType = null;
        var stream = false;
        var store = true;

        var isObject = reader.TokenType == JsonTokenType.StartObject;
        if (isObject)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                    continue;

                if (reader.ValueTextEquals("model"u8))
                {
                    ReadValue(ref reader);
                    if (reader.TokenType == JsonTokenType.String)
                        requestModel = reader.GetString();
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("stream"u8))
                {
                    ReadValue(ref reader);
                    stream = reader.TokenType == JsonTokenType.True;
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("previous_response_id"u8))
                {
                    ReadValue(ref reader);
                    if (reader.TokenType == JsonTokenType.String)
                        previousResponseId = reader.GetString();
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("type"u8))
                {
                    ReadValue(ref reader);
                    if (reader.TokenType == JsonTokenType.String)
                        eventType = reader.GetString();
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("store"u8))
                {
                    ReadValue(ref reader);
                    store = reader.TokenType != JsonTokenType.False;
                    SkipNestedValue(ref reader);
                    continue;
                }

                ReadValue(ref reader);
                SkipNestedValue(ref reader);
            }
        }
        else
        {
            SkipNestedValue(ref reader);
        }

        while (reader.Read())
        {
        }

        return new SnapshotMetadata(requestModel, stream, previousResponseId, eventType, store, isObject);
    }

    private static void ReadValue(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            throw new JsonException("Invalid JSON body.");
    }

    private static void SkipNestedValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
    }

    private readonly record struct SnapshotMetadata(
        string? RequestModel,
        bool Stream,
        string? PreviousResponseId,
        string? EventType,
        bool Store,
        bool IsObject);
}
