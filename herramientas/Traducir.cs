#:package Microsoft.CodeAnalysis.CSharp@4.14.0
#:property PublishAot=false

// Herramienta de traducción de Bancada (se ejecuta desde la raíz del repositorio).
//
//   dotnet run herramientas/Traducir.cs -- claves src/Bancada src/Bancada/Idiomas/en.json [plantilla.json]
//       Comprueba una traducción: claves que faltan o sobran y huecos {0}, {1}… mal copiados.
//       Con plantilla.json escribe todas las claves, para empezar un idioma nuevo.
//   dotnet run herramientas/Traducir.cs -- listar  src/Bancada   → textos nuevos que aún no pasan por Tx/Tf
//   dotnet run herramientas/Traducir.cs -- aplicar src/Bancada   → los envuelve en Tx("…") / Tf("…", datos)
//   dotnet run herramientas/Traducir.cs -- formatear src/Bancada → pone espacios tras las comas de Tx/Tf
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

var modo = args[0];
var raiz = args[1];
var archivos = Directory.GetFiles(raiz, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.EndsWith("Idioma.cs"))
    .Order().ToList();

if (modo == "claves")
{
    var usadas = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var f in archivos)
    {
        var raizArbol = CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetRoot();
        foreach (var inv in raizArbol.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not IdentifierNameSyntax { Identifier.ValueText: "Tx" or "Tf" }) continue;
            if (inv.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax lit) continue;
            var linea = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (!usadas.TryGetValue(lit.Token.ValueText, out var l)) usadas[lit.Token.ValueText] = l = new();
            l.Add($"{Path.GetFileName(f)}:{linea}");
        }
    }
    var traducidas = new Dictionary<string, string>();
    if (args.Length > 2 && File.Exists(args[2]))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(args[2]), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        foreach (var p in doc.RootElement.EnumerateObject()) traducidas[p.Name] = p.Value.GetString() ?? "";
    }
    int faltan = 0, malas = 0;
    foreach (var (k, donde) in usadas)
    {
        if (!traducidas.TryGetValue(k, out var t) || t.Length == 0)
        {
            faltan++;
            Console.WriteLine($"FALTA  {JsonSerializer.Serialize(k)}   [{string.Join(", ", donde.Take(3))}]");
            continue;
        }
        // Los huecos {0}, {1:0.0}… deben ser los mismos en la traducción.
        var h1 = Regex.Matches(k, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order();
        var h2 = Regex.Matches(t, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order();
        if (!h1.SequenceEqual(h2)) { malas++; Console.WriteLine($"HUECOS {JsonSerializer.Serialize(k)} → {JsonSerializer.Serialize(t)}"); }
    }
    var sobran = traducidas.Keys.Where(k => !k.StartsWith("__") && !usadas.ContainsKey(k)).ToList();
    foreach (var k in sobran) Console.WriteLine($"SOBRA  {JsonSerializer.Serialize(k)}");
    Console.WriteLine($"Claves usadas: {usadas.Count} · faltan: {faltan} · huecos mal: {malas} · sobran: {sobran.Count}");
    if (args.Length > 3) File.WriteAllText(args[3], JsonSerializer.Serialize(usadas.Keys.ToDictionary(k => k, k => traducidas.GetValueOrDefault(k, "")),
        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return;
}

if (modo == "formatear")
{
    // «Tf("…",a,b)» → «Tf("…", a, b)»
    foreach (var f in archivos)
    {
        var texto = File.ReadAllText(f);
        var raizArbol = CSharpSyntaxTree.ParseText(texto).GetRoot();
        var nuevo = new Separadores().Visit(raizArbol);
        if (nuevo.ToFullString() != texto) File.WriteAllText(f, nuevo.ToFullString(), new UTF8Encoding(false));
    }
    return;
}

int total = 0;
foreach (var f in archivos)
{
    var texto = File.ReadAllText(f);
    var arbol = CSharpSyntaxTree.ParseText(texto, new CSharpParseOptions(LanguageVersion.Preview));
    var rw = new Reescritor(Path.GetFileName(f), modo == "listar");
    var nuevo = rw.Visit(arbol.GetRoot());
    total += rw.Cambios;
    if (modo == "aplicar" && rw.Cambios > 0) File.WriteAllText(f, nuevo.ToFullString(), new UTF8Encoding(false));
}
Console.WriteLine($"Textos envueltos: {total}");

sealed class Separadores : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitArgumentList(ArgumentListSyntax node)
    {
        var visitado = (ArgumentListSyntax)base.VisitArgumentList(node)!;
        if (node.Parent is not InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Tf" or "Tx" } }) return visitado;
        var seps = visitado.Arguments.GetSeparators().Select(s => s.HasTrailingTrivia ? s : s.WithTrailingTrivia(Space)).ToList();
        return visitado.WithArguments(SeparatedList(visitado.Arguments, seps));
    }
}

sealed class Reescritor(string archivo, bool listar) : CSharpSyntaxRewriter
{
    public int Cambios;

    static readonly HashSet<string> MetodosTecnicos = new()
    {
        "IsMatch", "Match", "Matches", "Replace", "Split", "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf",
        "Equals", "Trim", "TrimEnd", "TrimStart", "Wmi", "Leer", "Ejecutar", "EjecutarAsync", "EjecutarSalida", "Buscar",
        "Combine", "GetFiles", "GetDirectories", "Exists", "ReadAllText", "ReadAllBytes", "WriteAllBytes", "ReadLines", "Delete",
        "OpenSubKey", "GetValue", "Parse", "TryParse", "TryGetProperty", "GetProperty", "ToString", "Escape", "Tx", "Tf",
        "Consultar", "Lanzar", "Clases", "Start", "FindResource", "TryFindResource", "GetManifestResourceStream", "GetEnvironmentVariable",
        "Serialize", "Leer", "Guardar", "CreateFile", "Texto", "Numero", "Propiedad", "N", "Campo", "Cuenta"
    };
    // Ojo: Ui.Texto se llama igual que UsbWindows.Texto; se distingue abajo por el receptor.

    static readonly HashSet<string> TiposTecnicos = new()
    {
        "Regex", "Guid", "ProcessStartInfo", "FontFamily", "ColumnDefinitions", "RowDefinitions", "Cursor", "Uri",
        "DirectoryInfo", "FileInfo", "DriveInfo", "Version", "JsonDocumentOptions"
    };

    static readonly HashSet<string> ClasesEstilo = new()
    {
        "titulo", "subtitulo", "seccion", "etiqueta", "valor", "grande", "cifra", "mono", "suave", "negrita", "tarjeta", "hundido",
        "aviso-caja", "ok-caja", "chip", "ok", "aviso", "fallo", "punto", "nav", "activo", "primario", "secundario", "veredicto",
        "elegido", "tecla", "pulsada", "activa", "barra", "barra-fondo", "marca", "icono-nav"
    };

    static readonly HashSet<string> Denegadas = new()
    {
        "Bancada", "Lenovo", "Intel", "Microsoft", "Realtek", "Samsung", "Dell", "Acer", "Apple", "Fujitsu", "Huawei", "Xiaomi", "Medion",
        "Gigabyte", "Broadcom", "Qualcomm", "Ethernet", "Bluetooth", "Charging", "Discharging", "Full", "Battery", "Mains", "Composite",
        "Package", "Core", "Tctl", "Tdie", "Virtual", "Unknown", "Inter", "Monitor", "Description", "Drive", "Default",
        "Manufacturer", "Model", "Name", "Version", "Status", "Capture", "Render", "Properties", "System", "Normal", "Auto",
        "Cascadia", "Consolas", "Linux", "Windows", "Logo", "Cobre", "Borde", "Tinta", "TintaSuave", "FondoHundido",
        "Gbps", "Mbps", "and (", "Qualcomm Atheros", "Red Hat (virtio)", "Chipset", "Esc", "Ctrl", "Alt", "Win", "Super", "Insert"
    };

    // Códigos internos que se comparan después: se traducen al mostrarlos, no al crearlos.
    static readonly HashSet<string> Codigos = new() { "Correcta", "Aviso", "Mala" };

    static bool EsTextoHumano(string s)
    {
        var t = s.Trim();
        if (t.Length < 2 || !t.Any(char.IsLetter)) return false;
        if (Regex.IsMatch(t, @"\\|SELECT |://|\.dll|__|^-|\s--?\w|\$|^\{|#|\[|@")) return false;
        if (t.Contains('\n') && t.Split('\n').All(l => Regex.IsMatch(l.Trim(), @"^[A-ZÁÉÍÓÚÑ][a-záéíóúñ]+$"))) return true; // «Impr\nPant»
        var fichas = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fichas.All(ClasesEstilo.Contains)) return false;
        if (Denegadas.Contains(t)) return false;
        if (Regex.IsMatch(t, "[áéíóúñÁÉÍÓÚÑ¿¡ü…]")) return true;
        if (Regex.IsMatch(t, @"\d,\d") && Regex.IsMatch(t, @"[A-Za-z]{2,}")) return true;
        if (Regex.IsMatch(t, @"^[A-ZÁÉÍÓÚÑ][a-záéíóúñ]{2,}$")) return true;
        if (t.Contains(' ') && Regex.IsMatch(t, @"(^|[\s(])[a-záéíóúñ]{3,}([\s.,:;)]|$)")) return true;
        if (Regex.IsMatch(t, @"^[A-ZÁÉÍÓÚÑ][a-záéíóúñ]+( [A-Za-z0-9áéíóúñÁÉÍÓÚÑ]+)+$")) return true;
        return false;
    }

    static string NombreMetodo(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax i => i.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => ""
    };

    bool Permitido(SyntaxNode nodo, string valor)
    {
        if (archivo is "DetectorWindows.cs" or "Almacenamiento.cs" && Codigos.Contains(valor)) return false;
        for (var p = nodo.Parent; p != null; p = p.Parent)
        {
            switch (p)
            {
                case ArgumentSyntax { Parent: BracketedArgumentListSyntax }:
                    return false; // o["Manufacturer"]
                case ArgumentSyntax a2 when archivo == "TecladoVista.cs" && a2.Parent?.Parent is BaseObjectCreationExpressionSyntax &&
                                            ((ArgumentListSyntax)a2.Parent).Arguments.IndexOf(a2) == 0:
                    return false; // código de la tecla física
                case AttributeSyntax:
                case PatternSyntax:
                case CaseSwitchLabelSyntax:
                case ParameterSyntax:
                case ElementAccessExpressionSyntax:
                    return false;
                case FieldDeclarationSyntax fd when fd.Modifiers.Any(SyntaxKind.ConstKeyword):
                case LocalDeclarationStatementSyntax ld when ld.IsConst:
                    return false;
                case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression):
                    return false;
                case ArgumentSyntax a:
                    if (a.Parent?.Parent is InvocationExpressionSyntax inv)
                    {
                        var nombre = NombreMetodo(inv);
                        // Ui.Texto sí es interfaz; UsbWindows.Texto no.
                        if (nombre == "Texto" && inv.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "Ui" } })
                            return a == inv.ArgumentList.Arguments[0];
                        if (nombre == "N" || MetodosTecnicos.Contains(nombre)) return false;
                    }
                    if (a.Parent?.Parent is ObjectCreationExpressionSyntax oc && TiposTecnicos.Contains(oc.Type.ToString().Split('.').Last())) return false;
                    return true;
                case StatementSyntax:
                case MemberDeclarationSyntax:
                    return true;
            }
        }
        return true;
    }

    static InvocationExpressionSyntax Llamada(string metodo, IEnumerable<ExpressionSyntax> argumentos) =>
        InvocationExpression(IdentifierName(metodo), ArgumentList(SeparatedList(argumentos.Select(Argument))));

    void Anotar(SyntaxNode n, string clave)
    {
        Cambios++;
        if (listar) Console.WriteLine($"{archivo}:{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {clave}");
    }

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.StringLiteralExpression)) return base.VisitLiteralExpression(node);
        var valor = node.Token.ValueText;
        if (!EsTextoHumano(valor) || !Permitido(node, valor)) return node;
        Anotar(node, valor);
        return Llamada("Tx", new ExpressionSyntax[] { LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(valor)) }).WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        var visitado = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;
        var formato = new StringBuilder();
        var soloTexto = new StringBuilder();
        var datos = new List<ExpressionSyntax>();
        foreach (var c in visitado.Contents)
        {
            switch (c)
            {
                case InterpolatedStringTextSyntax t:
                    var v = t.TextToken.ValueText;
                    soloTexto.Append(v);
                    formato.Append(v.Replace("{", "{{").Replace("}", "}}"));
                    break;
                case InterpolationSyntax i:
                    formato.Append('{').Append(datos.Count);
                    if (i.AlignmentClause != null) formato.Append(',').Append(i.AlignmentClause.Value.ToString());
                    if (i.FormatClause != null) formato.Append(':').Append(i.FormatClause.FormatStringToken.ValueText);
                    formato.Append('}');
                    datos.Add(i.Expression.WithoutTrivia());
                    soloTexto.Append(' ');
                    break;
            }
        }
        if (!EsTextoHumano(soloTexto.ToString()) || !Permitido(node, formato.ToString())) return visitado;
        Anotar(node, formato.ToString());
        var args = new List<ExpressionSyntax> { LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(formato.ToString())) };
        args.AddRange(datos.Select(d => d is ConditionalExpressionSyntax or BinaryExpressionSyntax or SwitchExpressionSyntax or IsPatternExpressionSyntax
            ? ParenthesizedExpression(d) : d));
        return Llamada("Tf", args).WithTriviaFrom(node);
    }

    /// <summary>Campos estáticos con textos: pasan a propiedades, para que se traduzcan al cambiar de idioma.</summary>
    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var antes = Cambios;
        var visitado = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node)!;
        if (Cambios == antes || !visitado.Modifiers.Any(SyntaxKind.StaticKeyword) || visitado.Declaration.Variables.Count != 1) return visitado;
        var v = visitado.Declaration.Variables[0];
        if (v.Initializer == null) return visitado;
        if (listar) Console.WriteLine($"{archivo}: campo estático {v.Identifier.ValueText} → propiedad");
        var mods = string.Join(" ", visitado.Modifiers.Where(m => !m.IsKind(SyntaxKind.ReadOnlyKeyword)).Select(m => m.Text));
        var tipo = visitado.Declaration.Type.ToString().Trim();
        var valor = v.Initializer.Value.ToFullString().Trim();
        if (v.Initializer.Value is InitializerExpressionSyntax) valor = $"new {tipo}\n    {valor}"; // «= { … }» no vale tras «=>»
        var prop = ParseMemberDeclaration($"{mods} {tipo} {v.Identifier.Text} => {valor};")!;
        return prop.WithLeadingTrivia(visitado.GetLeadingTrivia()).WithTrailingTrivia(visitado.GetTrailingTrivia());
    }
}
