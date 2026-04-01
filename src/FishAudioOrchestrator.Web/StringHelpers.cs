namespace FishAudioOrchestrator.Web;

public static class StringHelpers
{
    public static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
