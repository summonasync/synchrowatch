using Shouldly;
using Syncrowatch.App.Tray;
using Xunit;

namespace Syncrowatch.Tests;

public sealed class FilesWindowViewTests
{
    [Fact]
    public void Show_SecondCall_ReusesSameWindowInstance()
    {
        var created = new List<FakeFilesWindow>();
        var view = new FilesWindowView(CreateWindowSpy(created));

        view.Show();
        view.Show();

        created.ShouldHaveSingleItem();
        created[0].ShowCount.ShouldBe(2);
    }

    [Fact]
    public void Show_AfterWindowClosed_CreatesFreshWindowInstance()
    {
        var created = new List<FakeFilesWindow>();
        var view = new FilesWindowView(CreateWindowSpy(created));

        view.Show();
        created[0].RaiseClosed();

        view.Show();

        created.Count.ShouldBe(2);
        created[1].ShowCount.ShouldBe(1);
        created[0].ShowCount.ShouldBe(1);
    }

    [Fact]
    public void Show_AfterWindowClosed_OldWindowStopsReceivingShowCalls()
    {
        var created = new List<FakeFilesWindow>();
        var view = new FilesWindowView(CreateWindowSpy(created));

        view.Show();
        created[0].RaiseClosed();
        view.Show();
        created[0].RaiseClosed();
        view.Show();

        created.Count.ShouldBe(2);
        created[0].ShowCount.ShouldBe(1);
        created[1].ShowCount.ShouldBe(2);
    }

    private static Func<IFilesWindow> CreateWindowSpy(List<FakeFilesWindow> created) => () =>
    {
        var window = new FakeFilesWindow();
        created.Add(window);
        return window;
    };

    private sealed class FakeFilesWindow : IFilesWindow
    {
        public event EventHandler? Closed;

        public int ShowCount { get; private set; }

        public void Show() => ShowCount++;

        public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
