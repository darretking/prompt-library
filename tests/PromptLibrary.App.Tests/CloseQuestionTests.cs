using System.ComponentModel;
using System.Windows;
using PromptLibrary.App.Views;

namespace PromptLibrary.App.Tests;

/// <summary>
/// The final review's C1: while a window's close question was open, the tray, a second launch or the installer
/// showed or closed that window and WPF threw, because the question ran inside Closing.
/// </summary>
public class CloseQuestionTests
{
    /// <summary>A real, off-screen window whose Closing hands the question to <see cref="CloseQuestion"/>.</summary>
    private sealed class QuestionWindow : Window
    {
        public QuestionWindow()
        {
            Question = new CloseQuestion(this);
            Left = -10000;
            Top = -10000;
            Width = 100;
            Height = 100;
            ShowInTaskbar = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        public CloseQuestion Question { get; }
        public Func<bool> Ask { get; set; } = () => true;
        public bool ClosesForReal { get; set; }
        public int Asked { get; private set; }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ClosesForReal)
                Question.CancelAndAsk(e, () => { Asked++; return Ask(); }, Hide);
            base.OnClosing(e);
        }
    }

    [Fact]
    public void ShowInsideClosing_Throws_WhichIsWhyTheQuestionMovesOut()
        => Sta.Run(() =>
        {
            var window = new QuestionWindow { ClosesForReal = true };
            window.Show();
            InvalidOperationException? thrown = null;
            window.Closing += (_, _) => thrown = Record.Exception(window.Show) as InvalidOperationException;

            window.Close();

            Assert.NotNull(thrown);
        });

    [Fact]
    public void ShowAndCloseWhileTheQuestionIsOpen_DoNotThrow()
        => Sta.Run(() =>
        {
            var window = new QuestionWindow();
            Exception? shown = null, closed = null;
            bool? askingDuringQuestion = null;
            window.Ask = () =>
            {
                // What the question's message loop may run: tray Open / a second launch, and the installer's close.
                askingDuringQuestion = window.Question.IsAsking;
                shown = Record.Exception(window.Show);
                closed = Record.Exception(window.Close);
                return false; // Cancel: the window stays
            };
            window.Show();

            window.Close();
            Assert.True(window.Question.IsAsking); // pending until the dispatcher runs it
            Sta.DoEvents();

            Assert.Null(shown);
            Assert.Null(closed);
            Assert.True(askingDuringQuestion);
            Assert.Equal(1, window.Asked); // the close during the question didn't ask again
            Assert.False(window.Question.IsAsking);
            Assert.True(window.IsVisible);
            window.ClosesForReal = true;
            window.Close();
        });

    [Fact]
    public void Yes_RunsTheFollowUp_AfterTheQuestion()
        => Sta.Run(() =>
        {
            var window = new QuestionWindow { Ask = () => true };
            window.Show();

            window.Close();
            Assert.True(window.IsVisible); // the close itself was cancelled
            Sta.DoEvents();

            Assert.False(window.IsVisible); // hidden after the answer
            Assert.Equal(1, window.Asked);
            window.ClosesForReal = true;
            window.Close();
        });

    [Fact]
    public void TwoClosesBeforeTheQuestionRuns_AskOnce()
        => Sta.Run(() =>
        {
            var window = new QuestionWindow { Ask = () => false };
            window.Show();

            window.Close();
            window.Close();
            Sta.DoEvents();

            Assert.Equal(1, window.Asked);
            window.ClosesForReal = true;
            window.Close();
        });
}
