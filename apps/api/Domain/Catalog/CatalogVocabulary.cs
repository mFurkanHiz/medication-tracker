namespace MedicationTracker.Api.Domain.Catalog;

/// <summary>
/// How a medication is presented physically. Descriptive only: the product never
/// derives a dose, a conversion or any clinical conclusion from this value.
/// </summary>
public enum PharmaceuticalForm
{
    Tablet = 0,
    Capsule = 1,
    OralLiquid = 2,
    Drops = 3,
    Sachet = 4,
    Suppository = 5,
    Injection = 6,
    Cream = 7,
    Ointment = 8,
    Gel = 9,
    Patch = 10,
    InhalerSpray = 11,
    NasalSpray = 12,
    EyeDrops = 13,
    EarDrops = 14,
    Other = 99,
}

/// <summary>
/// The unit a package is counted in and a dose is expressed in. Capacity, remaining
/// amount, dose and every ledger entry for one medication share this unit, so no
/// unit conversion is ever performed on a medication amount.
/// </summary>
public enum MedicationUnit
{
    Tablet = 0,
    Capsule = 1,
    Milliliter = 2,
    Gram = 3,
    Milligram = 4,
    Drop = 5,
    Puff = 6,
    Patch = 7,
    Sachet = 8,
    Suppository = 9,
    Ampoule = 10,
    InternationalUnit = 11,
    Dose = 12,
}

/// <summary>
/// Forms whose default counting unit is a discrete countable object. Used to pick a
/// sensible default unit when the user has not chosen one; never to restrict them.
/// </summary>
public static class FormDefaults
{
    public static MedicationUnit DefaultUnitFor(PharmaceuticalForm form) => form switch
    {
        PharmaceuticalForm.Tablet => MedicationUnit.Tablet,
        PharmaceuticalForm.Capsule => MedicationUnit.Capsule,
        PharmaceuticalForm.OralLiquid => MedicationUnit.Milliliter,
        PharmaceuticalForm.Drops or PharmaceuticalForm.EyeDrops or PharmaceuticalForm.EarDrops => MedicationUnit.Drop,
        PharmaceuticalForm.Sachet => MedicationUnit.Sachet,
        PharmaceuticalForm.Suppository => MedicationUnit.Suppository,
        PharmaceuticalForm.Injection => MedicationUnit.Ampoule,
        PharmaceuticalForm.Cream or PharmaceuticalForm.Ointment or PharmaceuticalForm.Gel => MedicationUnit.Gram,
        PharmaceuticalForm.Patch => MedicationUnit.Patch,
        PharmaceuticalForm.InhalerSpray or PharmaceuticalForm.NasalSpray => MedicationUnit.Puff,
        _ => MedicationUnit.Dose,
    };
}
