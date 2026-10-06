# Senior Engineer Technical Exercise

Thanks for taking the time to do this. It should take **45 to 60 minutes**. Please stop at 60 minutes even if you're not finished; we've deliberately included more than fits in the time, and how you prioritise is part of what we're looking at.

## The situation

You've just joined the ingest platform team. The team owns a small tool that deduplicates a collection batch and writes a load file for the review platform. The functional spec is in `SPEC.md`. The tool is in `src/`.

This ticket landed in your queue this morning:

> **INGEST-4471 — Emails arriving in review without their attachments**
> Reviewers on the Halvorsen matter report emails that show as having attachments in the original mailbox but arrive in review with none. Started after the batch went through dedup. Sample batch that reproduces it is attached (`data/batch-sample.json`). The output the tool currently produces for it is `data/current-tool-output.csv`.

The spec also describes cross-mailbox email deduplication (§3.3), which the tool has never implemented. The team would like both things done.

## What we're asking you to do

1. Diagnose and fix the missing-attachment problem.
2. Implement cross-mailbox email deduplication as described in the spec.
3. Make sure the load file matches the spec.
4. Add tests that cover what you changed.
5. Fill in `DECISIONS.md`. This matters as much as the code. Wherever the spec was unclear, contradictory, or silent, tell us what you decided and why. Tell us what you didn't get to and what you'd do next.
6. Include the transcript(s) of any AI assistant conversations you had while working.

You don't need to finish all of this. You do need to leave the codebase in a state a teammate could pick up.

## Working with AI

Use whatever AI tools you normally use. We assume you will, and we'll read the transcripts. We're interested in how you direct the tool, what you check, and what you push back on, not whether you used it.

## Language

The starter is C# on .NET 8. You're welcome to keep working in C#. If you'd rather work in another language, you may reimplement the tool, provided you preserve the command-line contract (`<tool> <batch.json> <loadfile.csv>`) and the load-file format exactly, and you explain the choice in `DECISIONS.md`. Either way, we'll run your tool on batches you haven't seen.

## Questions

If something in the spec is unclear, you can email your interviewer and we'll reply during business hours. If you'd rather not wait, make a call and write it down in `DECISIONS.md`. Both are fine. Silently guessing is the one thing we'd rather you didn't do.

## Running the starter

```bash
cd src/DedupTool
dotnet run -- ../../data/batch-sample.json out.csv

cd ../..
dotnet test
```

## What to send back

A zip or repository link containing your code, tests, `DECISIONS.md`, and AI transcripts.

## What happens next

A 25-minute call. You'll walk us through your decisions, we'll run your tool on some additional data together and talk about what happens, and we'll hand you a requirement change and see how you'd approach it.
