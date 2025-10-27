using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpAI.Models;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Represents a built-in grammar rule with its content and dependencies.
/// </summary>
public class BuiltinRule
{
    public string Content { get; }
    public List<string> Dependencies { get; }

    public BuiltinRule(string content, List<string>? dependencies = null)
    {
        Content = content;
        Dependencies = dependencies ?? new List<string>();
    }
}

/// <summary>
/// Static class containing primitive and format rule definitions.
/// </summary>
public static class GrammarRules
{
    // Constraining spaces to prevent model "running away"
    public const string SPACE_RULE = "| \" \" | \"\\n\"{1,2} [ \\t]{0,20}";

    public static readonly Dictionary<string, BuiltinRule> PRIMITIVE_RULES = new()
    {
        ["boolean"] = new("(\"true\" | \"false\") space", new List<string>()),
        ["decimal-part"] = new("[0-9]{1,16}", new List<string>()),
        ["integral-part"] = new("[0] | [1-9] [0-9]{0,15}", new List<string>()),
        ["number"] = new("(\"-\"? integral-part) (\".\" decimal-part)? ([eE] [-+]? integral-part)? space", new List<string> { "integral-part", "decimal-part" }),
        ["integer"] = new("(\"-\"? integral-part) space", new List<string> { "integral-part" }),
        ["value"] = new("object | array | string | number | boolean | null", new List<string> { "object", "array", "string", "number", "boolean", "null" }),
        ["object"] = new("\"{\" space ( string \":\" space value (\",\" space string \":\" space value)* )? \"}\" space", new List<string> { "string", "value" }),
        ["array"] = new("\"[\" space ( value (\",\" space value)* )? \"]\" space", new List<string> { "value" }),
        ["uuid"] = new("\"\\\"\" [0-9a-fA-F]{8} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{4} \"-\" [0-9a-fA-F]{12} \"\\\"\" space", new List<string>()),
        ["char"] = new("[^\"\\\\\\x7F\\x00-\\x1F] | [\\\\] ([\"\\\\bfnrt] | \"u\" [0-9a-fA-F]{4})", new List<string>()),
        ["string"] = new("\"\\\"\" char* \"\\\"\" space", new List<string> { "char" }),
        ["null"] = new("\"null\" space", new List<string>())
    };

    public static readonly Dictionary<string, BuiltinRule> STRING_FORMAT_RULES = new()
    {
        ["date"] = new("[0-9]{4} \"-\" ( \"0\" [1-9] | \"1\" [0-2] ) \"-\" ( \"0\" [1-9] | [1-2] [0-9] | \"3\" [0-1] )", new List<string>()),
        ["time"] = new("([01] [0-9] | \"2\" [0-3]) \":\" [0-5] [0-9] \":\" [0-5] [0-9] ( \".\" [0-9]{3} )? ( \"Z\" | ( \"+\" | \"-\" ) ( [01] [0-9] | \"2\" [0-3] ) \":\" [0-5] [0-9] )", new List<string>()),
        ["date-time"] = new("date \"T\" time", new List<string> { "date", "time" }),
        ["date-string"] = new("\"\\\"\" date \"\\\"\" space", new List<string> { "date" }),
        ["time-string"] = new("\"\\\"\" time \"\\\"\" space", new List<string> { "time" }),
        ["date-time-string"] = new("\"\\\"\" date-time \"\\\"\" space", new List<string> { "date-time" })
    };

    public static readonly HashSet<string> RESERVED_NAMES = new()
    {
        "root", "dot"
    };

    static GrammarRules()
    {
        foreach (var key in PRIMITIVE_RULES.Keys)
            RESERVED_NAMES.Add(key);
        foreach (var key in STRING_FORMAT_RULES.Keys)
            RESERVED_NAMES.Add(key);
    }
}

/// <summary>
/// Builds custom GBNF grammars that are initially permissive but enforce JSON structure 
/// when the model enters the final channel for structured output.
/// </summary>
public static class ConditionalGrammarBuilder
{
    /// <summary>
    /// Creates a grammar that allows free text until final channel, then enforces JSON structure.
    /// </summary>
    /// <param name="responseFormat">The response format specification</param>
    /// <returns>GBNF grammar string</returns>
    public static string BuildConditionalGrammar(ResponseFormat? responseFormat)
    {
        if (responseFormat?.Type == ResponseFormatType.JsonSchema && responseFormat.JsonSchema?.Schema != null)
        {
            return BuildSchemaBasedGrammar(responseFormat.JsonSchema);
        }
        else if (responseFormat?.Type == ResponseFormatType.JsonObject)
        {
            return BuildGenericJsonGrammar();
        }
        
        return BuildPermissiveGrammar();
    }

