// Both handlers share one assembly occurrence/display scope per target.
public interface ITransactionHandler
{
    void Process(NxAssemblyExportContext context, string outputFolder, string descriptiveName);
    void Finish();
}
