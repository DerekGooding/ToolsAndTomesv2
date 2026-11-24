namespace ToolsAndTomes.Core.Instances;

public sealed class ResourceState
{
    private readonly Dictionary<string, int> _witReservations = [];

    public int CurrentMotes { get; private set; }
    public int MoteGenerationProgressBeats { get; private set; }
    public int CurrentHeat { get; private set; }
    public bool IsOverheated { get; private set; }
    public int FastIron { get; private set; }

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

    public void AddFastIron(int amount)
    {
        if (amount > 0)
            FastIron += amount;
    }

    public bool TrySpendFastIron(int amount)
    {
        if (amount <= 0)
            return true;
        if (FastIron < amount)
            return false;

        FastIron -= amount;
        return true;
    }

    /// <summary>Advances Mote generation by one beat. Returns true when a Mote is generated.</summary>
    public bool AdvanceMoteTimer(int beatsPerMote, int maxMotes)
    {
        if (CurrentMotes >= maxMotes)
        {
            MoteGenerationProgressBeats = 0;
            return false;
        }

        MoteGenerationProgressBeats++;
        if (MoteGenerationProgressBeats >= beatsPerMote)
        {
            MoteGenerationProgressBeats = 0;
            CurrentMotes = Math.Min(maxMotes, CurrentMotes + 1);
            return true;
        }

        return false;
    }

    public void ResetHeat()
    {
        CurrentHeat = 0;
        IsOverheated = false;
    }

    /// <summary>Adds heat. Returns true if this addition caused an overheat.</summary>
    public bool AddHeat(int amount, int heatCapacity)
    {
        if (amount <= 0)
            return false;

        CurrentHeat += amount;
        if (!IsOverheated && CurrentHeat >= heatCapacity)
        {
            IsOverheated = true;
            return true;
        }

        return false;
    }

    /// <summary>Dissipates heat for one beat. Overheat clears once heat falls to <paramref name="clearPercent"/>% of capacity or less.</summary>
    public void CoolDown(int dissipationPerBeat, int heatCapacity, int clearPercent = 40)
    {
        if (CurrentHeat <= 0)
            return;

        CurrentHeat = Math.Max(0, CurrentHeat - dissipationPerBeat);
        if (IsOverheated && CurrentHeat * 100 <= heatCapacity * clearPercent)
        {
            IsOverheated = false;
        }
    }
}
