using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModernTubeDownloader.Batch;

/// <summary>Strict streaming envelopes; at most one bounded row is materialized, never the whole urls/items array.</summary>
internal static class BatchJsonReader
{
    private enum Stage { Root, Rows, EnvelopeArray, EnvelopeEnd, End }

    internal static async IAsyncEnumerable<JsonElement> ReadAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new byte[65_537];
        var buffered = 0; var final = false; var stage = Stage.Root; var envelope = false; var bomChecked = false;
        var state = new JsonReaderState(new JsonReaderOptions { MaxDepth = 8 });
        while (true)
        {
            if (!bomChecked && !final && buffered < 3)
            {
                var prefixRead = await stream.ReadAsync(buffer.AsMemory(buffered), token).ConfigureAwait(false);
                if (prefixRead == 0) final = true; else buffered += prefixRead;
                continue;
            }
            if (!bomChecked && (buffered >= 3 || final))
            {
                if (buffer.AsSpan(0, buffered).StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
                { buffer.AsSpan(3, buffered - 3).CopyTo(buffer); buffered -= 3; }
                bomChecked = true;
            }
            JsonElement? row; int consumed; bool needMore;
            try { Step(buffer.AsSpan(0, buffered), final, ref state, ref stage, ref envelope, out consumed, out row, out needMore); }
            catch (JsonException error) { throw new BatchJsonFormatException(error); }
            if (consumed > 0)
            {
                buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
                buffered -= consumed;
            }
            if (row is { } value) yield return value;
            if (!needMore) continue;
            if (final)
            {
                if (stage != Stage.End || buffered != 0) throw new BatchJsonFormatException(new JsonException("Incomplete JSON envelope."));
                yield break;
            }
            if (buffered == buffer.Length) throw new InvalidDataException("Batch.Error.RecordSize");
            var read = await stream.ReadAsync(buffer.AsMemory(buffered), token).ConfigureAwait(false);
            if (read == 0) final = true; else buffered += read;
        }
    }

    private static void Step(ReadOnlySpan<byte> bytes, bool final, ref JsonReaderState state, ref Stage stage,
        ref bool envelope, out int consumed, out JsonElement? row, out bool needMore)
    {
        consumed = 0; row = null; needMore = true;
        var reader = new Utf8JsonReader(bytes, final, state);
        var before = reader;
        if (!reader.Read()) { consumed = (int)reader.BytesConsumed; state = reader.CurrentState; return; }
        switch (stage)
        {
            case Stage.Root:
                if (reader.TokenType == JsonTokenType.StartArray) stage = Stage.Rows;
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var lookahead = reader;
                    if (!lookahead.Read()) return;
                    if (lookahead.TokenType == JsonTokenType.PropertyName && (lookahead.ValueTextEquals("urls") || lookahead.ValueTextEquals("items")))
                    { reader = lookahead; envelope = true; stage = Stage.EnvelopeArray; }
                    else
                    {
                        if (!JsonDocument.TryParseValue(ref reader, out var document)) return;
                        using (document)
                        {
                            if (!document.RootElement.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String)
                                throw new JsonException("Single-object import requires a string url.");
                            row = document.RootElement.Clone();
                        }
                        stage = Stage.End;
                    }
                }
                else throw new JsonException("Expected an array, a url object, or a urls/items envelope.");
                break;
            case Stage.EnvelopeArray:
                if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("urls/items must be an array.");
                stage = Stage.Rows;
                break;
            case Stage.Rows:
                if (reader.TokenType == JsonTokenType.EndArray) stage = envelope ? Stage.EnvelopeEnd : Stage.End;
                else
                {
                    if (!JsonDocument.TryParseValue(ref reader, out var document)) return;
                    using (document) row = document.RootElement.Clone();
                }
                break;
            case Stage.EnvelopeEnd:
                if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Envelope contains an unexpected or duplicate field.");
                stage = Stage.End;
                break;
            case Stage.End: throw new JsonException("Unexpected trailing JSON value.");
        }
        if (row is not null && reader.BytesConsumed - before.BytesConsumed > 65_536)
            throw new InvalidDataException("Batch.Error.RecordSize");
        consumed = (int)reader.BytesConsumed; state = reader.CurrentState; needMore = false;
    }
}

/// <summary>Technical parser detail stays in the log; UI uses a fixed localized schema explanation.</summary>
public sealed class BatchJsonFormatException(Exception inner) : FormatException("Batch.Error.JsonFormat", inner);
