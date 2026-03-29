using System.Text.Json;

namespace Helper.Runtime.Generation;

public sealed class BlueprintJsonSchemaValidator : IBlueprintJsonSchemaValidator
{
    public string SchemaJson => """
    {
      "type": "object",
      "required": ["ProjectName", "RootNamespace", "Files", "NuGetPackages"],
      "properties": {
        "ProjectName": { "type": "string", "minLength": 1 },
        "RootNamespace": { "type": "string", "minLength": 1 },
        "NuGetPackages": { "type": "array", "items": { "type": "string" } },
        "Files": {
          "type": "array",
          "minItems": 1,
          "items": {
            "type": "object",
            "required": ["Path", "Purpose", "Role", "Dependencies"],
            "properties": {
              "Path": { "type": "string", "minLength": 1 },
              "Purpose": { "type": "string", "minLength": 1 },
              "Role": { "type": ["string", "number"] },
              "Dependencies": { "type": "array", "items": { "type": "string" } },
              "Methods": {
                "type": "array",
                "items": {
                  "type": "object",
                  "required": ["Name", "Signature", "Purpose", "ContextHints"],
                  "properties": {
                    "Name": { "type": "string", "minLength": 1 },
                    "Signature": { "type": "string", "minLength": 1 },
                    "Purpose": { "type": "string", "minLength": 1 },
                    "ContextHints": { "type": "string" }
                  }
                }
              }
            }
          }
        }
      }
    }
    """;

    public SchemaValidationResult ValidateRawJson(string? rawJson)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errors.Add("Blueprint payload is empty.");
            return new SchemaValidationResult(false, errors);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (Exception ex)
        {
            errors.Add($"Blueprint JSON parse failed: {ex.Message}");
            return new SchemaValidationResult(false, errors);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Blueprint root must be a JSON object.");
                return new SchemaValidationResult(false, errors);
            }

            ValidateRequiredString(document.RootElement, "ProjectName", errors);
            ValidateRequiredString(document.RootElement, "RootNamespace", errors);
            ValidateRequiredArray(document.RootElement, "NuGetPackages", errors, minItems: 0);
            ValidateRequiredArray(document.RootElement, "Files", errors, minItems: 1);

            if (document.RootElement.TryGetProperty("Files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var file in files.EnumerateArray())
                {
                    if (file.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"Files[{index}] must be an object.");
                        index++;
                        continue;
                    }

                    ValidateRequiredString(file, "Path", errors, $"Files[{index}]");
                    ValidateRequiredString(file, "Purpose", errors, $"Files[{index}]");
                    ValidateRequiredProperty(file, "Role", errors, $"Files[{index}]");
                    ValidateRequiredArray(file, "Dependencies", errors, minItems: 0, parent: $"Files[{index}]");

                    if (file.TryGetProperty("Methods", out var methods))
                    {
                        if (methods.ValueKind != JsonValueKind.Array)
                        {
                            errors.Add($"Files[{index}].Methods must be an array.");
                        }
                        else
                        {
                            var methodIndex = 0;
                            foreach (var method in methods.EnumerateArray())
                            {
                                if (method.ValueKind != JsonValueKind.Object)
                                {
                                    errors.Add($"Files[{index}].Methods[{methodIndex}] must be an object.");
                                    methodIndex++;
                                    continue;
                                }

                                ValidateRequiredString(method, "Name", errors, $"Files[{index}].Methods[{methodIndex}]");
                                ValidateRequiredString(method, "Signature", errors, $"Files[{index}].Methods[{methodIndex}]");
                                ValidateRequiredString(method, "Purpose", errors, $"Files[{index}].Methods[{methodIndex}]");
                                ValidateRequiredProperty(method, "ContextHints", errors, $"Files[{index}].Methods[{methodIndex}]");
                                methodIndex++;
                            }
                        }
                    }

                    index++;
                }
            }
        }

        return new SchemaValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateRequiredString(JsonElement element, string propertyName, List<string> errors, string? parent = null)
    {
        var context = parent is null ? propertyName : $"{parent}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            errors.Add($"Missing required property: {context}.");
            return;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            errors.Add($"Property {context} must be a non-empty string.");
        }
    }

    private static void ValidateRequiredArray(JsonElement element, string propertyName, List<string> errors, int minItems, string? parent = null)
    {
        var context = parent is null ? propertyName : $"{parent}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            errors.Add($"Missing required property: {context}.");
            return;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Property {context} must be an array.");
            return;
        }

        if (property.GetArrayLength() < minItems)
        {
            errors.Add($"Property {context} must contain at least {minItems} item(s).");
        }
    }

    private static void ValidateRequiredProperty(JsonElement element, string propertyName, List<string> errors, string? parent = null)
    {
        var context = parent is null ? propertyName : $"{parent}.{propertyName}";
        if (!element.TryGetProperty(propertyName, out _))
        {
            errors.Add($"Missing required property: {context}.");
        }
    }
}

