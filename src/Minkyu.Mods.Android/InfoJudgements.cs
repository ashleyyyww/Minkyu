namespace Minkyu.Mods;

internal sealed class InfoJudgements
{
    private readonly HashSet<int> missed = new();
    private readonly HashSet<int> blocks = new();
    private readonly HashSet<int> holdStarts = new();
    private readonly HashSet<int> playedMulti = new();
    internal int MissOther { get; private set; }
    internal int MissCollectible { get; private set; }
    internal int BlockCount => blocks.Count;
    internal int HoldPairs => holdStarts.Count;

    internal void Block(int id)
    {
        if (!missed.Contains(id)) blocks.Add(id);
    }

    internal void Miss(int id, uint type, bool holdStart = false)
    {
        if (!missed.Add(id)) return;
        blocks.Remove(id);
        if (type is 4 or 6 or 7) MissCollectible++;
        else MissOther++;
        if (holdStart) holdStarts.Add(id);
    }

    internal void MultiHit(int id)
    {
        if (playedMulti.Add(id) && missed.Remove(id)) MissOther--;
    }

    internal void MultiMiss(int id)
    {
        if (!playedMulti.Contains(id)) Miss(id, 8);
    }

    internal float Accuracy(int total, int perfect, int great, int music, int energy, int red, bool realtime)
    {
        if (total <= 0) return 100;
        var counted = perfect + great / 2f + BlockCount + music + energy + red;
        var remaining = Math.Max(0, total - perfect - great - BlockCount - music - energy - red - MissOther - MissCollectible - HoldPairs);
        var denominator = realtime ? total - remaining : total;
        var numerator = realtime ? counted : counted + remaining;
        if (denominator <= 0) return 100;
        var accuracy = Math.Clamp(numerator / denominator, 0, 1);
        const float precision = .0001f;
        var rounded = MathF.Round(accuracy / precision) * precision;
        // Rounding up must not promote the rank.
        if (accuracy < rounded && rounded is .6f or .7f or .8f or .9f or 1f) rounded -= precision;
        return rounded * 100;
    }
}
