using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AppSettingsGenerator
{
    public class ConfigurationClassDescription
    {
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public Dictionary<string, PropertyTypeDescription> Properties { get; set; }
    }

    public class PropertyTypeDescription
    {
        public bool IsValueType { get; set; }
        public bool IsGenericType { get; set; }
        public bool IsNullable { get; set; }
        public string FullName { get; set; }
        public string Name { get; set; }
        public string TypeArgumentName { get; set; }
    }

    [Generator]
    public class AppSettingsGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            bool BuildHostConfiguration(Dictionary<string, ConfigurationClassDescription> configurationClassDescriptions, ref StringBuilder stringBuilder)
            {
                // Search HostConfiguration description
                if (!configurationClassDescriptions.ContainsKey("Host"))
                {
                    return false;
                }

                var ns = configurationClassDescriptions["Host"].Namespace;

                var uns = new List<string>();
                var nsb = new StringBuilder();
                foreach (var tcd in configurationClassDescriptions)
                {

                    var tns = tcd.Value.Namespace;
                    if (tcd.Key == "Host" || tns == ns) continue;
                    if (!uns.Contains(tns))
                    {
                        uns.Add(tns);
                        nsb.Append($"using {tns};\n");
                    }
                }

                stringBuilder = new StringBuilder($@"
{nsb.ToString()}
namespace {ns}
{{
    public partial class HostConfiguration
    {{

");

                foreach (var tcd in configurationClassDescriptions)
                {
                    if (tcd.Key == "Host") continue;
                    var tcl = tcd.Value.ClassName;
                    var indent = 8;
                    stringBuilder.Append(' ', indent);
                    stringBuilder.AppendLine($"public {tcd.Value.ClassName} {tcd.Key} {{ get; set }}");
                }

                stringBuilder.AppendLine($@"
    }}
}}
");
                return true;
            }

            //Debugger.Launch();
            void CheckConfiguration(Dictionary<string, ConfigurationClassDescription> configClassDescription,
                List<KeyValuePair<string, object>> keyValuePairs, List<string> list)
            {
                foreach (var classDescription in configClassDescription)
                {
                    // <classKey>Configuration is an existing class in the project
                    var classKey = classDescription.Key;
                    if (classKey == "Host")
                    {
                        continue;
                    }

                    if (keyValuePairs.All(x => x.Key != classKey))
                    {
                        list.Add($"Configuration for class {classKey} not found in json file.");
                        continue;
                    }

                    var configProperties =
                        (Dictionary<string, object>) keyValuePairs.First(x => x.Key == classKey).Value;


                    foreach (var classProperty in classDescription.Value.Properties)
                    {
                        var classPropertyName = classProperty.Key;


                        var classPropertyDescription = classProperty.Value;

                        if (configProperties.ContainsKey(classPropertyName))
                        {
                            var configProperty = configProperties.First(x => x.Key == classPropertyName);
                            var configType = ((JsonElement) configProperty.Value).ValueKind;
                            if (
                                (configType == JsonValueKind.False && !(classPropertyDescription.Name == "Boolean" ||
                                                                        classPropertyDescription.IsNullable &&
                                                                        classPropertyDescription.FullName ==
                                                                        "bool?")) ||
                                (configType == JsonValueKind.Number && !(classPropertyDescription.Name == "Int32" ||
                                                                         classPropertyDescription.IsNullable &&
                                                                         classPropertyDescription.FullName ==
                                                                         "int?")) ||
                                (configType == JsonValueKind.String && classPropertyDescription.Name != "String")
                            )
                            {
                                list.Add(
                                    $"Class {classKey}, Property: {classPropertyName}. The field must be of tyoe {classPropertyDescription.FullName}");
                            }
                        }
                        else
                        {
                            if (classPropertyDescription.IsValueType && !classPropertyDescription.IsNullable)
                            {
                                list.Add(
                                    $"Class {classKey}, Property: {classPropertyName}. The field must be present in the configuration file.");
                            }
                        }
                    }
                }
            }

            var configurationDictionary = new Dictionary<string, object>();

            var appsettingsContent = LoadAndMergeConfigFiles(context, configurationDictionary);

            var topLevelProperties = configurationDictionary
                .Where(dict => !(dict.Value is Dictionary<string, object>))
                .ToList();

            var separateConfigClasses =
                configurationDictionary.Except(topLevelProperties)
                    .ToList();

            var existingClassInfo = LoadConfigurationClassInfo(context);

            var errors = new List<string>();

            CheckConfiguration(existingClassInfo, separateConfigClasses, errors);

            if (errors.Any())
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "ID1",
                        "Errors in generator",
                        "Generator errors: {0}",
                        "Cat1",
                        DiagnosticSeverity.Error,
                        true), Location.None, JsonSerializer.Serialize(errors)));
                return;
            }

            var sb = new StringBuilder();
            if (!BuildHostConfiguration(existingClassInfo, ref sb)) return;
            context.AddSource($"Host.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
            return;
            var configSectionClasses = new StringBuilder();

            foreach (var configClazz in separateConfigClasses)
            {
                BuildConfigClass(configClazz, configSectionClasses);
            }

            var sourceBuilder = new StringBuilder(@"using System;
using System.Collections.Generic;

namespace ApplicationConfig
{
    using ApplicationConfigurationSections;                
    
    public class MyAppConfig
    {
");

            //foreach (var (key, value) in topLevelProperties)
            foreach (var item in topLevelProperties)
            {
                var value = item.Value;
                var key = item.Key;

                if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    // Check first value to see what kind of array (list) it needs to be
                    var propertyType = GetPropertyTypeNameBasedOnValue(element.EnumerateArray().FirstOrDefault());

                    sourceBuilder.AppendLine(
                        $"\t\tpublic IEnumerable<{propertyType}> {NormalizePropertyName(key)} {{ get; set; }}");
                }
                else
                {
                    sourceBuilder.AppendLine($"\t\tpublic string {NormalizePropertyName(key)} {{ get; set; }}");
                }
            }

            //foreach (var (key, _) in separateConfigClasses)
            foreach (var item in separateConfigClasses)
            {
                var key = item.Key;
                sourceBuilder.AppendLine(
                    $"\t\tpublic {NormalizePropertyName(key)} {NormalizePropertyName(key)} {{ get; set; }}");
            }

            sourceBuilder.AppendLine("\t}");
            sourceBuilder.AppendLine("}");
            sourceBuilder.AppendLine();

            // Put configSectionClasses in separate namespace
            var configSectionNamespaceSb = new StringBuilder(@"namespace ApplicationConfigurationSections
{   
");
            configSectionNamespaceSb.AppendLine(configSectionClasses.ToString());
            configSectionNamespaceSb.AppendLine("}");

            sourceBuilder.AppendLine(configSectionNamespaceSb.ToString());

            context.AddSource("MyAppConfig",
                SourceText.From($"/*{appsettingsContent}*/" + sourceBuilder.ToString(), Encoding.UTF8));
        }

        private Dictionary<string, ConfigurationClassDescription> LoadConfigurationClassInfo(
            GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            Dictionary<string, ConfigurationClassDescription>
                result = new Dictionary<string, ConfigurationClassDescription>();
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var sm = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();
                foreach (var typeInfo in root.DescendantNodesAndSelf().Select(x => sm.GetTypeInfo(x))
                    .Where(t => t.Type != null && t.Type.Name.EndsWith("Configuration")))
                {
                    var typeName = typeInfo.Type?.Name;

                    var typeNameWithoutSuffix = typeName?.Substring(0,
                        typeName.IndexOf("Configuration", StringComparison.Ordinal));

                    if (!String.IsNullOrWhiteSpace(typeNameWithoutSuffix) && !result.ContainsKey(typeNameWithoutSuffix))
                    {
                        var classDescription = new ConfigurationClassDescription();
                        result.Add(typeNameWithoutSuffix, classDescription);
                        classDescription.ClassName = typeName;
                        classDescription.Namespace = typeInfo.Type.ContainingNamespace.ToDisplayString();
                        var members = new Dictionary<string, PropertyTypeDescription>();
                        classDescription.Properties = members;
                        foreach (var member in typeInfo.Type.GetMembers())
                        {
                            if (member is IMethodSymbol methodSymbol &&
                                methodSymbol.MethodKind == MethodKind.PropertyGet &&
                                methodSymbol.ReturnType is INamedTypeSymbol returnType
                            )
                            {
                                var name = methodSymbol.Name.Split('_')[1];
                                var propType = new PropertyTypeDescription();
                                propType.IsValueType = returnType.IsValueType;
                                propType.IsGenericType = returnType.IsGenericType;
                                propType.FullName = returnType.ToString();
                                propType.IsNullable = returnType.Name == "Nullable";
                                if (returnType.IsGenericType)
                                {
                                    propType.TypeArgumentName = returnType.TypeArguments.First().Name;
                                }

                                propType.Name = returnType.Name;
                                members.Add(name, propType);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static void BuildConfigClass(KeyValuePair<string, object> classInfo, StringBuilder sb)
        {
            StringBuilder nestedClasses = new StringBuilder();

            sb.AppendLine($"\tpublic class {classInfo.Key}");
            sb.AppendLine("\t{");

            foreach (var item in (Dictionary<string, object>) classInfo.Value)
            {
                if (item.Value is Dictionary<string, object>)
                {
                    sb.AppendLine($"\t\tpublic {item.Key} {NormalizePropertyName(item.Key)} {{ get; set; }}");
                    BuildConfigClass(item, nestedClasses);
                }
                else
                {
                    var prop = (JsonElement) item.Value;
                    var propertyType = GetPropertyTypeNameBasedOnValue(prop);
                    if (prop.ValueKind == JsonValueKind.Array)
                    {
                        sb.AppendLine(
                            $"\t\tpublic IEnumerable<{propertyType}> {NormalizePropertyName(item.Key)} {{ get; set; }}");
                    }
                    else
                    {
                        sb.AppendLine($"\t\tpublic {propertyType} {NormalizePropertyName(item.Key)} {{ get; set; }}");
                    }
                }
            }

            sb.AppendLine("\t}");
            sb.AppendLine();
            sb.AppendLine(nestedClasses.ToString());
        }

        private static string GetPropertyTypeNameBasedOnValue(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number) return "int";
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return "bool";
            return "string";
        }

        private static string NormalizePropertyName(string originalName)
        {
            var underscore = "_";
            var newPropertyName = originalName
                .Replace(".", underscore)
                .Replace("$", underscore);
            return newPropertyName;
        }

        private static string LoadAndMergeConfigFiles(
            GeneratorExecutionContext context,
            Dictionary<string, object> resultConfigurationDictionary)
        {
            var sb = new StringBuilder();
            foreach (var configFile in context.AdditionalFiles)
            {
                if (configFile.Path.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase))
                {
                    var contentOfFile = configFile.GetText()?.ToString();
                    sb.AppendLine(contentOfFile);

                    if (!string.IsNullOrEmpty(contentOfFile))
                    {
                        var deserializedJson = DeserializeToDictionary(contentOfFile);
                        MergeDictionaries(resultConfigurationDictionary, deserializedJson);
                    }
                }
            }

            return sb.ToString();
        }

        private static Dictionary<string, object> DeserializeToDictionary(string configJson)
        {
            var configValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);

            var finalConfigJson = new Dictionary<string, object>();
            foreach (KeyValuePair<string, JsonElement> configValue in configValues)
            {
                if (configValue.Value.ValueKind is JsonValueKind.Object)
                {
                    finalConfigJson.Add(configValue.Key, DeserializeToDictionary(configValue.Value.ToString()));
                }
                else
                {
                    finalConfigJson.Add(configValue.Key, configValue.Value);
                }
            }

            return finalConfigJson;
        }

        private static void MergeDictionaries(Dictionary<string, object> dictionary1,
            Dictionary<string, object> dictionary2)
        {
            foreach (var entry in dictionary2)
            {
                if (!dictionary1.ContainsKey(entry.Key))
                {
                    dictionary1.Add(entry.Key, entry.Value);
                }
                else
                {
                    // Which one has the most values? (in case of object)
                    if (entry.Value is Dictionary<string, object> existingObjectValue)
                    {
                        int numberOfValuesInExistingObject = existingObjectValue.Count;
                        var numberOfValuesInNewObject =
                            ((Dictionary<string, object>) dictionary1[entry.Key]).Count;

                        if (numberOfValuesInExistingObject < numberOfValuesInNewObject)
                        {
                            // replace existing object with new object
                            dictionary1[entry.Key] = entry.Value;

                            MergeDictionaries((Dictionary<string, object>) dictionary1[entry.Key],
                                (Dictionary<string, object>) entry.Value);
                        }
                    }
                }
            }
        }
    }
}