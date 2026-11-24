using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

public sealed class EquipmentInstance
{
    public string InstanceId { get; }
    public EquipmentDefinition Definition { get; }
    public int CurrentDurability { get; private set; }
    public int CurrentAmmo { get; private set; }

    public bool IsDisabled => CurrentDurability <= 0;
    public bool IsOperational => CurrentDurability > 0;

    /// <summary>Increments whenever <see cref="IsOperational"/> flips; used to invalidate cached Crest stats.</summary>
    public int OperationalVersion { get; private set; }

    public EquipmentInstance(string instanceId, EquipmentDefinition definition)
    {
        InstanceId = instanceId;
        Definition = definition;
        CurrentDurability = definition.MaxDurability;
        CurrentAmmo = definition.MaxAmmo;
    }

    public bool ApplyDamage(int amount)
    {
        if (amount <= 0 || IsDisabled)
            return false;

        CurrentDurability = Math.Max(0, CurrentDurability - amount);
        if (IsDisabled)
            OperationalVersion++;
        return IsDisabled;
    }

    /// <summary>Restores durability (field repair). A disabled component that regains durability is operational again.</summary>
    public void RestoreDurability(int amount)
    {
        if (amount <= 0 || CurrentDurability >= Definition.MaxDurability)
            return;

        bool wasDisabled = IsDisabled;
        CurrentDurability = Math.Min(Definition.MaxDurability, CurrentDurability + amount);
        if (wasDisabled && IsOperational)
            OperationalVersion++;
    }

    public bool TryConsumeAmmo(int amount)
    {
        if (amount <= 0)
            return true;

        if (CurrentAmmo < amount)
            return false;

        CurrentAmmo -= amount;
        return true;
    }

    public void Repair()
    {
        bool wasDisabled = IsDisabled;
        CurrentDurability = Definition.MaxDurability;
        if (wasDisabled && IsOperational)
            OperationalVersion++;
    }

    public void RefillAmmo()
    {
        CurrentAmmo = Definition.MaxAmmo;
    }
}
