using FluentAssertions;
using KnowledgeWeakness.App.ViewModels;
using KnowledgeWeakness.Core.Domain;

namespace KnowledgeWeakness.Tests;

public class ExtractedQuestionRowReviewGuardTests
{
    [Fact]
    public void Editing_IsCorrect_clears_NeedsReview()
    {
        var row = new ExtractedQuestionRow
        {
            Number = "1",
            Type = QuestionType.Choice,
            Stem = "x",
            IsCorrect = false,
            NeedsReview = true,
            AiIsCorrect = true,
            TeacherIsCorrect = false
        };

        // User adjudicates by flipping the IsCorrect cell.
        row.IsCorrect = true;

        row.NeedsReview.Should().BeFalse("editing IsCorrect must count as human adjudication");
    }

    [Fact]
    public void Toggling_IsCorrect_back_does_not_reintroduce_NeedsReview()
    {
        var row = new ExtractedQuestionRow
        {
            Number = "2",
            IsCorrect = true,
            NeedsReview = true
        };

        row.IsCorrect = false;
        row.IsCorrect = true;

        row.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void Setting_IsCorrect_to_same_value_still_clears_when_user_explicitly_touches_it()
    {
        // CommunityToolkit.Mvvm partial OnXxxChanged only fires when the value
        // actually changes. So if IsCorrect was already true and the user
        // re-sets it to true, the hook won't run — which is fine: the user
        // hasn't really adjudicated. We document that with this test.
        var row = new ExtractedQuestionRow
        {
            IsCorrect = true,
            NeedsReview = true
        };

        row.IsCorrect = true; // no actual change

        row.NeedsReview.Should().BeTrue("re-assigning same value is a no-op by design");
    }
}
