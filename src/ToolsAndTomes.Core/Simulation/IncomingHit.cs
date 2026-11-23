using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public sealed record IncomingHit(
    string SourceActorId,
    string SourceActionName,
    string TargetActorId,
    Vec2 SourcePosition,
    int DamagePoints,
    int PushDistance,
    int InterruptionPower);
