using DedupTool;
using Xunit;

namespace DedupTool.Tests;

public class DeduperTests
{
    private static Document Loose(string id, string custodian, string hash) => new()
    {
        DocId = id, Custodian = custodian, DocType = "loose", FileName = id + ".txt", Sha256 = hash, SizeBytes = 10,
    };

    private static Document Email(string id, string custodian, string hash, string sentDate) => new()
    {
        DocId = id, Custodian = custodian, DocType = "email", FileName = id + ".eml", Sha256 = hash, SizeBytes = 10,
        SentDate = sentDate,
    };

    private static Document Email(
        string id, string custodian, string hash, string sentDate,
        string from, string[] to, string subject, string body) => new()
    {
        DocId = id, Custodian = custodian, DocType = "email", FileName = id + ".eml", Sha256 = hash, SizeBytes = 10,
        SentDate = sentDate, From = from, To = to.ToList(), Subject = subject, Body = body,
    };

    private static Document Attachment(string id, string custodian, string hash, string parentId) => new()
    {
        DocId = id, Custodian = custodian, DocType = "attachment", ParentId = parentId, FileName = id + ".bin",
        Sha256 = hash, SizeBytes = 10,
    };

    [Fact]
    public void IdenticalLooseDocuments_SecondIsSuppressed()
    {
        var batch = new Batch
        {
            Documents = new() { Loose("A", "c1", "h1"), Loose("B", "c2", "h1") },
        };

        var result = Deduper.Run(batch);

        Assert.Single(result.Rows);
        Assert.Equal("A", result.Rows[0].DocId);
        Assert.Equal(1, result.Suppressed);
    }

    [Fact]
    public void DistinctDocuments_AllKept()
    {
        var batch = new Batch
        {
            Documents = new() { Loose("A", "c1", "h1"), Loose("B", "c1", "h2") },
        };

        var result = Deduper.Run(batch);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(0, result.Suppressed);
    }

    [Fact]
    public void LoadFile_StartsWithFixedHeader()
    {
        var csv = LoadFileWriter.ToCsv(Array.Empty<LoadFileRow>());

        Assert.StartsWith("DocID,FamilyID,ParentID,Custodian,DocType,FileName,SentDate,Hash", csv);
    }

    // INGEST-4471: an attachment with the same sha256 as an attachment on a
    // *different* email must not be suppressed. Families must come out intact
    // (spec 3.5) even when their attachment happens to be byte-identical to an
    // attachment belonging to a different family/custodian (this is exactly
    // the Q3-budget.xlsx / vendor-report.pdf situation in batch-sample.json).
    [Fact]
    public void CrossFamilyDuplicateAttachment_BothFamiliesKeepTheirCopy()
    {
        // Two distinct emails (different subject/body) that merely happen to
        // carry a byte-identical attachment - not a §3.3 cross-mailbox case.
        var email1 = Email("E1", "jsmith", "e1-hash", "2024-03-05T14:22:00-05:00",
            "jsmith@example.com", new[] { "mlee@example.com" }, "Q3 budget", "Here's the budget.");
        var attach1 = Attachment("A1", "jsmith", "dup-hash", "E1");
        var email2 = Email("E2", "mlee", "e2-hash", "2024-03-06T09:00:00Z",
            "mlee@example.com", new[] { "jsmith@example.com" }, "Vendor policy", "See attached policy.");
        var attach2 = Attachment("A2", "mlee", "dup-hash", "E2");

        var batch = new Batch { Documents = new() { email1, attach1, email2, attach2 } };

        var result = Deduper.Run(batch);

        Assert.Contains(result.Rows, r => r.DocId == "A1" && r.FamilyId == "E1");
        Assert.Contains(result.Rows, r => r.DocId == "A2" && r.FamilyId == "E2");
        Assert.Equal(0, result.Suppressed);
    }

    // A genuine duplicate attachment *within the same family* (same parent)
    // is still a duplicate per spec 3.2 and should still be suppressed - the
    // fix for INGEST-4471 narrows the dedup scope, it doesn't remove it.
    [Fact]
    public void SameFamilyDuplicateAttachment_IsStillSuppressed()
    {
        var email1 = Email("E1", "jsmith", "e1-hash", "2024-03-05T14:22:00-05:00");
        var attach1 = Attachment("A1", "jsmith", "dup-hash", "E1");
        var attach2 = Attachment("A2", "jsmith", "dup-hash", "E1");

        var batch = new Batch { Documents = new() { email1, attach1, attach2 } };

        var result = Deduper.Run(batch);

        Assert.Equal(1, result.Suppressed);
        Assert.DoesNotContain(result.Rows, r => r.DocId == "A2");
    }

    // Spec 5: SentDate must be UTC, formatted exactly YYYY-MM-DDTHH:MM:SSZ.
    // The tool currently echoes the raw header value (offset and all) instead
    // of converting it.
    [Fact]
    public void SentDate_IsConvertedToUtcZFormat()
    {
        var email = Email("E1", "jsmith", "h1", "2024-03-05T14:22:00-05:00");
        var batch = new Batch { Documents = new() { email } };

        var result = Deduper.Run(batch);

        Assert.Equal("2024-03-05T19:22:00Z", result.Rows[0].SentDate);
    }

