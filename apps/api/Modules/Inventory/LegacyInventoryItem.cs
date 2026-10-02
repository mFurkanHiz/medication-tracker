namespace MedicationTracker.Api.Modules.Inventory;

/// <summary>
/// The superseded one-row-per-medication stock holder.
/// </summary>
/// <remarks>
/// <para>
/// A unique index made this strictly 1:1 with a medication, so it never carried
/// information of its own — it was an indirection between a medication and its ledger.
/// The rebuilt model addresses stock by medication definition and physical package
/// directly.
/// </para>
/// <para>
/// The type and its table are kept because existing ledger entries, count sessions and
/// packages hold foreign keys to it, and dropping it would destroy the production audit
/// trail. New rows are still created alongside each definition so those foreign keys
/// stay satisfiable. See ADR 0014.
/// </para>
/// </remarks>
public sealed class LegacyInventoryItem
{
    private LegacyInventoryItem() { }

    public LegacyInventoryItem(Guid id, Guid householdId, Guid medicationDefinitionId, DateTimeOffset createdAt)
    {
        Id = id;
        HouseholdId = householdId;
        MedicationDefinitionId = medicationDefinitionId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid HouseholdId { get; private set; }

    public Guid MedicationDefinitionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
