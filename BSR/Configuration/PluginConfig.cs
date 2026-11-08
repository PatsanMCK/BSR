using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace BSR.Configuration
{
    public class PluginConfig
    {
        public static PluginConfig Instance { get; set; }
        // CIS = region (usually referred to post-soviet countries), not gender-related.
        public virtual string ServerUrl { get; set; } = "ws://ranked.beatsabercis.dpdns.org:8080";
        public virtual bool EnableScoreSubmission { get; set; } = false;
    }
}