using Ganss.Xss;

namespace Photino.Okf_Todo.Services;

public sealed class HtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Sanitize(string bodyHtml)
    {
        return _sanitizer.Sanitize(bodyHtml);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "p", "br", "strong", "b", "em", "i", "u", "s", "blockquote", "pre", "code",
            "h1", "h2", "h3", "h4", "ul", "ol", "li", "a", "img", "table", "thead",
            "tbody", "tr", "th", "td", "colgroup", "col", "span", "div"
        })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
        {
            "href", "target", "rel", "src", "alt", "title", "width", "height",
            "colspan", "rowspan", "style"
        })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        sanitizer.AllowedCssProperties.Clear();
        foreach (var property in new[]
        {
            "text-align", "font-weight", "font-style", "text-decoration", "width", "height",
            "max-width", "border-collapse", "border", "padding", "vertical-align"
        })
        {
            sanitizer.AllowedCssProperties.Add(property);
        }

        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto", "app" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        return sanitizer;
    }
}
