using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using PixelBackup.Core.Util;
using Xunit;

namespace PixelBackup.Tests;

public class CategoryTests
{
    [Fact]
    public void Photos_MatchImagesInCameraFolder()
    {
        var photos = CategoryCatalog.ById("photos")!;
        Assert.True(photos.Matches("/sdcard/DCIM/Camera/IMG_1.jpg"));
        Assert.True(photos.Matches("/sdcard/Pictures/Screenshots/shot.png"));
        Assert.False(photos.Matches("/sdcard/DCIM/Camera/VID_1.mp4"));
        Assert.False(photos.Matches("/sdcard/Music/song.mp3"));
    }

    [Fact]
    public void Videos_AndPhotos_DoNotOverlap()
    {
        var photos = CategoryCatalog.ById("photos")!;
        var videos = CategoryCatalog.ById("videos")!;
        const string video = "/sdcard/DCIM/Camera/VID_1.mp4";

        Assert.True(videos.Matches(video));
        Assert.False(photos.Matches(video));
    }

    [Fact]
    public void Downloads_TakeEveryFileType()
    {
        var downloads = CategoryCatalog.ById("downloads")!;
        Assert.True(downloads.Matches("/sdcard/Download/handbuch.pdf"));
        Assert.True(downloads.Matches("/sdcard/Download/archiv.tar.gz"));
        Assert.False(downloads.Matches("/sdcard/Documents/handbuch.pdf"));
    }

    [Fact]
    public void NonFileCategories_NeverMatchPaths()
    {
        var apps = CategoryCatalog.ById("apps")!;
        Assert.Equal(BackupCategoryKind.Apps, apps.Kind);
        Assert.False(apps.Matches("/data/app/base.apk"));
    }

    [Fact]
    public void CatchAllCategory_IsListedLastAmongFileCategories()
    {
        var other = CategoryCatalog.ById("otherfiles")!;
        Assert.True(other.IsCatchAll);
        Assert.DoesNotContain(CategoryCatalog.FileCategories, c => c.IsCatchAll);
    }
}

public class BatchingTests
{
    private static PlannedItem FileItem(string path, long size = 100) => new()
    {
        CategoryId = "photos",
        Type = BackupEntryType.File,
        RemotePath = path,
        RelativePath = PathMapper.ToRelativeBackupPath(path),
        Size = size,
        DisplayName = PathMapper.RemoteFileName(path)
    };

    [Fact]
    public void BuildBatches_GroupsFilesOfTheSameFolder()
    {
        var items = new List<PlannedItem>
        {
            FileItem("/sdcard/DCIM/Camera/a.jpg"),
            FileItem("/sdcard/DCIM/Camera/b.jpg"),
            FileItem("/sdcard/Pictures/c.png")
        };

        var batches = BackupService.BuildBatches(items).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(2, batches[0].Count);
        Assert.Single(batches[1]);
    }

    [Fact]
    public void BuildBatches_RespectsTheMaximumBatchSize()
    {
        var items = Enumerable.Range(0, 95)
            .Select(i => FileItem($"/sdcard/DCIM/Camera/img{i}.jpg"))
            .ToList();

        var batches = BackupService.BuildBatches(items, maxBatchSize: 40).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(40, batches[0].Count);
        Assert.Equal(15, batches[2].Count);
        Assert.Equal(95, batches.Sum(b => b.Count));
    }

    [Fact]
    public void BuildBatches_KeepsSpecialItemsSeparate()
    {
        var items = new List<PlannedItem>
        {
            FileItem("/sdcard/DCIM/Camera/a.jpg"),
            new()
            {
                CategoryId = "apps",
                Type = BackupEntryType.Apk,
                RemotePath = "/data/app/com.example/base.apk",
                RelativePath = "apps/com.example/base.apk",
                Size = 10
            },
            FileItem("/sdcard/DCIM/Camera/b.jpg")
        };

        var batches = BackupService.BuildBatches(items).ToList();

        Assert.Equal(3, batches.Count);
        Assert.All(batches, b => Assert.Single(b));
    }

    [Fact]
    public void BuildBatches_PullsNamesNeedingSanitizingOnTheirOwn()
    {
        var items = new List<PlannedItem>
        {
            FileItem("/sdcard/Download/normal.pdf"),
            FileItem("/sdcard/Download/Rechnung:2024.pdf"),
            FileItem("/sdcard/Download/auch-normal.pdf")
        };

        var batches = BackupService.BuildBatches(items).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal("/sdcard/Download/Rechnung:2024.pdf", batches[1][0].RemotePath);
    }
}
