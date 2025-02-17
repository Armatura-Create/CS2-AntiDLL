namespace AntiDLL
{
    using CounterStrikeSharp.API.Core;
    using CounterStrikeSharp.API.Core.Attributes;

    [MinimumApiVersion(version: 305)]
    public sealed partial class Plugin : BasePlugin
    {
        public override string ModuleName => "CS2 AntiDLL";

        public override string ModuleAuthor => "Nexd @ Eternar (https://eternar.dev) fork by Armatura";

        public override string ModuleVersion => "1.0.0 " +
#if RELEASE
            "(release)";
#else
            "(debug)";
#endif
    }
}
