using PixelBackup.App.ViewModels;
using PixelBackup.Core.Model;
using Xunit;

namespace PixelBackup.Tests;

/// <summary>
/// Die zwei Fortschrittsbalken: oben die laufende Gruppe, unten der ganze Lauf.
/// </summary>
public class ProgressTests
{
    [Fact]
    public void TotalAndGroupAreCountedSeparately()
    {
        var progress = new OperationProgress
        {
            ItemsDone = 30,
            ItemsTotal = 100,
            BytesDone = 250,
            BytesTotal = 1000,
            CurrentBytesDone = 50,
            CurrentBytesTotal = 200
        };

        Assert.Equal(25, progress.Percent);        // ganzer Lauf
        Assert.Equal(25, progress.CurrentPercent); // laufende Gruppe
        Assert.True(progress.HasCurrentProgress);

        var far = new OperationProgress { BytesDone = 10, BytesTotal = 1000, CurrentBytesDone = 9, CurrentBytesTotal = 10 };
        Assert.Equal(1, far.Percent);
        Assert.Equal(90, far.CurrentPercent);
    }

    [Fact]
    public void WithoutGroupSizeThereIsNoInventedNumber()
    {
        var progress = new OperationProgress { BytesDone = 5, BytesTotal = 10 };

        Assert.False(progress.HasCurrentProgress);
        Assert.Equal(0, progress.CurrentPercent);
        Assert.Equal("\u2013", progress.CurrentBytesText);
    }

    [Fact]
    public void PercentagesStayBetweenZeroAndHundred()
    {
        var overshoot = new OperationProgress
        {
            BytesDone = 2000,
            BytesTotal = 1000,
            CurrentBytesDone = 300,
            CurrentBytesTotal = 200
        };

        Assert.Equal(100, overshoot.Percent);
        Assert.Equal(100, overshoot.CurrentPercent);
    }

    [Fact]
    public void TheViewModelSplitsBothBars()
    {
        var model = new OperationProgressViewModel();

        model.Update(new OperationProgress
        {
            Phase = "Fotos",
            CurrentItem = "IMG_0001.jpg",
            ItemsDone = 5,
            ItemsTotal = 50,
            BytesDone = 100,
            BytesTotal = 1000,
            CurrentBytesDone = 40,
            CurrentBytesTotal = 80
        });

        Assert.Equal(10, model.Percent);
        Assert.Equal(50, model.CurrentPercent);
        Assert.False(model.CurrentIsIndeterminate);
        Assert.Equal("50 %", model.CurrentPercentText);
        Assert.Equal("10 %", model.PercentText);
        Assert.Equal("Fotos", model.Phase);
    }

    [Fact]
    public void WithoutGroupSizeTheUpperBarRuns()
    {
        var model = new OperationProgressViewModel();

        model.Update(new OperationProgress
        {
            Phase = "Sicherung wird vorbereitet",
            ItemsTotal = 40,
            BytesTotal = 800
        });

        Assert.True(model.CurrentIsIndeterminate);
        Assert.Equal("\u2026", model.CurrentPercentText);
        Assert.False(model.IsIndeterminate);   // der Gesamtbalken kennt seine Zahl
        Assert.Equal("0 %", model.PercentText);
    }

    [Fact]
    public void ResetClearsBothBars()
    {
        var model = new OperationProgressViewModel();

        model.Update(new OperationProgress { BytesDone = 5, BytesTotal = 10, CurrentBytesDone = 1, CurrentBytesTotal = 2 });
        model.Reset("Start");

        Assert.Equal(0, model.Percent);
        Assert.Equal(0, model.CurrentPercent);
        Assert.True(model.CurrentIsIndeterminate);
        Assert.Equal("Start", model.Phase);
    }
}
