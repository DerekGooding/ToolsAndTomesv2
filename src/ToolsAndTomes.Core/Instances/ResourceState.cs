namespace ToolsAndTomes.Core.Instances;

public sealed class ResourceState
{
    private readonly Dictionary<string, int> _witReservations = [];

    public int CurrentMotes { get; private set; }
    public int MoteGenerationProgressTicks { get; private set; }
    public double CurrentHeat { get; private set; }
    public bool IsOverheated { get; private set; }

    public int ReservedWit => _witReservations.Values.Sum();

    public ResourceState(int startingMotes = 1)
    {
        CurrentMotes = Math.Max(0, startingMotes);
    }

    public void ReserveWit(string key, int amount)
    {
        if (amount <= 0)
            return;

        _witReservations[key] = amount;
    }

    public void ReleaseWit(string key)
    {
        _witReservations.Remove(key);
    }

    public void ReleaseAllWit()
    {
        _witReservations.Clear();
    }

    public bool TrySpendMotes(int amount)
    {
        if (amount <= 0)
            return true;

        if (CurrentMotes < amount)
            return false;

        CurrentMotes -= amount;
        return true;
    }

    public void AddMotes(int amount, int maxMotes)
    {
        if (amount <= 0)
            return;

        CurrentMotes = Math.Min(maxMotes, CurrentMotes + amount);
    }

    public bool AdvanceMoteTimer(int ticksPerMote, int maxMotes)
    {
        if (CurrentMotes >= maxMotes)
        {
            MoteGenerationProgressTicks = 0;
            return false;
        }

        MoteGenerationProgressTicks++;
        if (MoteGenerationProgressTicks >= ticksPerMote)
        {
            MoteGenerationProgressTicks -= ticksPerMote;
            CurrentMotes = Math.Min(maxMotes, CurrentMotes + 1);
            return true;
        }

        return false;
    }

    public bool AddHeat(double amount, double heatCapacity)
    {
        if (amount <= 0)
            return false;

        CurrentHeat += amount;
        if (CurrentHeat >= heatCapacity)
        {
            IsOverheated = true;
            return true;
        }

        return false;
    }

    public void CoolDown(double dissipationPerTick, double heatCapacity)
    {
        if (CurrentHeat <= 0)
            return;

        CurrentHeat = Math.Max(0, CurrentHeat - dissipationPerTick);
        if (IsOverheated && CurrentHeat <= heatCapacity * 0.4)
        {
            IsOverheated = false;
        }
    }
}
