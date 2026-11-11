from pathlib import Path
p=Path('src/Updating/ComponentDependencyGraph.cs');s=p.read_text(encoding='utf8');s=s.replace('    public readonly List<Edge> Edges', '    public readonly List<string[]> SharedFileOwners = new List<string[]>();\n    public readonly List<Edge> Edges');needle='        if (target != null && paths.TryGetValue(target.Id, out var path))';s=s.replace(needle,'''        // Co-owner veto only: do not traverse a different package's private requirements.
        bool sharedChanged;
        do
        {
            sharedChanged = false;
            foreach (var owners in SharedFileOwners)
            {
                var requiredOwner = owners.FirstOrDefault(o => paths.ContainsKey(o));
                if (requiredOwner == null) continue;
                foreach (var owner in owners)
                    if (!paths.ContainsKey(owner)) { paths[owner] = paths[requiredOwner].Concat(new[] { owner }).ToList(); sharedChanged = true; }
            }
        } while (sharedChanged);
'''+needle);p.write_text(s,encoding='utf8')
p=Path('src/Updating/ComponentDependencyCollector.cs');s=p.read_text(encoding='utf8');s=s.replace('        return graph;','''        foreach (string file in files)
        {
            var owners = Owners(file);
            if (owners.Count > 1) graph.SharedFileOwners.Add(owners.Select(o => o.Id).ToArray());
        }
        return graph;''');p.write_text(s,encoding='utf8')
# worker confirmed identity fallback requires matching file hash, never name-only
p=Path('Updater/ShopComponentProtection.cs');s=p.read_text(encoding='utf8');s=s.replace('Hash = f.Hash }));','Hash = f.Hash })).ToArray();');s=s.replace('            var package = ManifestPackage(file);','''            ShopPackage package;
            try { package = ManifestPackage(file); }
            catch (IOException)
            {
                var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
                string name = data?["name"]?.GetValue<string>() ?? "";
                string directory = Path.GetDirectoryName(file)!;
                var keys = claims.Where(c => Files.Child(storage.Root, c.Path).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && c.Hash.Length > 0 && File.Exists(Files.Child(storage.Root, c.Path)) && Files.Hash(Files.Child(storage.Root, c.Path)) == c.Hash)
                    .Select(c => c.PackageKey).Distinct(Files.Compare).ToArray();
                if (keys.Length != 1 || !keys[0].EndsWith("-" + name, StringComparison.OrdinalIgnoreCase)
                    || !Version.TryParse(data?["version_number"]?.GetValue<string>(), out _)
                    || (data?["namespace"]?.GetValue<string>() ?? "").Length > 0
                    || (data?["author"]?.GetValue<string>() ?? "").Length > 0) throw;
                package = new ShopPackage { Key = keys[0], Dependencies = (data?["dependencies"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray()).Select(d => d!.GetValue<string>()).ToList() };
            }''');p.write_text(s,encoding='utf8')
