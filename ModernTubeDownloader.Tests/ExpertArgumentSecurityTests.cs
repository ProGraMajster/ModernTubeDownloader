using ModernTubeDownloader.Utilities;

namespace ModernTubeDownloader.Tests;

[Trait("Category", "CookieSecurity")]
public sealed class ExpertArgumentSecurityTests
{
    public static IEnumerable<object[]> Cases()
    {
        var owned = new Dictionary<string, string>
        {
            ["--cookies"] = "--cookies", ["--cookies-from-browser"] = "--cookies-from-browser", ["--username"] = "--username",
            ["--password"] = "--password", ["--twofactor"] = "--twofactor", ["--video-password"] = "--video-password",
            ["--netrc"] = "--netrc", ["--netrc-location"] = "--netrc-location", ["--netrc-cmd"] = "--netrc-cmd",
            ["--client-certificate"] = "--client-certificate", ["--client-certificate-key"] = "--client-certificate-key",
            ["--client-certificate-password"] = "--client-certificate-password", ["--ap-username"] = "--ap-username", ["--ap-password"] = "--ap-password",
            ["--alias"] = "--alias", ["--preset-alias"] = "--preset-alias", ["--config-locations"] = "--config-locations",
            ["--plugin-dirs"] = "--plugin-dirs", ["--remote-components"] = "--remote-components", ["--exec"] = "--exec",
            ["--exec-before-download"] = "--exec-before-download", ["--downloader"] = "--downloader", ["--external-downloader"] = "--downloader",
            ["--downloader-args"] = "--downloader-args", ["--external-downloader-args"] = "--downloader-args",
            ["--postprocessor-args"] = "--postprocessor-args", ["--ppa"] = "--postprocessor-args", ["--use-postprocessor"] = "--use-postprocessor",
            ["--print-traffic"] = "--print-traffic", ["--dump-pages"] = "--dump-pages", ["--write-pages"] = "--write-pages", ["--verbose"] = "--verbose",
            ["--write-info-json"] = "--write-info-json", ["--embed-info-json"] = "--embed-info-json", ["--no-clean-infojson"] = "--no-clean-info-json"
        };
        foreach (var (option, canonical) in owned)
        {
            yield return [option + " 'synthetic fixture'", "managed:" + canonical];
            yield return [option + "='synthetic fixture'", "managed:" + canonical];
        }
        foreach (var (input, reason) in new (string, string)[]
        {
            ("--cookies-fr chrome", "managed:--cookies-from-browser"), ("--userna=test", "managed:--username"),
            ("--netrc-c echo", "managed:--netrc-cmd"), ("--config-l=x", "managed:--config-locations"),
            ("-psynthetic", "managed:--password"), ("-u synthetic", "managed:--username"), ("-2synthetic", "managed:--twofactor"),
            ("-qn", "managed:--netrc"), ("-qvpfixture", "managed:--verbose"), ("-qpfake", "managed:--password"),
            ("-tmp4", "managed:--preset-alias"), ("-Pfolder", "managed:--paths"), ("-ofile", "managed:--output"),
            ("--alias a '--alias b --cookies' --a", "managed:--alias"),
            ("--retries 3 --cookies x --cookies y", "managed:--cookies"),
            ("--add-header 'Cookie: fixture'", "credential-header"), ("--add-h='authorization: fixture'", "credential-header"),
            ("--add-headers '  Proxy-Authorization : fixture'", "credential-header"),
            ("--PASSWORD fixture", "unknown-option"), ("--Cookies fixture", "unknown-option"),
            ("--net fixture", "ambiguous-option"), ("-- --cookies fixture", "end-of-options"),
            ("https://example.test/another", "positional-argument"), ("--retries", "missing-value"),
            ("--quiet=true", "unexpected-value"), ("--unknown-future-option x", "unknown-option")
        }) yield return [input, reason];
        foreach (var input in new[] { "--retries 3", "--retries=3 --retries 4", "--user-agent 'Modern Tube Downloader'",
            "--add-header='X-Example: safe'", "-q --socket-timeout 10", "--limit-rate 1M", "--user-agent '--password'",
            "--referer=https://example.test/", "--retry-sleep 2", "--no-check-certificates", "-r1M", "--user-ag=test", "--proxy ''" })
            yield return [input, "allow"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParserSpellingsAreResolvedBeforePolicyAndValuesAreNeverEchoed(string input, string reason)
    {
        if (reason == "allow") Assert.NotEmpty(CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(input));
        else
        {
            var error = Assert.Throws<CustomArgumentException>(() => CommandLineArgumentTokenizer.ParseSafeYtDlpArguments(input));
            Assert.Equal(reason, error.Reason);
            Assert.DoesNotContain("synthetic", error.ToString());
            Assert.DoesNotContain("fixture", error.ToString());
        }
    }
}
