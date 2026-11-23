using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

/// <summary>
/// A Crest is the assembled collection of its installed component instances.
/// It has no predefined chassis or class; all capabilities emerge from its components.
/// The order of <see cref="Installed"/> is the Crest's <b>Damage Order</b>: the defender's chosen
/// sequence in which components absorb penetrating damage.
/// </summary>
public sealed class CrestLoadout
{
    private readonly List<EquipmentInstance> _installed = [];

    public string Id { get; }
    public string Name { get; set; }

    /// <summary>Installed components, in Damage Order.</summary>
    public IReadOnlyList<EquipmentInstance> Installed => _installed;

    /// <summary>Components in the order they absorb penetrating damage (same as <see cref="Installed"/>).</summary>
    public IReadOnlyList<EquipmentInstance> DamageOrder => _installed;

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

    /// <summary>Exports the assembly; component order (the Damage Order) is preserved.</summary>
    public CrestConfiguration ExportConfiguration(string? overrideId = null, string? description = null) =>
        new()
        {
            Id = overrideId ?? Id,
            Name = Name,
            Description = description ?? $"Saved assembly ({_installed.Count} components).",
            ComponentIds = _installed.Select(e => e.Definition.Id).ToList()
        };

    /// <summary>
    /// Installs a component. By default it is placed ahead of any Core(s) at the end of the Damage Order,
    /// so the Core keeps its recommended last position.
    /// </summary>
    public void Install(EquipmentInstance instance)
    {
        if (_installed.Any(e => e.InstanceId == instance.InstanceId))
            return;

        int insertAt = _installed.Count;
        if (!instance.Definition.IsCore)
        {
            while (insertAt > 0 && _installed[insertAt - 1].Definition.IsCore)
                insertAt--;
        }

        _installed.Insert(insertAt, instance);
    }

    public EquipmentInstance? Uninstall(string instanceId)
    {
        var item = _installed.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item is null)
            return null;

        _installed.Remove(item);
        return item;
    }

    /// <summary>Moves a component within the Damage Order by <paramref name="delta"/> positions (negative = earlier).</summary>
    public bool MoveInDamageOrder(string instanceId, int delta)
    {
        int index = _installed.FindIndex(e => e.InstanceId == instanceId);
        if (index < 0 || delta == 0)
            return false;

        int newIndex = Math.Clamp(index + delta, 0, _installed.Count - 1);
        if (newIndex == index)
            return false;

        var item = _installed[index];
        _installed.RemoveAt(index);
        _installed.Insert(newIndex, item);
        return true;
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
