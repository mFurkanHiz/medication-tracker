namespace MedicationTracker.Api.Domain.Inventory;

/// <summary>
/// Lifecycle of one physical package.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Empty</c> member. Emptiness is derived from a zero
/// ledger balance, so a package cannot be marked empty while still holding stock, or
/// hold a stale "empty" flag after a correction puts stock back into it. Only facts
/// that the ledger cannot express live here.
/// </remarks>
public enum PackageState
{
    /// <summary>Unopened. Its balance equals its nominal capacity.</summary>
    Sealed = 0,

    /// <summary>Opened at least once. May still be full, may be empty.</summary>
    Opened = 1,

    /// <summary>Thrown away or expired out. Never selected for consumption.</summary>
    Disposed = 2,

    /// <summary>Physically missing. Never selected for consumption.</summary>
    Lost = 3,

    /// <summary>Retired from view without asserting what happened to it.</summary>
    Archived = 4,
}

/// <summary>
/// Why a ledger entry exists. Replaces the previous free-text <c>reason</c> column.
/// </summary>
/// <remarks>
/// Every entry is an append-only signed delta. Corrections are expressed as a
/// <see cref="CorrectionReversal"/> paired with a <see cref="CorrectionConsume"/>
/// under one correlation id, never as an update to the entry being corrected.
/// </remarks>
public enum LedgerEntryType
{
    /// <summary>Stock entered the household (purchase, prescription, gift).</summary>
    Acquire = 0,

    /// <summary>Stock was consumed by a recorded administration.</summary>
    Consume = 1,

    /// <summary>Credit that undoes an earlier entry during a correction.</summary>
    CorrectionReversal = 2,

    /// <summary>Debit that re-applies a corrected consumption to the right source.</summary>
    CorrectionConsume = 3,

    /// <summary>Stock the household found that the ledger did not know about.</summary>
    Found = 4,

    /// <summary>Stock lost, spilled or damaged.</summary>
    Loss = 5,

    /// <summary>Stock deliberately discarded, for example after expiry.</summary>
    Dispose = 6,

    /// <summary>Adjustment accepted from a counted/reconciled session.</summary>
    CountAdjustment = 7,

    /// <summary>Moving loose stock into a package, or between packages. Nets to zero.</summary>
    PackageTransfer = 8,

    /// <summary>Manual correction of an earlier acquisition or adjustment.</summary>
    ManualAdjustment = 9,
}

/// <summary>
/// Whether a ledger entry type may be chosen directly by a user-facing stock edit.
/// Consumption and correction entries are written only by the administration and
/// correction flows, so they are not offered as manual stock adjustments.
/// </summary>
public static class LedgerEntryTypes
{
    public static bool IsManuallySelectable(LedgerEntryType type) => type is
        LedgerEntryType.Acquire or
        LedgerEntryType.Found or
        LedgerEntryType.Loss or
        LedgerEntryType.Dispose or
        LedgerEntryType.ManualAdjustment;

    /// <summary>Entry types that must carry a negative quantity.</summary>
    public static bool IsAlwaysNegative(LedgerEntryType type) => type is
        LedgerEntryType.Consume or
        LedgerEntryType.CorrectionConsume or
        LedgerEntryType.Loss or
        LedgerEntryType.Dispose;

    /// <summary>Entry types that must carry a positive quantity.</summary>
    public static bool IsAlwaysPositive(LedgerEntryType type) => type is
        LedgerEntryType.Acquire or
        LedgerEntryType.Found or
        LedgerEntryType.CorrectionReversal;
}
