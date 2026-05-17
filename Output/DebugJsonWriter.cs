using System.Text.Json;
using System.Text.Encodings.Web;
using derbyhubDb.Effects;
using derbyhubDb.StoryData;

namespace derbyhubDb.Output;

public static class DebugJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(string debugDir, StoryReadResult storyResult, EffectLoadResult effectResult)
    {
        File.WriteAllText(
            Path.Combine(debugDir, "story-events.debug.json"),
            JsonSerializer.Serialize(storyResult.Stories.Take(500), Options));
        File.WriteAllText(
            Path.Combine(debugDir, "unmatched-kamigame.debug.json"),
            JsonSerializer.Serialize(effectResult.UnmatchedEvents, Options));
        File.WriteAllText(
            Path.Combine(debugDir, "missing-master-story.debug.json"),
            JsonSerializer.Serialize(storyResult.MissingMasterStories, Options));
    }
}
