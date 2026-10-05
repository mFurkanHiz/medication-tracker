using System.Globalization;
using System.Text;

namespace MedicationTracker.Api.Domain.Catalog;

/// <summary>One medicine as the matcher sees it: how it is named, and what it was tagged against.</summary>
public sealed record MedicineIdentity(
    Guid Id,
    string Name,
    string? Brand,
    IReadOnlyList<string> ActiveIngredients,
    IReadOnlyList<string> DoNotTakeWithTags);

/// <summary>
/// One warning on one medicine's row: the other medicine, the words that matched, and
/// whose note the warning comes from.
/// </summary>
public sealed record DoNotTakeWithConflict(
    Guid MedicationDefinitionId,
    string MedicationName,
    IReadOnlyList<string> Matched,
    Guid NotedOnMedicationDefinitionId,
    string NotedOn);

/// <summary>
/// Matches the household's own "do not take with" tags against the names and active
/// ingredients of the other medicines the same person has on the same day.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the product cross-references two medicines, and the whole of
/// what it does is string equality on words the household typed. Both halves of every
/// match are the household's own record — the tag on one medicine, the name or
/// ingredient on another — so the warning reminds them of their own note and claims
/// nothing of its own. There is no drug database, no synonym list and no inference, and
/// a word the household did not write cannot match. Silence therefore means only that
/// the household wrote nothing that matched, never that two medicines are safe
/// together; the screen says so next to every warning. ADR 0016.
/// </para>
/// <para>
/// Equality is on a normalised form: case folded, diacritics removed and spaces and
/// punctuation dropped, so "C Vitamini" and "c vitamini" are one word and the Turkish
/// dotted and dotless i do not split a match. "cvitamine" still does not match
/// "cvitamini" — that would be guessing, and guessing is what this must not do.
/// </para>
/// <para>
/// The warning is placed on both rows: the medicine whose note named the other, with the
/// words that matched as the reason; and the medicine that was named, with the noting
/// medicine as the reason — the owner's example, "Allerset: Parol ile beraber almayınız,
/// nedeni: Parol, Paracetamol" and "Parol: … nedeni: Allerset".
/// </para>
/// </remarks>
public static class DoNotTakeWithMatcher
{
    /// <summary>The warnings for each medicine among medicines one person has on one day.</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<DoNotTakeWithConflict>> Among(
        IReadOnlyCollection<MedicineIdentity> medicines)
    {
        ArgumentNullException.ThrowIfNull(medicines);

        var found = medicines.ToDictionary(medicine => medicine.Id, _ => new List<DoNotTakeWithConflict>());

        foreach (var noting in medicines)
        {
            var tags = noting.DoNotTakeWithTags
                .Select(Normalize)
                .Where(tag => tag.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            if (tags.Count == 0)
            {
                continue;
            }

            foreach (var other in medicines)
            {
                if (other.Id == noting.Id)
                {
                    continue;
                }

                var matched = Terms(other)
                    .Where(term => tags.Contains(term.Normalized))
                    .Select(term => term.Display)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (matched.Count == 0)
                {
                    continue;
                }

                found[noting.Id].Add(new DoNotTakeWithConflict(other.Id, other.Name, matched, noting.Id, noting.Name));
                found[other.Id].Add(new DoNotTakeWithConflict(noting.Id, noting.Name, [noting.Name], noting.Id, noting.Name));
            }
        }

        return found.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<DoNotTakeWithConflict>)Merge(pair.Value));
    }

    /// <summary>
    /// Case folded, diacritics removed, nothing but letters and digits — so that two
    /// spellings of the same word the household typed twice are one word, and nothing
    /// else is.
    /// </summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark
                || !char.IsLetterOrDigit(character))
            {
                continue;
            }

            // Dotted İ decomposes to I plus a mark the loop above dropped; dotless ı does
            // not decompose at all. Both are the letter i for the purpose of a match.
            builder.Append(character is 'ı' or 'I' or 'İ' ? 'i' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static IEnumerable<(string Display, string Normalized)> Terms(MedicineIdentity medicine)
    {
        yield return (medicine.Name, Normalize(medicine.Name));

        if (!string.IsNullOrWhiteSpace(medicine.Brand))
        {
            yield return (medicine.Brand, Normalize(medicine.Brand));
        }

        foreach (var ingredient in medicine.ActiveIngredients)
        {
            yield return (ingredient, Normalize(ingredient));
        }
    }

    /// <summary>Two medicines that name each other produce one warning per row, not two.</summary>
    private static List<DoNotTakeWithConflict> Merge(List<DoNotTakeWithConflict> conflicts) =>
        conflicts
            .GroupBy(conflict => conflict.MedicationDefinitionId)
            .Select(group => group.First() with
            {
                Matched = group.SelectMany(conflict => conflict.Matched)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .ToList();
}
