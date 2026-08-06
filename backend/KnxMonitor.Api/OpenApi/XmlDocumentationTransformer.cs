using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace KnxMonitor.Api.OpenApi;

/// <summary>
/// Überträgt die XML-Doku-Kommentare der Controller in das OpenAPI-Dokument.
/// </summary>
/// <remarks>
/// .NET 9 wertet <c>&lt;summary&gt;</c> und Co. nicht von selbst aus — das kommt erst
/// mit .NET 10. Bis dahin lesen wir die vom Compiler erzeugte XML-Datei selbst ein.
/// Damit bleibt die Beschreibung eines Endpunkts dort, wo sie hingehört: direkt an
/// der Methode, zusammen mit Signatur und Attributen, aus denen der Rest des Dokuments
/// ohnehin generiert wird. Eine zweite, separat gepflegte Wahrheit entsteht nicht.
///
/// Nach der Migration auf .NET 10 kann diese Klasse ersatzlos entfallen.
/// </remarks>
public sealed class XmlDocumentationTransformer : IOpenApiOperationTransformer
{
    // Der Compiler legt die Datei neben die Assembly (GenerateDocumentationFile).
    private static readonly Lazy<IReadOnlyDictionary<string, XElement>> Members = new(Load);

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            return Task.CompletedTask;
        }

        if (!Members.Value.TryGetValue(MemberKey(descriptor.MethodInfo), out var member))
        {
            return Task.CompletedTask;
        }

        var summary = Text(member.Element("summary"));
        if (!string.IsNullOrEmpty(summary))
        {
            operation.Summary = summary;
        }

        // <remarks> trägt die Details (Grenzwerte, Sonderfälle) — in der Referenz
        // erscheint das als Fließtext unter der Kurzfassung.
        var remarks = Text(member.Element("remarks"));
        if (!string.IsNullOrEmpty(remarks))
        {
            operation.Description = remarks;
        }

        // <param name="..."> auf die passenden Parameter legen.
        foreach (var param in member.Elements("param"))
        {
            var name = param.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var target = operation.Parameters?.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                target.Description = Text(param);
            }
        }

        // <returns> beschreibt die Erfolgsantwort.
        var returns = Text(member.Element("returns"));
        if (!string.IsNullOrEmpty(returns) && operation.Responses is not null)
        {
            foreach (var code in new[] { "200", "201", "204" })
            {
                if (operation.Responses.TryGetValue(code, out var response))
                {
                    response.Description = returns;
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Baut den Schlüssel, unter dem die Methode in der XML-Datei steht (z. B. <c>M:Ns.Type.Method(System.Int32)</c>).</summary>
    private static string MemberKey(MethodInfo method)
    {
        var type = method.DeclaringType!;
        var name = $"M:{type.FullName}.{method.Name}";
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return name;
        }

        return name + "(" + string.Join(",", parameters.Select(p => TypeKey(p.ParameterType))) + ")";
    }

    /// <summary>Typnamen so schreiben, wie der Compiler sie in die XML-Datei stellt.</summary>
    private static string TypeKey(Type type)
    {
        if (type.IsByRef)
        {
            return TypeKey(type.GetElementType()!) + "@";
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName!;
            var trimmed = definition[..definition.IndexOf('`')];
            var args = string.Join(",", type.GetGenericArguments().Select(TypeKey));
            return $"{trimmed}{{{args}}}";
        }

        return type.FullName ?? type.Name;
    }

    private static string Text(XElement? element)
    {
        if (element is null) return string.Empty;

        // Zeilenumbrüche und Einrückung der Quelldatei zu einem Absatz zusammenziehen,
        // damit die Referenz keine zerrissenen Sätze anzeigt.
        var raw = string.Concat(element.Nodes().Select(n => n switch
        {
            XText text => text.Value,
            XElement e when e.Name == "see" => e.Attribute("cref")?.Value.Split('.').Last() ?? e.Value,
            XElement e when e.Name == "paramref" => e.Attribute("name")?.Value ?? e.Value,
            XElement e => e.Value,
            _ => string.Empty,
        }));

        var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        return string.Join(" ", lines).Trim();
    }

    private static IReadOnlyDictionary<string, XElement> Load()
    {
        var path = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".xml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, XElement>();
        }

        try
        {
            return XDocument.Load(path)
                .Descendants("member")
                .Where(m => m.Attribute("name") is not null)
                .GroupBy(m => m.Attribute("name")!.Value)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception)
        {
            // Eine fehlerhafte Doku-Datei darf den Start nicht verhindern — dann fehlen
            // eben die Beschreibungen.
            return new Dictionary<string, XElement>();
        }
    }
}
