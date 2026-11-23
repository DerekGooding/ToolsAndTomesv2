using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

public sealed class CrestLoadout
{
    private readonly List<EquipmentInstance> _installed = [];

    public CrestTemplateDefinition Template { get; }
    public IReadOnlyList<EquipmentInstance> Installed => _installed;

    public CrestLoadout(CrestTemplateDefinition template, IEnumerable<EquipmentInstance>? initialEquipment = null)
    {
        Template = template;
        if (initialEquipment is not null)
        {
            _installed.AddRange(initialEquipment);
        }
    }

    public static CrestLoadout CreateFromTemplate(
        CrestTemplateDefinition template,
        GameCatalog catalog,
        string idPrefix = "eq")
    {
        var instances = new List<EquipmentInstance>();
        int index = 1;
        foreach (var eqId in template.DefaultEquipmentIds)
        {
            var def = catalog.GetEquipment(eqId);
            instances.Add(new EquipmentInstance($"{idPrefix}_{index++}_{eqId}", def));
        }

        return new CrestLoadout(template, instances);
    }

    public void Install(EquipmentInstance instance)
    {
        if (_installed.Any(e => e.InstanceId == instance.InstanceId))
            return;

        if (instance.Definition.IsCore)
        {
            _installed.RemoveAll(e => e.Definition.IsCore);
        }

        _installed.Add(instance);
    }

    public EquipmentInstance? Uninstall(string instanceId)
    {
        var item = _installed.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item is null || item.Definition.IsCore)
            return null;

        _installed.Remove(item);
        return item;
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
