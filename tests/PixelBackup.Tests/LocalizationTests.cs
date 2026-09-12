using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using Xunit;

namespace PixelBackup.Tests;

[Collection("Sprache")]
public class LocalizationTests : IDisposable
{
    private readonly AppLanguage _original = Localizer.I.Lang;

    public void Dispose() => Localizer.I.Lang = _original;

    [Fact]
    public void Tr_ReturnsTheTextOfTheSelectedLanguage()
    {
        Localizer.I.Lang = AppLanguage.De;
        Assert.Equal("Hallo", Loc.Tr("Hallo", "Hello"));

        Localizer.I.Lang = AppLanguage.En;
        Assert.Equal("Hello", Loc.Tr("Hallo", "Hello"));
    }

    [Fact]
    public void LanguageChanged_IsRaisedOnlyOnRealChanges()
    {
        Localizer.I.Lang = AppLanguage.De;

        var raised = 0;
        void Handler() => raised++;

        Localizer.I.LanguageChanged += Handler;
        try
        {
            Localizer.I.Lang = AppLanguage.De; // unverändert
            Assert.Equal(0, raised);

            Localizer.I.Lang = AppLanguage.En;
            Assert.Equal(1, raised);
        }
        finally
        {
            Localizer.I.LanguageChanged -= Handler;
        }
    }

    [Fact]
    public void Categories_FollowTheSelectedLanguage()
    {
        var photos = CategoryCatalog.ById("photos")!;

        Localizer.I.Lang = AppLanguage.De;
        Assert.Equal("Fotos", photos.DisplayName);
        Assert.Contains("Kamerabilder", photos.Description);

        Localizer.I.Lang = AppLanguage.En;
        Assert.Equal("Photos", photos.DisplayName);
        Assert.Contains("Camera shots", photos.Description);
    }

    [Fact]
    public void EveryCategory_HasBothLanguages()
    {
        foreach (var category in CategoryCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(category.NameDe), category.Id);
            Assert.False(string.IsNullOrWhiteSpace(category.NameEn), category.Id);
            Assert.False(string.IsNullOrWhiteSpace(category.DescriptionDe), category.Id);
            Assert.False(string.IsNullOrWhiteSpace(category.DescriptionEn), category.Id);

            if (category.CaveatDe is not null)
            {
                Assert.False(string.IsNullOrWhiteSpace(category.CaveatEn), category.Id);
            }
        }
    }

    [Fact]
    public void DeviceTexts_FollowTheSelectedLanguage()
    {
        var device = new DeviceInfo { Model = "Pixel 8", Manufacturer = "Google", SdkLevel = "37", AndroidVersion = "17" };

        Localizer.I.Lang = AppLanguage.De;
        Assert.Contains("nein", device.RootText);

        Localizer.I.Lang = AppLanguage.En;
        Assert.Contains("no ", device.RootText);
        Assert.Equal("Android 17 (API 37)", device.AndroidText);
        Assert.Equal(37, device.SdkNumber);
    }

    [Fact]
    public void DetectFromSystem_MapsGermanToGermanAndEverythingElseToEnglish()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("de-AT");
            Assert.Equal(AppLanguage.De, Localizer.DetectFromSystem());

            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal(AppLanguage.En, Localizer.DetectFromSystem());

            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(AppLanguage.En, Localizer.DetectFromSystem());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }
}
