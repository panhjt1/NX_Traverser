/// <summary>
/// One instance per run. The driver calls Process for each matched target, then
/// Finish exactly once even on cancellation or failure. Process borrows context:
/// do not close source parts, own its lifetime, or retain NX objects after return.
/// Throw operation failures; the driver owns logging, counting and continuation.
/// </summary>
public interface ITransactionHandler
{
    void Process(TransactionContext context);
    void Finish();
}