    [Fact]
    public void SentDate_DropsFractionalSeconds()
    {
        var email = Email("E1", "jsmith", "h1", "2024-03-05T14:22:00.1234567-05:00");
        var batch = new Batch { Documents = new() { email } };

        var result = Deduper.Run(batch);

        Assert.Equal("2024-03-05T19:22:00Z", result.Rows[0].SentDate);
    }

    // Attachments carry their family's top-level email date (spec 5) - that
    // inherited date must also be the converted UTC value, not the raw header.
    [Fact]
    public void SentDate_AttachmentInheritsConvertedUtcDate()
    {
        var email = Email("E1", "jsmith", "e1-hash", "2024-03-05T14:22:00-05:00");
        var attach = Attachment("A1", "jsmith", "a1-hash", "E1");
        var batch = new Batch { Documents = new() { email, attach } };

        var result = Deduper.Run(batch);

        var attachRow = Assert.Single(result.Rows, r => r.DocId == "A1");
        Assert.Equal("2024-03-05T19:22:00Z", attachRow.SentDate);
    }

    // Spec 3.3: the same email collected from two mailboxes is a duplicate
    // even though raw bytes (and sha256) differ. Mirrors batch-sample.json:
    // same instant via different offsets, email address differs only by
    // case, body differs only by line-ending style.
    [Fact]
    public void CrossMailboxDuplicateEmail_SecondCopyIsSuppressed()
    {
        var e1 = Email("E1", "jsmith", "hash-sent-copy", "2024-03-05T14:22:00-05:00",
            "john.smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\n\nSee attached.\n");
        var e2 = Email("E2", "mlee", "hash-inbox-copy", "2024-03-05T19:22:00Z",
            "John.Smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\r\n\r\nSee attached.\r\n");

        var batch = new Batch { Documents = new() { e1, e2 } };

        var result = Deduper.Run(batch);

        Assert.Single(result.Rows);
        Assert.Equal(1, result.Suppressed);
    }

    // Selection of which copy survives must be deterministic regardless of
    // input order (spec 6.1) - not just "first one we see".
    [Fact]
    public void CrossMailboxDuplicateEmail_SurvivorIsIndependentOfInputOrder()
    {
        var e1 = Email("E1", "jsmith", "hash-sent-copy", "2024-03-05T14:22:00-05:00",
            "john.smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\n\nSee attached.\n");
        var e2 = Email("E2", "mlee", "hash-inbox-copy", "2024-03-05T19:22:00Z",
            "John.Smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\r\n\r\nSee attached.\r\n");

        var forward = Deduper.Run(new Batch { Documents = new() { e1, e2 } });
        var reversed = Deduper.Run(new Batch { Documents = new() { e2, e1 } });

        Assert.Equal(forward.Rows[0].DocId, reversed.Rows[0].DocId);
    }

    // Guard against an overly loose match: same subject/from/to/date but a
    // genuinely different body must NOT be merged.
    [Fact]
    public void EmailsWithDifferentBody_AreNotMergedAsDuplicates()
    {
        var e1 = Email("E1", "jsmith", "h1", "2024-03-05T14:22:00-05:00",
            "john.smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review", "Original message.");
        var e2 = Email("E2", "mlee", "h2", "2024-03-05T19:22:00Z",
            "john.smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review", "Completely different content.");

        var batch = new Batch { Documents = new() { e1, e2 } };

        var result = Deduper.Run(batch);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(0, result.Suppressed);
    }

    // When the suppressed mailbox copy's attachment is byte-identical to the
    // surviving copy's attachment (the common case - attachments usually
    // don't get mangled by the mail relay the way headers do), the orphaned
    // attachment must re-parent onto the surviving family rather than vanish
    // or point at a FamilyID that no longer has a row.
    [Fact]
    public void CrossMailboxDuplicateEmail_AttachmentsCollapseIntoSurvivingFamily()
    {
        var e1 = Email("E1", "jsmith", "hash-sent-copy", "2024-03-05T14:22:00-05:00",
            "john.smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\n\nSee attached.\n");
        var a1 = Attachment("A1", "jsmith", "attachment-hash", "E1");
        var e2 = Email("E2", "mlee", "hash-inbox-copy", "2024-03-05T19:22:00Z",
            "John.Smith@example.com", new[] { "maria.lee@example.com" }, "Q3 budget review",
            "Hi Maria,\r\n\r\nSee attached.\r\n");
        var a2 = Attachment("A2", "mlee", "attachment-hash", "E2");

        var batch = new Batch { Documents = new() { e1, a1, e2, a2 } };

        var result = Deduper.Run(batch);

        var survivingEmailId = Assert.Single(result.Rows, r => r.DocType == "email").DocId;
        var attachmentRow = Assert.Single(result.Rows, r => r.DocType == "attachment");
        Assert.Equal(survivingEmailId, attachmentRow.FamilyId);
        Assert.Equal(survivingEmailId, attachmentRow.ParentId);
        Assert.Equal(2, result.Suppressed); // E2's email row + one duplicate attachment
    }
}
