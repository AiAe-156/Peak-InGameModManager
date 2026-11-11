from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:70], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')

# 1. Editor rollback keeps parentId.
sub('OverviewGroupEditor.cs',
    'new OverviewGroup { id = g.id, name = g.name, description = g.description, color = g.color, order = g.order }',
    'new OverviewGroup { id = g.id, name = g.name, description = g.description, color = g.color, order = g.order, parentId = g.parentId }')

# 2. Change review shows parent changes.
sub('ProfileChangeReview.cs',
    '("order", Loc.T("Order", "顺序", "順序")) };\n        foreach (string id in previous.state.groups',
    '("order", Loc.T("Order", "顺序", "順序")), ("parentId", Loc.T("Parent group", "上级分组", "上級分組")) };\n        foreach (string id in previous.state.groups')

# 3. Profile documents: written as 2 when nested, read as 1 or 2. Config snapshots and indexes stay 1.
sub('ProfilePairPersistence.cs',
    '''        foreach (var json in new[] { profile, config })
            if (json["schemaVersion"]?.Type != JTokenType.Integer || json.Value<int>("schemaVersion") != 1)
                throw new InvalidDataException("Profile pair schemaVersion must be integer 1.");''',
    '''        // profile.json is version 2 when it carries nested groups (older versions accept only 1 and so refuse it);
        // the configuration snapshot never changed shape and stays 1.
        if (profile["schemaVersion"]?.Type != JTokenType.Integer || !GroupTree.SupportedSchema(profile.Value<int>("schemaVersion")))
            throw new InvalidDataException("Profile schemaVersion must be integer 1 or 2.");
        if (config["schemaVersion"]?.Type != JTokenType.Integer || config.Value<int>("schemaVersion") != 1)
            throw new InvalidDataException("Configuration snapshot schemaVersion must be integer 1.");''')

sub('OverviewProfileStore.cs',
    '        var persisted = Clone(profile); persisted.config = new OverviewConfigSnapshot(); persisted.directory = ""; persisted.loadError = "";',
    '        var persisted = Clone(profile); persisted.config = new OverviewConfigSnapshot(); persisted.directory = ""; persisted.loadError = "";\n'
    '        persisted.schemaVersion = GroupTree.SchemaFor(persisted.state?.groups);')
sub('OverviewProfileStore.cs',
    '            if (importedSource.schemaVersion != SchemaVersion)\n',
    '            if (!GroupTree.SupportedSchema(importedSource.schemaVersion))\n')
sub('OverviewProfileStore.cs',
    '            if (profile.schemaVersion != SchemaVersion) throw new InvalidDataException(Loc.F("Unsupported profile version {0}"',
    '            if (!GroupTree.SupportedSchema(profile.schemaVersion)) throw new InvalidDataException(Loc.F("Unsupported profile version {0}"')
# RequireImportSchema is used for both profile.json and config-values.json of an import: accept 2 only for a profile document.
sub('OverviewProfileStore.cs',
    '''        JToken? schema = json["schemaVersion"];
        if (schema == null || schema.Type != JTokenType.Integer || schema.Value<int>() != SchemaVersion)''',
    '''        JToken? schema = json["schemaVersion"];
        bool profileDocument = Path.GetFileName(path).Equals("profile.json", StringComparison.OrdinalIgnoreCase);
        if (schema == null || schema.Type != JTokenType.Integer || !(profileDocument ? GroupTree.SupportedSchema(schema.Value<int>()) : schema.Value<int>() == SchemaVersion))''')

# 4. Export writes the same rule into each exported profile.json.
sub('OverviewProfileExport.cs',
    '            CleanProfile(profile, id, item.Name);\n',
    '            CleanProfile(profile, id, item.Name);\n'
    '            profile["schemaVersion"] = GroupTree.HasNesting(profile["state"]?["groups"]?.ToObject<List<OverviewGroup>>()) ? 2 : 1;\n')

# 5. Workspace pack-information.json.
sub('PackWorkspaceInformation.cs',
    '        if (information.schemaVersion != 1 || information.pack == null',
    '        if (!GroupTree.SupportedSchema(information.schemaVersion) || information.pack == null')
sub('PackWorkspaceInformation.cs',
    '        var information = new PackWorkspaceInformation { pack = state.pack, groups = state.groups, rules = state.mods, resourceDirectory = resources };',
    '        var information = new PackWorkspaceInformation { schemaVersion = GroupTree.SchemaFor(state.groups), pack = state.pack, groups = state.groups, rules = state.mods, resourceDirectory = resources };')
print('ok')
