namespace Minkyu.Mods;

internal sealed record InfoField(string Id, string Name, string Text, int Size, string Font, bool Enabled = true);

internal static class InfoFields
{
    internal static readonly InfoField[] All =
    {
        new("UpperLeft", "Upper left", "<b>{overview}</b>", 60, "Snaps Taste"),
        new("UpperRight", "Upper right", "{song}\\n<size=27>{level} - Level {diff}</size>", 38, "Normal"),
        new("ScoreRight", "Beside score", "{scoreGap}", 38, "Snaps Taste"),
        new("ScoreBelow", "Beside SCORE label", "{accGap} / {stats}", 36, "Snaps Taste"),
        new("LowerLeft", "Lower left", "{hit} of {total} notes", 30, "Snaps Taste"),
        new("LowerRight", "Lower right", "Sky Speed: {skySpeed}x\\nGround Speed: {groundSpeed}x", 30, "Lato", false)
    };

    internal static IEnumerable<Setting> Settings()
    {
        foreach (var field in All)
        {
            var key = "Info.Field." + field.Id + ".";
            yield return new(key + "Enabled", "Info+ display", "Show this area", field.Enabled);
            yield return new(key + "Text", "Info+ display", "Text", field.Text);
            yield return new(key + "Size", "Info+ display", "Text size", field.Size, 16, 80);
            yield return new(key + "Font", "Info+ display", "Font", field.Font, Choices: new[] { "Snaps Taste", "Normal", "Lato", "Luckiest Guy" });
            yield return new(key + "Color", "Info+ display", "Text color", "White", Choices: new[] { "White", "Pink", "Yellow", "Cyan" });
            yield return new(key + "OffsetX", "Info+ display", "Horizontal offset", field.Id == "UpperLeft" ? 20 : 0, -300, 300);
            yield return new(key + "OffsetY", "Info+ display", "Vertical offset", field.Id == "UpperLeft" ? 30 : 0, -300, 300);
            yield return new(key + "Outline", "Info+ display", "Text outline", false);
            yield return new(key + "OutlineColor", "Info+ display", "Outline color", "White", Choices: new[] { "White", "Black", "Pink", "Yellow", "Cyan" });
            yield return new(key + "OutlineWidth", "Info+ display", "Outline width", field.Id == "UpperLeft" ? 3 : 2, 1, 6);
        }
    }
}
