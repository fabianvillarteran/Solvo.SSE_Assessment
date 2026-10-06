using System.Globalization;
using System.Linq;

namespace DedupTool;

/// <summary>
/// Suppresses duplicate documents in a batch.
///
/// - Loose files are duplicates of each other if they share a SHA-256 (spec 3.1).
/// - Attachments are duplicates of each other only *within the same family*
///   (spec 3.2 scoped by 3.5): an attachment that happens to be byte-identical
///   to an attachment on a different email must not be suppressed, or that
///   other family loses it entirely (INGEST-4471).
/// - Emails are duplicates of each other if they were collected from
///   different mailboxes but are the same logical message (spec 3.3): same
///   participants, subject, instant and body, even though raw bytes (and
///   therefore sha256) differ because of per-mailbox header/encoding/line-
///   ending differences.
///
/// Within each of those duplicate groups, the lexicographically smallest
/// DocId is kept, so the result does not depend on input order (spec 6.1).
/// When a suppressed email had its own attachments, those attachments are
/// re-parented onto the surviving email so they don't point at a FamilyID
/// that no longer has a row.
/// </summary>
public static class Deduper
{
    public static DedupResult Run(Batch batch)
    {
        var byId = batch.Documents.ToDictionary(d => d.DocId);

        // Pick a deterministic survivor per duplicate-content group - "lowest
        // DocId wins" rather than "first in the input array wins".
        var emailSurvivor = Canonicalize(batch.Documents.Where(d => d.DocType == "email"), EmailContentKey);

        // Resolve each attachment's effective parent through the email
        // canonicalization, so an attachment whose parent email got
        // suppressed as a duplicate re-parents onto the surviving copy.
        var resolvedParentId = new Dictionary<string, string>();
        foreach (var d in batch.Documents)
        {
            if (d.ParentId != null)
            {
                resolvedParentId[d.DocId] = emailSurvivor.GetValueOrDefault(d.ParentId, d.ParentId);
            }
        }

        var attachmentSurvivor = Canonicalize(
            batch.Documents.Where(d => d.DocType == "attachment"),
            d => $"{resolvedParentId[d.DocId]}|{d.Sha256}");

        var looseSurvivor = Canonicalize(batch.Documents.Where(d => d.DocType == "loose"), d => d.Sha256);

        var rows = new List<LoadFileRow>();
        foreach (var d in batch.Documents)
        {
            var isSurvivor = d.DocType switch
            {
                "email" => emailSurvivor[d.DocId] == d.DocId,
                "attachment" => attachmentSurvivor[d.DocId] == d.DocId,
                "loose" => looseSurvivor[d.DocId] == d.DocId,
                _ => true,
            };

            if (!isSurvivor)
            {
                continue;
            }

            string familyId;
            string parentId;
            string sentDate;

            if (d.ParentId != null)
            {
                parentId = resolvedParentId[d.DocId];
                familyId = parentId;
                sentDate = FormatSentDateUtc(byId[parentId].SentDate);
            }
            else
            {
                parentId = "";
                familyId = d.DocId;
                sentDate = FormatSentDateUtc(d.SentDate);
            }

            rows.Add(new LoadFileRow
            {
                DocId = d.DocId,
                FamilyId = familyId,
                ParentId = parentId,
                Custodian = d.Custodian,
                DocType = d.DocType,
                FileName = d.FileName,
                SentDate = sentDate,
                Hash = d.Sha256,
            });
        }

        return new DedupResult(rows, batch.Documents.Count - rows.Count);
    }

    // Groups documents by dedup key and maps every DocId to the
    // lexicographically smallest DocId in its group - a stable choice of
    // survivor that doesn't depend on the order documents appear in the batch.
    private static Dictionary<string, string> Canonicalize(IEnumerable<Document> docs, Func<Document, string> key)
    {
        var survivor = new Dictionary<string, string>();
        foreach (var group in docs.GroupBy(key))
        {
            var winner = group.Select(d => d.DocId).OrderBy(id => id, StringComparer.Ordinal).First();
            foreach (var d in group)
            {
                survivor[d.DocId] = winner;
            }
        }
        return survivor;
    }

    // Spec 3.3: same participants, subject, instant and body identify the
    // same logical email regardless of which mailbox it was collected from.
    // From/To are compared case-insensitively (email addresses) and the body
    // is compared with line endings normalized, since those are exactly the
    // things that vary between a sender's Sent Items copy and a recipient's
    // Inbox copy.
    private static string EmailContentKey(Document d)
    {
        var from = (d.From ?? "").Trim().ToLowerInvariant();
        var to = string.Join(";", (d.To ?? new List<string>())
            .Select(t => t.Trim().ToLowerInvariant())
            .OrderBy(t => t, StringComparer.Ordinal));
        var subject = (d.Subject ?? "").Trim();
        var sentDate = FormatSentDateUtc(d.SentDate);
        var body = (d.Body ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();

        return string.Join("\u0001", from, to, subject, sentDate, body);
    }

    // Spec 5: SentDate must be UTC, formatted exactly YYYY-MM-DDTHH:MM:SSZ -
    // no fractional seconds, no offset. Input is ISO 8601 with offset.
    private static string FormatSentDateUtc(string? rawSentDate)
    {
        if (string.IsNullOrEmpty(rawSentDate))
        {
            return "";
        }

        var parsed = DateTimeOffset.Parse(rawSentDate, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
