using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Helper.Runtime.Core
{
    public enum FileRole
    {
        Infrastructure, Model, Interface, ViewModel, View, Service, Logic, Contract, Configuration, Script, Resource, Test
    }

    public sealed class FileRoleJsonConverter : JsonConverter<FileRole>
    {
        private static readonly Dictionary<string, FileRole> RoleAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["infrastructure"] = FileRole.Infrastructure,
            ["infra"] = FileRole.Infrastructure,
            ["model"] = FileRole.Model,
            ["models"] = FileRole.Model,
            ["entity"] = FileRole.Model,
            ["interface"] = FileRole.Interface,
            ["interfaces"] = FileRole.Interface,
            ["contractinterface"] = FileRole.Interface,
            ["viewmodel"] = FileRole.ViewModel,
            ["viewmodels"] = FileRole.ViewModel,
            ["vm"] = FileRole.ViewModel,
            ["view"] = FileRole.View,
            ["views"] = FileRole.View,
            ["xaml"] = FileRole.View,
            ["service"] = FileRole.Service,
            ["services"] = FileRole.Service,
            ["logic"] = FileRole.Logic,
            ["businesslogic"] = FileRole.Logic,
            ["domainlogic"] = FileRole.Logic,
            ["contract"] = FileRole.Contract,
            ["contracts"] = FileRole.Contract,
            ["configuration"] = FileRole.Configuration,
            ["config"] = FileRole.Configuration,
            ["script"] = FileRole.Script,
            ["scripts"] = FileRole.Script,
            ["resource"] = FileRole.Resource,
            ["resources"] = FileRole.Resource,
            ["asset"] = FileRole.Resource,
            ["assets"] = FileRole.Resource,
            ["test"] = FileRole.Test,
            ["tests"] = FileRole.Test
        };

        public override FileRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericRole))
            {
                if (Enum.IsDefined(typeof(FileRole), numericRole))
                {
                    return (FileRole)numericRole;
                }

                return FileRole.Logic;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                return FileRole.Logic;
            }

            var raw = reader.GetString();
            if (TryParse(raw, out var parsed))
            {
                return parsed;
            }

            return FileRole.Logic;
        }

        public override void Write(Utf8JsonWriter writer, FileRole value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());

        public static bool TryParse(string? raw, out FileRole role)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                role = FileRole.Logic;
                return false;
            }

            var normalized = new string(raw.Where(char.IsLetterOrDigit).ToArray());
            if (RoleAliases.TryGetValue(normalized, out role))
            {
                return true;
            }

            if (Enum.TryParse<FileRole>(raw, ignoreCase: true, out role))
            {
                return true;
            }

            role = FileRole.Logic;
            return false;
        }
    }

    public record ArbanMethodTask(string Name, string Signature, string Purpose, string ContextHints);
    public record ArbanResult(string MethodName, string Body, bool Success = true, int Attempts = 1, List<string>? Errors = null);

    public record SwarmFileDefinition(
        string Path,
        string Purpose,
        FileRole Role,
        List<string> Dependencies,
        List<ArbanMethodTask>? Methods = null
    );

    public record SwarmBlueprint(
        string ProjectName,
        string RootNamespace,
        List<SwarmFileDefinition> Files,
        List<string> NuGetPackages
    );

    public record TumenFileTask(
        string Path,
        FileRole Role,
        string ClassName,
        string Namespace,
        List<ArbanMethodTask> Methods,
        List<string> Usings
    );

    public record SwarmArtifact(string Path, string Content);

    // --- Core Interfaces ---

}


