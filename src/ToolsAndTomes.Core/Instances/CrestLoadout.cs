using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

/// <summary>
/// A Crest is the assembled collection of its installed component instances.
/// It has no predefined chassis or class; all capabilities emerge from its components.
/// </summary>
public sealed class CrestLoadout
{
    private readonly List<EquipmentInstance> _installed = [];

    public string Id { get; }
    public string Name { get; set; }
    public IReadOnlyList<EquipmentInstance> Installed => _installed;

    public CrestLoadout(string id, string name, IEnumerable<EquipmentInstance>? initialEquipment = null)
    {
        Id = id;
        Name = name;
        if (initialEquipment is not null)
            _installed.AddRange(initialEquipment);
    }

    public static CrestLoadout CreateFromConfiguration(
        CrestConfiguration config,
        GameCatalog catalog,
        string idPrefix = "comp")
    {
        var instances = new List<EquipmentInstance>();
        int index = 1;
        foreach (var compId in config.ComponentIds)
        {
            var def = catalog.GetEquipment(compId);
            instances.Add(new EquipmentInstance($"{idPrefix}_{index++}_{compId}", def));
        }

        return new CrestLoadout(config.Id, config.Name, instances);
    }

    public CrestConfiguration ExportConfiguration(string? overrideId = null, string? description = null) =>
        new()
        {
            Id = overrideId ?? Id,
            Name = Name,
            Description = description ?? $"Saved assembly ({_installed.Count} components).",
            ComponentIds = _installed.Select(e => e.Definition.Id).ToList()
        };

    public void Install(EquipmentInstance instance)
    {
        if (_installed.Any(e => e.InstanceId == instance.InstanceId))
            return;

        _installed.Add(instance);
    }

    public EquipmentInstance? Uninstall(string instanceId)
    {
        var item = _installed.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item is null)
            return null;

        _installed.Remove(item);
        return item;
    }

    public bool TransferComponentTo(string instanceId, CrestLoadout destination)
    {
        if (ReferenceEquals(this, destination))
            return false;

        var removed = Uninstall(instanceId);
        if (removed is null)
            return false;

        destination.Install(removed);
        return true;
    }

    public EquipmentInstance? FindById(string instanceId) =>
        _installed.FirstOrDefault(e => e.InstanceId == instanceId);

    public EquipmentInstance? FindOperationalProviderForAction(string actionId) =>
        _installed.FirstOrDefault(e => e.IsOperational && e.Definition.GrantedActionIds.Contains(actionId));

    public IEnumerable<string> GetOperationalActionIds() =>
        _installed
            .Where(e => e.IsOperational)
            .SelectMany(e => e.Definition.GrantedActionIds)
            .Distinct();

    public void RepairAll()
    {
        foreach (var item in _installed)
        {
            item.Repair();
            item.RefillAmmo();
        }
    }
}
