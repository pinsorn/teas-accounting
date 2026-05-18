using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using FluentAssertions;

namespace Accounting.Domain.Tests;

public class JournalEntryTests
{
    private static JournalEntry NewBalanced()
    {
        var j = new JournalEntry
        {
            PrefixCode = "JV",
            Description = "Test",
            TotalDebit = 100m,
            TotalCredit = 100m,
        };
        j.Lines.Add(new JournalLine { LineNo = 1, AccountId = 1, DebitAmount = 100m });
        j.Lines.Add(new JournalLine { LineNo = 2, AccountId = 2, CreditAmount = 100m });
        return j;
    }

    [Fact]
    public void Post_sets_status_and_metadata()
    {
        var j = NewBalanced();
        var now = DateTimeOffset.UtcNow;
        j.MarkPosted("05-2026-JV-0001", userId: 42, now);

        j.Status.Should().Be(DocumentStatus.Posted);
        j.DocNo.Should().Be("05-2026-JV-0001");
        j.PostedAt.Should().Be(now);
        j.PostedBy.Should().Be(42);
    }

    [Fact]
    public void Post_fails_if_unbalanced()
    {
        var j = NewBalanced();
        j.TotalDebit = 100m; j.TotalCredit = 90m;
        var act = () => j.MarkPosted("05-2026-JV-0001", 1, DateTimeOffset.UtcNow);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("je.unbalanced");
    }

    [Fact]
    public void Post_fails_if_already_posted()
    {
        var j = NewBalanced();
        j.MarkPosted("05-2026-JV-0001", 1, DateTimeOffset.UtcNow);
        var act = () => j.MarkPosted("05-2026-JV-0002", 1, DateTimeOffset.UtcNow);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("je.not_draft");
    }

    [Fact]
    public void Post_fails_with_empty_doc_no()
    {
        var j = NewBalanced();
        var act = () => j.MarkPosted("", 1, DateTimeOffset.UtcNow);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("je.no_docno");
    }
}
