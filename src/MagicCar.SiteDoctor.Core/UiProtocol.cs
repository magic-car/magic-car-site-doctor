using System.Text.Json;
using MagicCar.SiteDoctor.Models;

namespace MagicCar.SiteDoctor;

public sealed record UiCommand(string Type, RecoveryActionId? Action = null);
public static class UiProtocol
{
    public const string Origin = "https://site-doctor.local";
    public const string DocumentUrl = Origin + "/index.html";
    public static bool IsDocument(string? source) => string.Equals(source, DocumentUrl, StringComparison.Ordinal);
    public static UiCommand? Parse(string json, string source)
    {
        if (!IsDocument(source) || json.Length > 256) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 2 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;
            var properties = root.EnumerateObject().Select(p => p.Name).ToArray();
            var text = type.GetString();
            if (text is "ready" or "runDiagnostics" or "openPrinterQueue")
                return properties.Length == 1 ? new(text) : null;
            if (text != "repair" || properties.Length != 2 || !root.TryGetProperty("action", out var field) || field.ValueKind != JsonValueKind.String) return null;
            if (!Enum.TryParse<RecoveryActionId>(field.GetString(), out var action) || !Enum.IsDefined(action) || field.GetString() != action.ToString()) return null;
            return new("repair", action);
        }
        catch (JsonException) { return null; }
    }
}
