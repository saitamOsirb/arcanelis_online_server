using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTibia.Server.Api.Data;

public sealed class CharacterTemplate
{
    private readonly JsonObject _template;

    public CharacterTemplate(IWebHostEnvironment env, IConfiguration cfg)
    {
        // Archivo esperado: data/templates/character.json
        var candidates = new List<string>();

        // 1) data/ al lado del ejecutable (si lo copiaste al output)
        candidates.Add(Path.Combine(env.ContentRootPath, "data", "templates", "character.json"));

        // 2) BasePath configurable (recomendado: ../data)
        var basePathCfg = cfg["Data:BasePath"];
        if (!string.IsNullOrWhiteSpace(basePathCfg))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(env.ContentRootPath, basePathCfg, "templates", "character.json")));
        }

        // 3) Fallbacks típicos para cuando el working dir cambia
        candidates.Add(Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "data", "templates", "character.json")));
        candidates.Add(Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "data", "templates", "character.json")));

        var path = candidates.FirstOrDefault(File.Exists);

        if (path is null)
        {
            var msg = "No se encontró el template. Probé:\n- " + string.Join("\n- ", candidates);
            throw new FileNotFoundException(msg);
        }

        var json = File.ReadAllText(path);
        _template = JsonNode.Parse(json)?.AsObject()
                    ?? throw new InvalidOperationException($"Template JSON inválido (no es objeto raíz). Path: {path}");
    }

    public string CreatePlayerJson(string name, int sex)
    {
        var node = JsonNode.Parse(_template.ToJsonString())!.AsObject();

        // Estas rutas dependen de tu template real.
        node["creatureStatistics"]!["name"] = name;
        node["characterStatistics"]!["sex"] = sex;

        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}  