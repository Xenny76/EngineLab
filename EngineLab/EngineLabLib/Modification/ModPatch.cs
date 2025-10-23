namespace EngineLabLib.Modification
{
    public sealed class ModPatch
    {
        public readonly List<SetOp> Sets = [];
        public sealed record SetOp(string Path, object? Value);
    }

}