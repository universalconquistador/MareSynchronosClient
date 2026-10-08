
namespace MareSynchronos.PlayerData.Pairs;

public enum PairDataExceptionReason
{
    CombatOrPerformance,
    GposeOrCutscene,
    InvalidCharacterState,
    ApplicationTaskRunning,
    ThresholdViolation,
    IpcInvocationError
}

public class PairDataApplicationException : Exception
{
    public PairDataExceptionReason Reason { get; }

    public PairDataApplicationException(PairDataExceptionReason reason, string msg) : base(msg)
    {
        Reason = reason;
    }
}
