using Coldstart.Domain;
using FluentAssertions;
using Xunit;

namespace Coldstart.Domain.Tests;

public class LeadNoteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    private static Lead NewLead() =>
        Lead.Create(CustomerId.New(), "Acme Co", Fit.Yes, "Subj", "Body", T0);

    [Fact]
    public void AddNote_AppendsNote_AndReturnsId()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var id = lead.AddNote("called them", T0);

        // Assert
        lead.Notes.Should().ContainSingle();
        lead.Notes[0].Id.Should().Be(id);
        lead.Notes[0].Text.Should().Be("called them");
        lead.Notes[0].CreatedAt.Should().Be(T0);
        lead.Notes[0].UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void AddNote_WithBlankText_Throws()
    {
        // Arrange
        var lead = NewLead();

        // Act
        var act = () => lead.AddNote("   ", T0);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void UpdateNote_ChangesTextAndStampsUpdatedAt()
    {
        // Arrange
        var lead = NewLead();
        var id = lead.AddNote("first", T0);

        // Act
        lead.UpdateNote(id, "revised", T1);

        // Assert
        lead.Notes[0].Text.Should().Be("revised");
        lead.Notes[0].UpdatedAt.Should().Be(T1);
    }

    [Fact]
    public void UpdateNote_WithUnknownId_IsNoOp()
    {
        // Arrange
        var lead = NewLead();
        lead.AddNote("keep", T0);

        // Act
        var act = () => lead.UpdateNote(Guid.NewGuid(), "x", T1);

        // Assert
        act.Should().NotThrow();
        lead.Notes.Should().ContainSingle().Which.Text.Should().Be("keep");
    }

    [Fact]
    public void RemoveNote_RemovesById_UnknownIdIsNoOp()
    {
        // Arrange
        var lead = NewLead();
        var id = lead.AddNote("bye", T0);

        // Act
        lead.RemoveNote(Guid.NewGuid());   // no-op
        lead.RemoveNote(id);               // removes

        // Assert
        lead.Notes.Should().BeEmpty();
    }

    [Fact]
    public void AddNote_IsAllowedOnASentLead()
    {
        // Arrange — get the lead to Sent
        var lead = NewLead();
        lead.Approve(new EmailAddress("x@y.com"));
        lead.MarkSent(T0);

        // Act
        var act = () => lead.AddNote("post-send follow-up", T1);

        // Assert
        act.Should().NotThrow();
        lead.Notes.Should().ContainSingle();
    }
}
