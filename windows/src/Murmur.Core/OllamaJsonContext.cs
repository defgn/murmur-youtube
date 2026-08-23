using System.Text.Json.Serialization;

namespace Murmur.Core;

// The Ollama HTTP contract. Kept out of OllamaCleaner so the source-generated context has
// no partial-container requirement to fight. Every member carries an explicit JSON name —
// Ollama speaks lowercase, and positional records would serialize PascalCase.

/// <summary>Request body for /api/chat.</summary>
internal sealed record OllamaChatRequest(string Model, IReadOnlyList<OllamaChatMessage> Messages)
{
    [JsonPropertyName("model")] public string Model { get; } = Model;
    [JsonPropertyName("messages")] public IReadOnlyList<OllamaChatMessage> Messages { get; } = Messages;
    [JsonPropertyName("stream")] public bool Stream { get; } = false;
    [JsonPropertyName("options")] public OllamaChatOptions Options { get; } = new();
}

/// <summary>One chat turn.</summary>
internal sealed record OllamaChatMessage(string Role, string Content)
{
    [JsonPropertyName("role")] public string Role { get; } = Role;
    [JsonPropertyName("content")] public string Content { get; } = Content;
}

/// <summary>Greedy, deterministic decoding.</summary>
internal sealed record OllamaChatOptions
{
    [JsonPropertyName("temperature")] public double Temperature { get; } = 0;
}

/// <summary>Response body from /api/chat.</summary>
internal sealed record OllamaChatResponse(OllamaChatMessage? Message)
{
    [JsonPropertyName("message")] public OllamaChatMessage? Message { get; } = Message;
}

/// <summary>Response body from /api/tags.</summary>
internal sealed record OllamaTagsResponse(List<OllamaModelInfo>? Models)
{
    [JsonPropertyName("models")] public List<OllamaModelInfo>? Models { get; } = Models;
}

/// <summary>One installed model.</summary>
internal sealed record OllamaModelInfo(string Name)
{
    [JsonPropertyName("name")] public string Name { get; } = Name;
}

/// <summary>Source-generated serializer for the Ollama wire format.</summary>
[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaChatResponse))]
[JsonSerializable(typeof(OllamaTagsResponse))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext;
