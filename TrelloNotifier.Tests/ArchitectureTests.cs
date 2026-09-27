using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrelloNotifier.Tests;

internal static class ArchitectureTests
{
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        """;

    public static void ModelsAreIndependent()
    {
        Verify(ReadModelSources(), allowSchedule: false);
    }

    public static void ScheduleDependsOnlyOnModels()
    {
        string schedule = Path.Combine(FindRoot(), "TrelloNotifier", "Services", "ReminderSchedule.cs");
        Verify(ReadModelSources().Append(CSharpSyntaxTree.ParseText(File.ReadAllText(schedule), path: schedule)),
            allowSchedule: true);
    }

    public static void GuardRejectsForbiddenDependencies()
    {
        // Fallos de compilación aislada: no hay acceso a servicios ni a WinUI.
        ExpectRejection("class Bad { TrelloNotifier.Services.SettingsStore? Store; }", false);
        ExpectRejection("class Bad { Microsoft.UI.Xaml.Controls.Page? Page; }", false);
        ExpectRejection("class Bad { TrelloNotifier.Services.TrelloApiClient? Api; }", true);
        // Referencias válidas para el compilador, pero prohibidas por arquitectura.
        ExpectRejection("class Bad { string Read() => System.IO.File.ReadAllText(\"fake\"); }", false);
        ExpectRejection("using Client = System.Net.Http.HttpClient; class Bad { Client? Http; }", true);
        ExpectRejection("class Bad { void Print() => System.Console.WriteLine(\"fake\"); }", true);
    }

    private static void ExpectRejection(string source, bool allowSchedule)
    {
        try
        {
            Verify(new[] { CSharpSyntaxTree.ParseText(source, path: "negative-fixture.cs") }, allowSchedule);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("El guard aceptó una dependencia prohibida del fixture.");
    }

    private static void Verify(IEnumerable<SyntaxTree> sources, bool allowSchedule)
    {
        // Referencias solo al runtime .NET; nunca a app, dobles ni ensamblados WinUI.
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("No se encontraron referencias del runtime.");
        var references = trustedAssemblies.Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path));
        SyntaxTree[] trees = sources.Append(CSharpSyntaxTree.ParseText(GlobalUsings)).ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create("ArchitectureProbe", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Diagnostic[] errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException("Dependencia no disponible en la capa aislada: " + string.Join("; ", errors.Select(e => e.ToString())));
        }

        foreach (SyntaxTree tree in trees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (NameSyntax name in tree.GetRoot().DescendantNodes().OfType<NameSyntax>())
            {
                ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
                if (symbol is IAliasSymbol alias) symbol = alias.Target;
                if (symbol is null || symbol is INamespaceSymbol) continue;
                string? ns = symbol.ContainingNamespace?.ToDisplayString();
                bool allowed = ns is null or "" or "<global namespace>" or "System" or
                    "System.Collections" or "System.Collections.Generic" or "System.Linq" or
                    "System.Text.Json.Serialization" or "TrelloNotifier.Models" ||
                    (allowSchedule && ns == "TrelloNotifier.Services");
                string? type = (symbol as INamedTypeSymbol ?? symbol.ContainingType)?.ToDisplayString();
                if (!allowed || type is "System.Console" or "System.Environment")
                {
                    throw new InvalidOperationException($"Dependencia prohibida: {symbol} en {name.GetLocation().GetLineSpan()}.");
                }
            }
        }
    }

    private static IEnumerable<SyntaxTree> ReadModelSources()
    {
        string directory = Path.Combine(FindRoot(), "TrelloNotifier", "Models");
        string[] files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "Generated Files"))
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No se encontraron fuentes de modelos.");
        return files.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrelloNotifier.sln"))) return directory.FullName;
        }
        throw new InvalidOperationException("Ejecuta las pruebas desde una copia del proyecto con sus fuentes.");
    }
}
