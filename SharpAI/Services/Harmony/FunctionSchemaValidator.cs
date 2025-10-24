using SharpAI.Models;
using System.Text.Json;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Provides validation for function definitions according to OpenAI specification
/// </summary>
public static class FunctionSchemaValidator
{
    /// <summary>
    /// Validates a function definition according to OpenAI specification
    /// </summary>
    /// <param name="functionDef">The function definition to validate</param>
    /// <returns>A list of validation errors, empty if valid</returns>
    public static List<string> ValidateFunctionDefinition(FunctionDefinition functionDef)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(functionDef.Name))
        {
            errors.Add("Function name is required");
        }

        if (functionDef.Parameters != null)
        {
            ValidateParameters(functionDef.Parameters, functionDef.Strict == true, errors);
        }

        return errors;
    }

    /// <summary>
    /// Validates function parameters according to OpenAI JSON schema requirements
    /// </summary>
    private static void ValidateParameters(FunctionParameters parameters, bool isStrict, List<string> errors)
    {
        if (parameters.Type != "object")
        {
            errors.Add("Function parameters must have type 'object'");
        }

        if (isStrict)
        {
            // Strict mode requirements according to OpenAI spec
            if (parameters.AdditionalProperties != false)
            {
                errors.Add("Strict mode requires additionalProperties to be false");
            }

            // All properties must be in required array for strict mode
            if (parameters.Properties != null && parameters.Required != null)
            {
                foreach (var propertyName in parameters.Properties.Keys)
                {
                    if (!parameters.Required.Contains(propertyName))
                    {
                        errors.Add($"Strict mode requires all properties to be in required array. Missing: {propertyName}");
                    }
                }
            }
            else if (parameters.Properties != null && parameters.Properties.Count > 0)
            {
                errors.Add("Strict mode requires all properties to be marked as required");
            }

            // Validate nested objects
            if (parameters.Properties != null)
            {
                foreach (var (propertyName, propertySchema) in parameters.Properties)
                {
                    ValidatePropertySchema(propertyName, propertySchema, isStrict, errors);
                }
            }
        }
    }

    /// <summary>
    /// Validates a property schema recursively
    /// </summary>
    private static void ValidatePropertySchema(string propertyName, PropertySchema schema, bool isStrict, List<string> errors)
    {
        if (schema.Type == null)
        {
            errors.Add($"Property '{propertyName}' must have a type specified");
            return;
        }

        // Handle union types (e.g., ["string", "null"])
        if (schema.Type is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            var typeArray = JsonSerializer.Deserialize<string[]>(jsonElement.GetRawText());
            if (typeArray == null || typeArray.Length == 0)
            {
                errors.Add($"Property '{propertyName}' has invalid type array");
            }
        }
        else if (schema.Type is string typeString)
        {
            ValidateBasicType(propertyName, typeString, errors);
        }

        // Validate nested objects
        if (schema.Properties != null)
        {
            if (isStrict && schema.AdditionalProperties != false)
            {
                errors.Add($"Nested object '{propertyName}' in strict mode must have additionalProperties set to false");
            }

            foreach (var (nestedPropertyName, nestedSchema) in schema.Properties)
            {
                ValidatePropertySchema($"{propertyName}.{nestedPropertyName}", nestedSchema, isStrict, errors);
            }
        }

        // Validate array items
        if (schema.Items != null)
        {
            ValidatePropertySchema($"{propertyName}[]", schema.Items, isStrict, errors);
        }
    }

    /// <summary>
    /// Validates basic JSON schema types
    /// </summary>
    private static void ValidateBasicType(string propertyName, string type, List<string> errors)
    {
        var validTypes = new HashSet<string>
        {
            "string", "number", "integer", "boolean", "array", "object", "null"
        };

        if (!validTypes.Contains(type))
        {
            errors.Add($"Property '{propertyName}' has invalid type: {type}");
        }
    }

    /// <summary>
    /// Validates a list of tool definitions
    /// </summary>
    public static Dictionary<string, List<string>> ValidateToolDefinitions(List<ToolDefinition> tools)
    {
        var validationResults = new Dictionary<string, List<string>>();

        foreach (var tool in tools)
        {
            if (tool.Function != null)
            {
                var errors = ValidateFunctionDefinition(tool.Function);
                if (errors.Count > 0)
                {
                    validationResults[tool.Function.Name] = errors;
                }
            }
        }

        return validationResults;
    }
}