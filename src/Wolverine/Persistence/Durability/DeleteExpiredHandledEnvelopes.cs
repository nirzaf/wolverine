using System;
using System.Threading.Tasks;

namespace Wolverine.Persistence.Durability;

public class DeleteExpiredHandledEnvelopes : IMessagingAction
{
    public string Description { get; } = "Deleting Expired, Handled Envelopes";
    public async Task ExecuteAsync(IEnvelopePersistence storage, IDurabilityAgent agent)
    {
        await storage.Session.BeginAsync();


        try
        {
            await storage.DeleteExpiredHandledEnvelopesAsync(DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            await storage.Session.RollbackAsync();
            throw;
        }

        await storage.Session.CommitAsync();
    }
}