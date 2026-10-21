namespace PHYSXR.Core.Enums
{
    // Evaluation condition for a trial (spec section B, "Run conditions").
    //
    //   VIRTUAL_ONLY           packets are still received and logged, but the
    //                          physical part of WorldState is forced neutral
    //   PHYSICAL_PLUS_VIRTUAL  live packets from the sensor station over UDP
    //   SIMULATED              packets from the in-game simulator, through the
    //                          same validator and pipeline as real packets
    public enum RunCondition
    {
        VIRTUAL_ONLY = 0,
        PHYSICAL_PLUS_VIRTUAL,
        SIMULATED
    }
}
