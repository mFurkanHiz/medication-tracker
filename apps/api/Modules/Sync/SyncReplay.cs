using MedicationTracker.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MedicationTracker.Api.Modules.Sync;

/// <summary>
/// The three lines every create that a phone may send twice needs: is the key well
/// formed, was it already applied, and remember that it was.
/// </summary>
/// <remarks>
/// A phone creates a person, a medicine or a plan offline under an id it chose, queues
/// the command, and sends it on reconnect — perhaps twice, when the first answer was
/// lost. The receipt turns the second send into the first answer. The caller holds the
/// household lock, so two sends of the same key cannot both miss the receipt.
/// </remarks>
internal static class SyncReplay
{
    public static bool IsValidKey(string? key) =>
        key is null || (!string.IsNullOrWhiteSpace(key) && key.Length <= 100);

    public static Task<SyncCommandReceipt?> PriorAsync(
        MedicationTrackerDbContext db, Guid householdId, string? key, CancellationToken ct) =>
        key is null
            ? Task.FromResult<SyncCommandReceipt?>(null)
            : db.SyncCommandReceipts.AsNoTracking().SingleOrDefaultAsync(
                receipt => receipt.HouseholdId == householdId && receipt.IdempotencyKey == key, ct);

    /// <summary>A client id, or null when none was sent; the empty guid counts as none.</summary>
    public static Guid? ClientId(Guid? id) => id is { } value && value != Guid.Empty ? value : null;

    public static void Record(
        MedicationTrackerDbContext db, Guid householdId, Guid accountId, string? key, string kind,
        Guid entityId, DateTimeOffset now)
    {
        if (key is not null)
        {
            db.SyncCommandReceipts.Add(new SyncCommandReceipt(
                Guid.CreateVersion7(), householdId, accountId, key, kind, entityId, now));
        }
    }
}
