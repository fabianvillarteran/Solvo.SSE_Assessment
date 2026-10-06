using System.Text;

namespace DedupTool;

/// <summary>
/// Writes the load file consumed by the review platform ingest job.
/// Column order is fixed. Do not change without coordinating with the ingest team.
/// </summary>
public static class LoadFileWriter
{
    public const string Header = "DocID,FamilyID,ParentID,Custodian,DocType,FileName,SentDate,Hash";

    public static string ToCsv(IEnumerable<LoadFileRow> rows)
    {
        // Ingest job expects LF line endings regardless of platform.
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var r in rows)
        {
            sb.Append(string.Join(",",
                r.DocId, r.FamilyId, r.ParentId, r.Custodian,
                r.DocType, r.FileName, r.SentDate, r.Hash)).Append('\n');
        }
        return sb.ToString();
    }

    public static void Write(string path, IEnumerable<LoadFileRow> rows)
    {
        File.WriteAllText(path, ToCsv(rows));
    }
}
