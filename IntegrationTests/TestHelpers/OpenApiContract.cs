using System.Text.Json;

namespace IntegrationTests.TestHelpers;

// Checks real responses against Contracts/Api.approved.json, the approved copy of the endpoint overview the app is built against.
// The copy is only updated on purpose, so a change to an endpoint makes the tests fail until it has been approved.
// Checks status codes, field names and JSON types. Formats and patterns are not checked
public static class OpenApiContract
{
    private static readonly JsonElement Document = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "Api.approved.json"))).RootElement;

    public static async Task AssertMatchesAsync(HttpResponseMessage response, string method, string pathTemplate)
    {
        string statusCode = ((int)response.StatusCode).ToString();
        JsonElement responses = Document.GetProperty("paths").GetProperty(pathTemplate)
            .GetProperty(method.ToLowerInvariant()).GetProperty("responses");

        Assert.True(responses.TryGetProperty(statusCode, out JsonElement documented),
            $"{method} {pathTemplate} returned {statusCode}, which is not in Contracts/Api.approved.json");

        string body = await response.Content.ReadAsStringAsync();

        if (!documented.TryGetProperty("content", out JsonElement content))
        {
            Assert.Equal("", body);
            return;
        }

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        JsonElement schema = content.GetProperty("application/json").GetProperty("schema");
        AssertMatchesSchema(JsonDocument.Parse(body).RootElement, schema, "$");
    }

    private static void AssertMatchesSchema(JsonElement value, JsonElement schema, string location)
    {
        if (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            // "#/components/schemas/ErrorResponse" -> ErrorResponse
            string name = reference.GetString()!.Split('/')[^1];
            schema = Document.GetProperty("components").GetProperty("schemas").GetProperty(name);
        }

        string[] allowedTypes = AllowedTypes(schema);
        string actualType = JsonType(value);
        Assert.True(allowedTypes.Contains(actualType),
            $"{location} is {actualType}, but Contracts/Api.approved.json allows {string.Join(" or ", allowedTypes)}");

        if (actualType == "object")
        {
            JsonElement properties = schema.GetProperty("properties");

            if (schema.TryGetProperty("required", out JsonElement required))
                foreach (JsonElement name in required.EnumerateArray())
                    Assert.True(value.TryGetProperty(name.GetString()!, out _), $"{location} is missing {name}");

            foreach (JsonProperty property in value.EnumerateObject())
            {
                Assert.True(properties.TryGetProperty(property.Name, out JsonElement propertySchema),
                    $"{location}.{property.Name} is not in Contracts/Api.approved.json");
                AssertMatchesSchema(property.Value, propertySchema, $"{location}.{property.Name}");
            }
        }
        else if (actualType == "array")
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
                AssertMatchesSchema(item, schema.GetProperty("items"), $"{location}[{index++}]");
        }
    }

    // "type" is either one type or a list, e.g. ["integer", "string"] or ["null", "string"]
    private static string[] AllowedTypes(JsonElement schema)
    {
        JsonElement type = schema.GetProperty("type");
        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()!).ToArray()
            : [type.GetString()!];
    }

    private static string JsonType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => value.TryGetInt64(out _) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "undefined"
    };
}