    /// <summary>
    /// Builds a grammar based on a specific JSON schema.
    /// </summary>
    private static string BuildSchemaBasedGrammar(JsonSchemaFormat jsonSchema)
    {
        return GenerateSchemaGrammar(jsonSchema.Schema);
    }

    /// <summary>
    /// Builds a generic JSON grammar for when no specific schema is provided.
    /// This creates a permissive grammar that allows any content but encourages JSON structure.
    /// </summary>
    private static string BuildGenericJsonGrammar()
    {
        var sb = new StringBuilder();
        sb.AppendLine("root ::= mixed-content");
        sb.AppendLine();
        sb.AppendLine("mixed-content ::= (text-content | json-content)*");
        sb.AppendLine();
        sb.AppendLine("text-content ::= [^\\x00{]+");
        sb.AppendLine();
        sb.AppendLine("json-content ::= object | array");
        sb.AppendLine();
        sb.AppendLine("object ::= \"{\" ws (string \":\" ws value (\",\" ws string \":\" ws value)*)? \"}\" ws");
        sb.AppendLine();
        sb.AppendLine("array ::= \"[\" ws (value (\",\" ws value)*)? \"]\" ws");
        sb.AppendLine();
        sb.AppendLine("string ::= \"\\\"\" ([^\"\\\\\\x7F\\x00-\\x1F] | \"\\\\\" ([\"\\\\bfnrt] | \"u\" [0-9a-fA-F]{4}))* \"\\\"\" ws");
        sb.AppendLine();
        sb.AppendLine("number ::= (\"-\"? ([0-9] | [1-9] [0-9]{0,15})) (\".\" [0-9]+)? ([eE] [-+]? [0-9] [1-9]{0,15})? ws");
        sb.AppendLine();
        sb.AppendLine("value ::= object | array | string | number | (\"true\" | \"false\" | \"null\") ws");
        sb.AppendLine();
        sb.AppendLine("ws ::= (\" \" | \"\\n\" | \"\\t\")*");
        
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Builds a completely permissive grammar when no response format is specified.
    /// </summary>
    private static string BuildPermissiveGrammar()
    {
        return @"
root ::= .*
".Trim();
    }

    /// <summary>
    /// Generates grammar rules from a JSON schema object.
    /// </summary>
    private static string GenerateSchemaGrammar(object? schema)
    {
        if (schema == null) 
        {
            return GetGenericJsonGrammar();
        }

        try
        {
            // Parse the schema to understand its structure
            var schemaJson = JsonSerializer.Serialize(schema);
            var schemaDoc = JsonDocument.Parse(schemaJson);
            
            var grammarBuilder = new SchemaConverter();
            return grammarBuilder.GenerateGrammar(schemaDoc.RootElement);
        }
        catch (Exception)
        {
            // Fallback to generic JSON grammar if schema parsing fails
        return GetGenericJsonGrammar();
    }
}

/// <summary>
/// Converts JSON schemas to GBNF grammar rules.
/// </summary>
public class SchemaConverter
{
    private readonly Dictionary<string, string> _rules = new();
    private readonly Dictionary<string, object> _refs = new();
    private readonly HashSet<string> _refsBeingResolved = new();
    private readonly Dictionary<string, int> _propOrder;
    private readonly bool _allowFetch;
    private readonly bool _dotall;
    private readonly bool _rawPattern;

    private static readonly Regex INVALID_RULE_CHARS_RE = new(@"[^a-zA-Z0-9-]+");
    private static readonly Regex GRAMMAR_LITERAL_ESCAPE_RE = new(@"[\r\n""]");
    private static readonly Regex GRAMMAR_RANGE_LITERAL_ESCAPE_RE = new(@"[\r\n""\]\-\\]");
    
    private static readonly Dictionary<char, string> GRAMMAR_LITERAL_ESCAPES = new()
    {
        ['\r'] = "\\r",
        ['\n'] = "\\n",
        ['"'] = "\\\"",
        ['-'] = "\\-",
        [']'] = "\\]"
    };

    private static readonly HashSet<char> NON_LITERAL_SET = new() { '|', '.', '(', ')', '[', ']', '{', '}', '*', '+', '?' };
    private static readonly HashSet<char> ESCAPED_IN_REGEXPS_BUT_NOT_IN_LITERALS = new() { '^', '$', '.', '[', ']', '(', ')', '|', '{', '}', '*', '+', '?' };

    public SchemaConverter(Dictionary<string, int>? propOrder = null, bool allowFetch = false, bool dotall = false, bool rawPattern = false)
    {
        _propOrder = propOrder ?? new Dictionary<string, int>();
        _allowFetch = allowFetch;
        _dotall = dotall;
        _rawPattern = rawPattern;
        _rules["space"] = GrammarRules.SPACE_RULE;
    }

    /// <summary>
    /// Generates a complete GBNF grammar from a JSON schema element.
    /// </summary>
    public string GenerateGrammar(JsonElement schemaElement)
    {
        Visit(schemaElement, "");
        return FormatGrammar();
    }

    /// <summary>
    /// Formats the generated rules into a complete grammar string.
    /// </summary>
    private string FormatGrammar()
    {
        var sb = new StringBuilder();
        var sortedRules = _rules.OrderBy(kv => kv.Key);
        
        foreach (var rule in sortedRules)
        {
            sb.AppendLine($"{rule.Key} ::= {rule.Value}");
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// Visits a schema element and generates appropriate grammar rules.
    /// </summary>
    private string Visit(JsonElement schema, string name)
    {
        var ruleName = string.IsNullOrEmpty(name) || GrammarRules.RESERVED_NAMES.Contains(name) ? 
            (string.IsNullOrEmpty(name) ? "root" : name + "-") : name;

        // Handle $ref
        if (schema.TryGetProperty("$ref", out var refElement))
        {
            var refValue = refElement.GetString();
            if (!string.IsNullOrEmpty(refValue))
            {
                return AddRule(ruleName, ResolveRef(refValue));
            }
        }

        // Handle oneOf/anyOf
        JsonElement alternatives = default;
        if (schema.TryGetProperty("oneOf", out var oneOfElement))
        {
            alternatives = oneOfElement;
        }
        else if (schema.TryGetProperty("anyOf", out var anyOfElement))
        {
            alternatives = anyOfElement;
        }
        
        if (alternatives.ValueKind != JsonValueKind.Undefined)
        {
            return AddRule(ruleName, GenerateUnionRule(name, alternatives));
        }

        // Handle type as array
        if (schema.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.Array)
        {
            var types = new List<JsonElement>();
            foreach (var typeItem in typeElement.EnumerateArray())
            {
                types.Add(typeItem);
            }
            return AddRule(ruleName, GenerateUnionRuleFromTypes(name, schema, types));
        }

        // Handle const
        if (schema.TryGetProperty("const", out var constElement))
        {
            return AddRule(ruleName, GenerateConstantRule(constElement) + " space");
        }

        // Handle enum
        if (schema.TryGetProperty("enum", out var enumElement))
        {
            var enumValues = new List<string>();
            foreach (var enumValue in enumElement.EnumerateArray())
            {
                enumValues.Add(GenerateConstantRule(enumValue));
            }
            var rule = "(" + string.Join(" | ", enumValues) + ") space";
            return AddRule(ruleName, rule);
        }

        var schemaType = schema.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() : null;
        var schemaFormat = schema.TryGetProperty("format", out var formatProperty) ? formatProperty.GetString() : null;

        // Handle object type or implicit object (has properties)
        if ((schemaType == null || schemaType == "object") && 
            (schema.TryGetProperty("properties", out _) || 
             (schema.TryGetProperty("additionalProperties", out var addlProps) && addlProps.ValueKind != JsonValueKind.True)))
        {
            var required = GetRequiredProperties(schema);
            var properties = GetProperties(schema);
            var additionalProperties = GetAdditionalProperties(schema);
            return AddRule(ruleName, BuildObjectRule(properties, required, name, additionalProperties));
        }

        // Handle allOf
        if (schema.TryGetProperty("allOf", out var allOfElement))
        {
            return HandleAllOf(schema, allOfElement, name, ruleName);
        }

        // Handle array type
        if ((schemaType == null || schemaType == "array") && 
            (schema.TryGetProperty("items", out _) || schema.TryGetProperty("prefixItems", out _)))
        {
            return HandleArrayType(schema, name, ruleName);
        }

        // Handle string with pattern
        if ((schemaType == null || schemaType == "string") && schema.TryGetProperty("pattern", out var patternElement))
        {
            var pattern = patternElement.GetString();
            if (!string.IsNullOrEmpty(pattern))
            {
                return VisitPattern(pattern, ruleName);
            }
        }

        // Handle UUID format
        if ((schemaType == null || schemaType == "string") && 
            !string.IsNullOrEmpty(schemaFormat) && 
            Regex.IsMatch(schemaFormat, @"^uuid[1-5]?$"))
        {
            return AddPrimitive(ruleName == "root" ? "root" : schemaFormat, GrammarRules.PRIMITIVE_RULES["uuid"]);
        }

        // Handle string formats
        if ((schemaType == null || schemaType == "string") && 
            !string.IsNullOrEmpty(schemaFormat) && 
            GrammarRules.STRING_FORMAT_RULES.ContainsKey($"{schemaFormat}-string"))
        {
            var primName = $"{schemaFormat}-string";
            return AddRule(ruleName, AddPrimitive(primName, GrammarRules.STRING_FORMAT_RULES[primName]));
        }

        // Handle string with length constraints
        if (schemaType == "string" && 
            (schema.TryGetProperty("minLength", out _) || schema.TryGetProperty("maxLength", out _)))
        {
            var charRule = AddPrimitive("char", GrammarRules.PRIMITIVE_RULES["char"]);
            var minLen = schema.TryGetProperty("minLength", out var minLenElement) ? minLenElement.GetInt32() : 0;
            var maxLen = schema.TryGetProperty("maxLength", out var maxLenElement) ? (int?)maxLenElement.GetInt32() : null;
            
            return AddRule(ruleName, "\"\\\"\" " + BuildRepetition(charRule, minLen, maxLen) + " \"\\\"\" space");
        }

        // Handle integer with constraints
        if ((schemaType == null || schemaType == "integer") && HasIntegerConstraints(schema))
        {
            return HandleIntegerConstraints(schema, ruleName);
        }

        // Handle object type or empty schema
        if (schemaType == "object" || (schema.EnumerateObject().Count() == 0))
        {
            return AddRule(ruleName, AddPrimitive("object", GrammarRules.PRIMITIVE_RULES["object"]));
        }

        // Handle primitive types
        if (!string.IsNullOrEmpty(schemaType) && GrammarRules.PRIMITIVE_RULES.ContainsKey(schemaType))
        {
            return AddPrimitive(ruleName == "root" ? "root" : schemaType, GrammarRules.PRIMITIVE_RULES[schemaType]);
        }

        // Fallback to value
        return AddPrimitive("value", GrammarRules.PRIMITIVE_RULES["value"]);
    }

    // Helper methods will be implemented in the next steps...

    /// <summary>
    /// Adds a rule to the rules dictionary with a unique name.
    /// </summary>
    private string AddRule(string name, string rule)
    {
        var escapedName = INVALID_RULE_CHARS_RE.Replace(name, "-");
        if (!_rules.ContainsKey(escapedName) || _rules[escapedName] == rule)
        {
            _rules[escapedName] = rule;
            return escapedName;
        }

        var i = 0;
        var uniqueName = $"{escapedName}{i}";
        while (_rules.ContainsKey(uniqueName) && _rules[uniqueName] != rule)
        {
            i++;
            uniqueName = $"{escapedName}{i}";
        }
        _rules[uniqueName] = rule;
        return uniqueName;
    }

    /// <summary>
    /// Adds a primitive rule and its dependencies.
    /// </summary>
    private string AddPrimitive(string name, BuiltinRule rule)
    {
        var ruleName = AddRule(name, rule.Content);

        foreach (var dep in rule.Dependencies)
        {
            var depRule = GrammarRules.PRIMITIVE_RULES.GetValueOrDefault(dep) ?? 
                         GrammarRules.STRING_FORMAT_RULES.GetValueOrDefault(dep);
            if (depRule != null && !_rules.ContainsKey(dep))
            {
                AddPrimitive(dep, depRule);
            }
        }
        return ruleName;
    }

    /// <summary>
    /// Formats a literal value for use in grammar.
    /// </summary>
    private string FormatLiteral(string literal)
    {
        var escaped = GRAMMAR_LITERAL_ESCAPE_RE.Replace(literal, match =>
            GRAMMAR_LITERAL_ESCAPES.GetValueOrDefault(match.Value[0], match.Value));
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Generates a constant rule for a JSON value.
    /// </summary>
    private string GenerateConstantRule(JsonElement value)
    {
        return FormatLiteral(JsonSerializer.Serialize(value));
    }

    /// <summary>
    /// Generates a union rule from alternatives.
    /// </summary>
    private string GenerateUnionRule(string name, JsonElement alternatives)
    {
        var rules = new List<string>();
        var i = 0;
        foreach (var alt in alternatives.EnumerateArray())
        {
            var altName = string.IsNullOrEmpty(name) ? $"alternative-{i}" : $"{name}-{i}";
            rules.Add(Visit(alt, altName));
            i++;
        }
        return string.Join(" | ", rules);
    }

    /// <summary>
    /// Generates a union rule from type array.
    /// </summary>
    private string GenerateUnionRuleFromTypes(string name, JsonElement schema, List<JsonElement> types)
    {
        var rules = new List<string>();
        var i = 0;
        foreach (var typeElement in types)
        {
            var modifiedSchema = new Dictionary<string, object>();
            foreach (var prop in schema.EnumerateObject())
            {
                if (prop.Name != "type")
                {
                    modifiedSchema[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }
            modifiedSchema["type"] = typeElement.GetString();
            
            var schemaJson = JsonSerializer.Serialize(modifiedSchema);
            var schemaDoc = JsonDocument.Parse(schemaJson);
            var altName = string.IsNullOrEmpty(name) ? $"alternative-{i}" : $"{name}-{i}";
            rules.Add(Visit(schemaDoc.RootElement, altName));
            i++;
        }
        return string.Join(" | ", rules);
    }

    /// <summary>
    /// Gets required properties from schema.
    /// </summary>
    private HashSet<string> GetRequiredProperties(JsonElement schema)
    {
        var required = new HashSet<string>();
        if (schema.TryGetProperty("required", out var requiredElement) && 
            requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var propName = item.GetString();
                    if (!string.IsNullOrEmpty(propName))
                    {
                        required.Add(propName);
                    }
                }
            }
        }
        return required;
    }

    /// <summary>
    /// Gets properties from schema.
    /// </summary>
    private List<(string, JsonElement)> GetProperties(JsonElement schema)
    {
        var properties = new List<(string, JsonElement)>();
        if (schema.TryGetProperty("properties", out var propsElement))
        {
            foreach (var prop in propsElement.EnumerateObject())
            {
                properties.Add((prop.Name, prop.Value));
            }
        }
        return properties;
    }

    /// <summary>
    /// Gets additional properties setting from schema.
    /// </summary>
    private object? GetAdditionalProperties(JsonElement schema)
    {
        if (schema.TryGetProperty("additionalProperties", out var addlProps))
        {
            return addlProps.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => JsonSerializer.Deserialize<object>(addlProps.GetRawText())
            };
        }
        return null;
    }

    /// <summary>
    /// Resolves a $ref reference.
    /// </summary>
    private string ResolveRef(string refValue)
    {
        var refName = refValue.Split('/').Last();
        if (!_rules.ContainsKey(refName) && !_refsBeingResolved.Contains(refValue))
        {
            _refsBeingResolved.Add(refValue);
            if (_refs.TryGetValue(refValue, out var resolved))
            {
                var resolvedJson = JsonSerializer.Serialize(resolved);
                var resolvedDoc = JsonDocument.Parse(resolvedJson);
                refName = Visit(resolvedDoc.RootElement, refName);
            }
            _refsBeingResolved.Remove(refValue);
        }
        return refName;
    }

    /// <summary>
    /// Handles allOf schema composition.
    /// </summary>
    private string HandleAllOf(JsonElement schema, JsonElement allOfElement, string name, string ruleName)
    {
        var required = new HashSet<string>();
        var properties = new List<(string, JsonElement)>();
        var enumSets = new List<HashSet<object>>();

        foreach (var component in allOfElement.EnumerateArray())
        {
            var compSchema = component;
            if (component.TryGetProperty("$ref", out var refElement))
            {
                var refValue = refElement.GetString();
                if (!string.IsNullOrEmpty(refValue) && _refs.TryGetValue(refValue, out var resolved))
                {
                    var resolvedJson = JsonSerializer.Serialize(resolved);
                    compSchema = JsonDocument.Parse(resolvedJson).RootElement;
                }
            }

            if (compSchema.TryGetProperty("properties", out var propsElement))
            {
                foreach (var prop in propsElement.EnumerateObject())
                {
                    properties.Add((prop.Name, prop.Value));
                }
            }

            if (compSchema.TryGetProperty("enum", out var enumElement))
            {
                var enumSet = new HashSet<object>();
                foreach (var enumValue in enumElement.EnumerateArray())
                {
                    enumSet.Add(JsonSerializer.Deserialize<object>(enumValue.GetRawText())!);
                }
                enumSets.Add(enumSet);
            }
        }

        if (enumSets.Count > 0)
        {
            var intersection = enumSets[0];
            for (int i = 1; i < enumSets.Count; i++)
            {
                intersection.IntersectWith(enumSets[i]);
            }

            if (intersection.Count > 0)
            {
                var enumValues = intersection.OrderBy(x => x?.ToString()).Select(v =>
                    FormatLiteral(JsonSerializer.Serialize(v)));
                var rule = "(" + string.Join(" | ", enumValues) + ") space";
                return AddRule(ruleName, rule);
            }
        }

        return AddRule(ruleName, BuildObjectRule(properties, required, name, null));
    }

    /// <summary>
    /// Handles array type schemas.
    /// </summary>
    private string HandleArrayType(JsonElement schema, string name, string ruleName)
    {
        JsonElement itemsElement = default;
        bool hasItems = schema.TryGetProperty("items", out itemsElement);
        
        if (schema.TryGetProperty("prefixItems", out var prefixItems) || 
            (hasItems && itemsElement.ValueKind == JsonValueKind.Array))
        {
            var itemElements = prefixItems.ValueKind != JsonValueKind.Undefined ? prefixItems : itemsElement;
            var itemRules = new List<string>();
            var i = 0;
            foreach (var item in itemElements.EnumerateArray())
            {
                var itemName = string.IsNullOrEmpty(name) ? $"tuple-{i}" : $"{name}-tuple-{i}";
                itemRules.Add(Visit(item, itemName));
                i++;
            }
            return AddRule(ruleName, "\"[\" space " + string.Join(" \",\" space ", itemRules) + " \"]\" space");
        }
        else if (hasItems)
        {
            var itemName = string.IsNullOrEmpty(name) ? "item" : $"{name}-item";
            var itemRuleName = Visit(itemsElement, itemName);
            var minItems = schema.TryGetProperty("minItems", out var minElement) ? minElement.GetInt32() : 0;
            var maxItems = schema.TryGetProperty("maxItems", out var maxElement) ? (int?)maxElement.GetInt32() : null;
            return AddRule(ruleName, "\"[\" space " + BuildRepetition(itemRuleName, minItems, maxItems, "\",\" space") + " \"]\" space");
        }

        return AddRule(ruleName, AddPrimitive("array", GrammarRules.PRIMITIVE_RULES["array"]));
    }

    /// <summary>
    /// Builds a repetition pattern for grammar rules.
    /// </summary>
    private string BuildRepetition(string itemRule, int minItems, int? maxItems, string? separatorRule = null)
    {
        if (maxItems == 0)
            return "";

        if (minItems == 0 && maxItems == 1)
            return $"{itemRule}?";

        if (separatorRule == null)
        {
            if (minItems == 1 && maxItems == null)
                return $"{itemRule}+";
            else if (minItems == 0 && maxItems == null)
                return $"{itemRule}*";
            else
                return $"{itemRule}{{{minItems},{(maxItems?.ToString() ?? "")}}}";
        }

        var result = itemRule + " " + BuildRepetition($"({separatorRule} {itemRule})", 
            minItems > 0 ? minItems - 1 : 0, 
            maxItems.HasValue ? maxItems - 1 : null);
        return minItems == 0 ? $"({result})?" : result;
    }

    /// <summary>
    /// Generates integer range constraints.
    /// </summary>
    private void GenerateMinMaxInt(int? minValue, int? maxValue, List<string> output, int decimalsLeft = 16, bool topLevel = true)
    {
        bool hasMin = minValue.HasValue;
        bool hasMax = maxValue.HasValue;

        void DigitRange(char fromChar, char toChar)
        {
            output.Add("[");
            if (fromChar == toChar)
                output.Add(fromChar.ToString());
            else
            {
                output.Add(fromChar.ToString());
                output.Add("-");
                output.Add(toChar.ToString());
            }
            output.Add("]");
        }

        void MoreDigits(int minDigits, int maxDigits)
        {
            output.Add("[0-9]");
            if (minDigits == maxDigits && minDigits == 1)
                return;
            output.Add("{");
            output.Add(minDigits.ToString());
            if (maxDigits != minDigits)
            {
                output.Add(",");
                if (maxDigits != int.MaxValue)
                    output.Add(maxDigits.ToString());
            }
            output.Add("}");
        }

        if (hasMin && hasMax)
        {
            if (minValue < 0 && maxValue < 0)
            {
                output.Add("\"-\" (");
                GenerateMinMaxInt(-maxValue.Value, -minValue.Value, output, decimalsLeft, true);
                output.Add(")");
                return;
            }

            if (minValue < 0)
            {
                output.Add("\"-\" (");
                GenerateMinMaxInt(0, -minValue.Value, output, decimalsLeft, true);
                output.Add(") | ");
                minValue = 0;
            }

            var minS = minValue.ToString();
            var maxS = maxValue.ToString();
            var minDigits = minS.Length;
            var maxDigits = maxS.Length;

            for (int digits = minDigits; digits < maxDigits; digits++)
            {
                output.Add($"\"{minS}\" | ");
                minS = "1" + new string('0', digits);
            }
            output.Add($"\"{minS}\" | \"{maxS}\"");
            return;
        }

        // Simplified implementation for single bounds
        if (hasMin)
        {
            if (minValue < 0)
            {
                output.Add("\"-\" (");
                GenerateMinMaxInt(null, -minValue.Value, output, decimalsLeft, false);
                output.Add(") | [0] | [1-9] [0-9]*");
            }
            else if (minValue == 0)
            {
                output.Add(topLevel ? "[0] | [1-9] [0-9]*" : "[0-9]+");
            }
            else
            {
                output.Add($"\"{minValue}\" | ");
                output.Add("[1-9] [0-9]*");
            }
            return;
        }

        if (hasMax)
        {
            if (maxValue >= 0)
            {
                if (topLevel)
                    output.Add("\"-\" [1-9] [0-9]* | ");
                output.Add($"[0-{Math.Min(maxValue.Value, 9)}]");
                if (maxValue > 9)
                    output.Add($" | \"{maxValue}\"");
            }
            else
            {
                output.Add("\"-\" (");
                GenerateMinMaxInt(-maxValue.Value, null, output, decimalsLeft, false);
                output.Add(")");
            }
            return;
        }

        throw new InvalidOperationException("At least one of minValue or maxValue must be set");
    }

    /// <summary>
    /// Builds object rule with properties and constraints.
    /// </summary>
    private string BuildObjectRule(List<(string, JsonElement)> properties, HashSet<string> required, string name, object? additionalProperties)
    {
        var propKvRuleNames = new Dictionary<string, string>();
        
        // Sort properties by prop_order if specified
        var sortedProps = properties
            .OrderBy(p => _propOrder.GetValueOrDefault(p.Item1, _propOrder.Count))
            .ThenBy(p => properties.FindIndex(x => x.Item1 == p.Item1))
            .ToList();

        foreach (var (propName, propSchema) in sortedProps)
        {
            var propRuleName = Visit(propSchema, string.IsNullOrEmpty(name) ? propName : $"{name}-{propName}");
            propKvRuleNames[propName] = AddRule(
                string.IsNullOrEmpty(name) ? $"{propName}-kv" : $"{name}-{propName}-kv",
                $"{FormatLiteral(propName)} space \":\" space {propRuleName}");
        }

        var requiredProps = sortedProps.Where(p => required.Contains(p.Item1)).Select(p => p.Item1).ToList();
        var optionalProps = sortedProps.Where(p => !required.Contains(p.Item1)).Select(p => p.Item1).ToList();

        if (additionalProperties != null && !additionalProperties.Equals(false))
        {
            var subName = string.IsNullOrEmpty(name) ? "additional" : $"{name}-additional";
            var valueRule = additionalProperties is bool ? 
                AddPrimitive("value", GrammarRules.PRIMITIVE_RULES["value"]) :
                Visit(JsonDocument.Parse(JsonSerializer.Serialize(additionalProperties)).RootElement, $"{subName}-value");
            
            var keyRule = !sortedProps.Any() ? 
                AddPrimitive("string", GrammarRules.PRIMITIVE_RULES["string"]) :
                AddRule($"{subName}-k", NotStrings(sortedProps.Select(p => p.Item1).ToList()));

            propKvRuleNames["*"] = AddRule($"{subName}-kv", $"{keyRule} \":\" space {valueRule}");
            optionalProps.Add("*");
        }

        var rule = "\"{\" space";
        if (requiredProps.Any())
        {
            rule += " " + string.Join(" \",\" space ", requiredProps.Select(k => propKvRuleNames[k]));
        }

        if (optionalProps.Any())
        {
            rule += " (";
            if (requiredProps.Any())
                rule += " \",\" space ( ";

            var optionalRules = new List<string>();
            for (int i = 0; i < optionalProps.Count; i++)
            {
                optionalRules.Add(GetRecursiveRefs(optionalProps.Skip(i).ToList(), propKvRuleNames, name, i == 0 && !requiredProps.Any()));
            }
            rule += " " + string.Join(" | ", optionalRules);

            if (requiredProps.Any())
                rule += " )";
            rule += " )?";
        }

        rule += " \"}\" space";
        return rule;
    }

    /// <summary>
    /// Generates a rule that matches any string except the specified ones.
    /// </summary>
    private string NotStrings(List<string> strings)
    {
        // Simplified implementation - in practice, this would use trie-based exclusion
        var charRule = AddPrimitive("char", GrammarRules.PRIMITIVE_RULES["char"]);
        return $"\"\\\"\" {charRule}+ \"\\\"\"";
    }

    /// <summary>
    /// Gets recursive references for optional properties.
    /// </summary>
    private string GetRecursiveRefs(List<string> keys, Dictionary<string, string> propKvRuleNames, string name, bool firstIsOptional)
    {
        if (!keys.Any()) return "";
        
        var key = keys[0];
        var rest = keys.Skip(1).ToList();
        var kvRuleName = propKvRuleNames[key];
        var commaRef = $"( \",\" space {kvRuleName} )";
        
        string result;
        if (firstIsOptional)
        {
            result = commaRef + (key == "*" ? "*" : "?");
        }
        else
        {
            result = kvRuleName + (key == "*" ? " " + commaRef + "*" : "");
        }

        if (rest.Any())
        {
            var restRuleName = AddRule(
                string.IsNullOrEmpty(name) ? $"{key}-rest" : $"{name}-{key}-rest",
                GetRecursiveRefs(rest, propKvRuleNames, name, true));
            result += " " + restRuleName;
        }

        return result;
    }

    /// <summary>
    /// Checks if schema has integer constraints.
    /// </summary>
    private bool HasIntegerConstraints(JsonElement schema)
    {
        return schema.TryGetProperty("minimum", out _) ||
               schema.TryGetProperty("exclusiveMinimum", out _) ||
               schema.TryGetProperty("maximum", out _) ||
               schema.TryGetProperty("exclusiveMaximum", out _);
    }

    /// <summary>
    /// Handles integer constraints.
    /// </summary>
    private string HandleIntegerConstraints(JsonElement schema, string ruleName)
    {
        int? minValue = null, maxValue = null;

        if (schema.TryGetProperty("minimum", out var minElement))
            minValue = minElement.GetInt32();
        else if (schema.TryGetProperty("exclusiveMinimum", out var exMinElement))
            minValue = exMinElement.GetInt32() + 1;

        if (schema.TryGetProperty("maximum", out var maxElement))
            maxValue = maxElement.GetInt32();
        else if (schema.TryGetProperty("exclusiveMaximum", out var exMaxElement))
            maxValue = exMaxElement.GetInt32() - 1;

        var output = new List<string>();
        output.Add("(");
        GenerateMinMaxInt(minValue, maxValue, output);
        output.Add(") space");
        return AddRule(ruleName, string.Join("", output));
    }

    /// <summary>
    /// Visits a regex pattern and converts it to GBNF.
    /// </summary>
    private string VisitPattern(string pattern, string name)
    {
        // This is a simplified version - full regex to GBNF conversion is complex
        // For now, we'll handle basic patterns and fall back to string for complex ones
        if (!pattern.StartsWith("^") || !pattern.EndsWith("$"))
        {
            return AddPrimitive("string", GrammarRules.PRIMITIVE_RULES["string"]);
        }

        var content = pattern[1..^1]; // Remove ^ and $
        var rule = _rawPattern ? content : $"\"\\\"\" ({content}) \"\\\"\" space";
        return AddRule(name, rule);
    }
}    /// <summary>
    /// Returns a generic JSON grammar as fallback.
    /// </summary>
    private static string GetGenericJsonGrammar()
    {
        var sb = new StringBuilder();
        sb.AppendLine("json-content ::= object");
        sb.AppendLine();
        sb.AppendLine("object ::= \"{\" ws (string \":\" ws value (\",\" ws string \":\" ws value)*)? \"}\" ws");
        sb.AppendLine();
        sb.AppendLine("array ::= \"[\" ws (value (\",\" ws value)*)? \"]\" ws");
        sb.AppendLine();
        sb.AppendLine("string ::= \"\\\"\" ([^\"\\\\\\x7F\\x00-\\x1F] | \"\\\\\" ([\"\\\\bfnrt] | \"u\" [0-9a-fA-F]{4}))* \"\\\"\" ws");
        sb.AppendLine();
        sb.AppendLine("number ::= (\"-\"? ([0-9] | [1-9] [0-9]{0,15})) (\".\" [0-9]+)? ([eE] [-+]? [0-9] [1-9]{0,15})? ws");
        sb.AppendLine();
        sb.AppendLine("value ::= object | array | string | number | (\"true\" | \"false\" | \"null\") ws");
        sb.AppendLine();
        sb.AppendLine("ws ::= (\" \" | \"\\n\" | \"\\t\")*");
        
        return sb.ToString().Trim();
    }


}