using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsStyle.DocCheck
{
    /// <summary>
    /// Syntax-only version of compiler warning CS1591 (missing XML comment for publicly visible type or member).
    /// The compiler skips CS1591 whenever a project has errors, which is always the case for Unity code built
    /// without UnityEngine references. Parsing alone avoids that. Also reports syntax the target C# version
    /// does not support, such as file-scoped namespaces under C# 9.
    /// </summary>
    internal static class Program
    {
        private static readonly HashSet<string> ExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "ci", "Library", "Temp", "Logs", "UserSettings", "obj", "bin",
        };

        // "Feature is not available in C# N" errors (one ID per language version) and "feature is in preview".
        // Some are only found while binding, so they come from a compilation, not the parser.
        private static readonly HashSet<string> LanguageVersionErrors = new HashSet<string>
        {
            "CS8022", "CS8023", "CS8024", "CS8025", "CS8026", "CS8059", "CS8107", "CS8302",
            "CS8320", "CS8370", "CS8400", "CS8773", "CS8936", "CS9058", "CS8652",
        };

        private static readonly List<string> Findings = new List<string>();

        // Partial types only need docs on one part, so they are resolved after every file is read.
        private static readonly Dictionary<string, PartialType> PartialTypes = new Dictionary<string, PartialType>();

        private static int Main(string[] args)
        {
            string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
            LanguageVersion languageVersion = ReadLanguageVersion();
            var options = new CSharpParseOptions(languageVersion, DocumentationMode.Parse);

            var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsExcluded(Path.GetRelativePath(root, file)))
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();

            var trees = new List<SyntaxTree>();
            foreach (string file in files)
            {
                string relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options, relativePath);
                trees.Add(tree);
                CheckMembers(((CompilationUnitSyntax)tree.GetRoot()).Members, string.Empty, ContainerKind.Namespace);
            }

            ReportLanguageErrors(trees);

            foreach (PartialType partialType in PartialTypes.Values.Where(type => !type.HasDocs))
            {
                Findings.Add(partialType.Finding);
            }

            foreach (string finding in Findings.OrderBy(finding => finding, StringComparer.Ordinal))
            {
                Console.WriteLine(finding);
            }

            Console.WriteLine($"Checked {files.Count} files against C# {languageVersion.ToDisplayString()}: {Findings.Count} findings.");
            return Findings.Count == 0 ? 0 : 1;
        }

        private static LanguageVersion ReadLanguageVersion()
        {
            string value = typeof(Program).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "TargetLangVersion")?.Value;

            return LanguageVersionFacts.TryParse(value, out LanguageVersion version) ? version : LanguageVersion.Default;
        }

        private static bool IsExcluded(string relativePath)
        {
            return relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => ExcludedFolders.Contains(segment));
        }

        // Reports real syntax errors and newer-than-target syntax. Missing-reference errors (UnityEngine, etc.)
        // are expected without references and are ignored.
        private static void ReportLanguageErrors(List<SyntaxTree> trees)
        {
            IEnumerable<Diagnostic> parseErrors = trees.SelectMany(tree => tree.GetDiagnostics());
            IEnumerable<Diagnostic> versionErrors = CSharpCompilation
                .Create("DocCheck", trees, options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .GetDiagnostics()
                .Where(diagnostic => LanguageVersionErrors.Contains(diagnostic.Id));

            IEnumerable<string> errors = parseErrors
                .Concat(versionErrors)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => Format(diagnostic.Location, "error", diagnostic.Id, diagnostic.GetMessage()))
                .Distinct();

            Findings.AddRange(errors);
        }

        private static void CheckMembers(SyntaxList<MemberDeclarationSyntax> members, string containerName, ContainerKind container)
        {
            foreach (MemberDeclarationSyntax member in members)
            {
                if (member is BaseNamespaceDeclarationSyntax ns)
                {
                    CheckMembers(ns.Members, Join(containerName, ns.Name.ToString()), ContainerKind.Namespace);
                    continue;
                }

                if (!IsVisible(member, container))
                {
                    continue;
                }

                string name = Join(containerName, GetName(member));

                if (member is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    TrackPartialType(type, name);
                }
                else if (!HasDocs(member))
                {
                    AddMissingDocs(member, name);
                }

                if (member is TypeDeclarationSyntax nestedType)
                {
                    ContainerKind kind = nestedType is InterfaceDeclarationSyntax ? ContainerKind.Interface : ContainerKind.Type;
                    CheckMembers(nestedType.Members, name, kind);
                }
                else if (member is EnumDeclarationSyntax enumType)
                {
                    foreach (EnumMemberDeclarationSyntax enumMember in enumType.Members.Where(m => !HasDocs(m)))
                    {
                        AddMissingDocs(enumMember, Join(name, enumMember.Identifier.Text));
                    }
                }
            }
        }

        private static bool IsVisible(MemberDeclarationSyntax member, ContainerKind container)
        {
            SyntaxTokenList modifiers = member.Modifiers;

            if (modifiers.Any(SyntaxKind.PublicKeyword))
            {
                return true;
            }

            // private protected is not visible outside the assembly, so it does not need docs.
            if (modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword))
            {
                return container != ContainerKind.Namespace;
            }

            // Interface members are public unless they say otherwise. Explicit implementations are never visible.
            bool hasAccessModifier = modifiers.Any(SyntaxKind.PrivateKeyword) || modifiers.Any(SyntaxKind.InternalKeyword);
            return container == ContainerKind.Interface && !hasAccessModifier;
        }

        private static bool HasDocs(SyntaxNode node)
        {
            return node.GetLeadingTrivia().Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
        }

        private static void TrackPartialType(TypeDeclarationSyntax type, string name)
        {
            string key = $"{name}`{type.Arity}";
            if (!PartialTypes.TryGetValue(key, out PartialType partialType))
            {
                partialType = new PartialType(Format(GetNameToken(type).GetLocation(), "warning", "CS1591", MissingDocsMessage(name)));
                PartialTypes.Add(key, partialType);
            }

            partialType.HasDocs |= HasDocs(type);
        }

        private static void AddMissingDocs(SyntaxNode node, string name)
        {
            Findings.Add(Format(GetNameToken(node).GetLocation(), "warning", "CS1591", MissingDocsMessage(name)));
        }

        private static string MissingDocsMessage(string name)
        {
            return $"Missing XML comment for publicly visible type or member '{name}'";
        }

        private static string GetName(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case IndexerDeclarationSyntax _:
                    return "this[]";
                case OperatorDeclarationSyntax op:
                    return $"operator {op.OperatorToken.Text}";
                case ConversionOperatorDeclarationSyntax conversion:
                    return $"{conversion.ImplicitOrExplicitKeyword.Text} operator {conversion.Type}";
                case MethodDeclarationSyntax method:
                    return $"{method.Identifier.Text}()";
                case ConstructorDeclarationSyntax constructor:
                    return $"{constructor.Identifier.Text}()";
                default:
                    return GetNameToken(member).Text;
            }
        }

        private static SyntaxToken GetNameToken(SyntaxNode node)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    return type.Identifier;
                case DelegateDeclarationSyntax del:
                    return del.Identifier;
                case MethodDeclarationSyntax method:
                    return method.Identifier;
                case ConstructorDeclarationSyntax constructor:
                    return constructor.Identifier;
                case PropertyDeclarationSyntax property:
                    return property.Identifier;
                case EventDeclarationSyntax ev:
                    return ev.Identifier;
                case IndexerDeclarationSyntax indexer:
                    return indexer.ThisKeyword;
                case OperatorDeclarationSyntax op:
                    return op.OperatorToken;
                case ConversionOperatorDeclarationSyntax conversion:
                    return conversion.OperatorKeyword;
                case BaseFieldDeclarationSyntax field:
                    return field.Declaration.Variables.First().Identifier;
                case EnumMemberDeclarationSyntax enumMember:
                    return enumMember.Identifier;
                default:
                    return node.GetFirstToken();
            }
        }

        private static string Join(string container, string name)
        {
            return container.Length == 0 ? name : $"{container}.{name}";
        }

        // Same layout as compiler output, so CI can turn every tool's output into annotations the same way.
        private static string Format(Location location, string severity, string id, string message)
        {
            FileLinePositionSpan span = location.GetLineSpan();
            int line = span.StartLinePosition.Line + 1;
            int column = span.StartLinePosition.Character + 1;
            return $"{span.Path}({line},{column}): {severity} {id}: {message}";
        }

        private enum ContainerKind
        {
            Namespace,
            Type,
            Interface,
        }

        private sealed class PartialType
        {
            public PartialType(string finding)
            {
                Finding = finding;
            }

            public string Finding { get; }

            public bool HasDocs { get; set; }
        }
    }
}
