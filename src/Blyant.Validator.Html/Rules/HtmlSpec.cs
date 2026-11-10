using System.Collections.Frozen;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// What the HTML standard (https://html.spec.whatwg.org/) allows, as the rules need it: the elements and their
/// attributes, the global attributes, boolean and enumerated attributes, and the obsolete elements and attributes
/// (section 16, "Obsolete features"). SVG and MathML inside a page are not covered.
/// </summary>
public static class HtmlSpec
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static FrozenSet<string> Set(string words) =>
        words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(Ci);

    /// <summary>The attributes every HTML element can have (besides data-*, aria-* and event handlers on*).</summary>
    public static readonly FrozenSet<string> GlobalAttributes = Set(
        "accesskey autocapitalize autocorrect autofocus class contenteditable dir draggable enterkeyhint hidden id " +
        "inert inputmode is itemid itemprop itemref itemscope itemtype lang nonce popover slot spellcheck style " +
        "tabindex title translate writingsuggestions role " +
        // RDFa Lite, as the Nu Html Checker allows it (Open Graph: <meta property="og:title">)
        "vocab typeof property resource prefix");

    /// <summary>The elements of HTML and the attributes of each, besides the global ones.</summary>
    public static readonly FrozenDictionary<string, FrozenSet<string>> Elements = new Dictionary<string, string>(Ci)
    {
        ["html"] = "manifest version xmlns",
        ["head"] = "profile",
        ["title"] = "",
        ["base"] = "href target",
        ["link"] = "href crossorigin rel as media hreflang type referrerpolicy sizes imagesrcset imagesizes integrity " +
                   "blocking color disabled fetchpriority charset methods rev target urn",
        ["meta"] = "name http-equiv content charset media scheme",
        ["style"] = "media blocking type",
        ["body"] = "alink background bgcolor link text vlink marginbottom marginheight marginleft marginright " +
                   "margintop marginwidth bottommargin leftmargin rightmargin topmargin",
        ["article"] = "", ["section"] = "", ["nav"] = "", ["aside"] = "", ["hgroup"] = "", ["header"] = "",
        ["footer"] = "", ["address"] = "", ["main"] = "", ["search"] = "", ["figure"] = "", ["figcaption"] = "",
        ["h1"] = "align", ["h2"] = "align", ["h3"] = "align", ["h4"] = "align", ["h5"] = "align", ["h6"] = "align",
        ["p"] = "align",
        ["hr"] = "align color noshade size width",
        ["pre"] = "width",
        ["blockquote"] = "cite",
        ["ol"] = "reversed start type compact",
        ["ul"] = "compact type",
        ["menu"] = "compact",
        ["li"] = "value type",
        ["dl"] = "compact", ["dt"] = "", ["dd"] = "",
        ["div"] = "align",
        ["a"] = "href target download ping rel hreflang type referrerpolicy charset coords shape methods name rev urn",
        ["em"] = "", ["strong"] = "", ["small"] = "", ["s"] = "", ["cite"] = "", ["dfn"] = "", ["abbr"] = "",
        ["ruby"] = "", ["rt"] = "", ["rp"] = "", ["code"] = "", ["var"] = "", ["samp"] = "", ["kbd"] = "",
        ["sub"] = "", ["sup"] = "", ["i"] = "", ["b"] = "", ["u"] = "", ["mark"] = "", ["bdi"] = "", ["span"] = "",
        ["wbr"] = "",
        ["q"] = "cite",
        ["data"] = "value",
        ["time"] = "datetime",
        ["bdo"] = "",
        ["br"] = "clear",
        ["ins"] = "cite datetime", ["del"] = "cite datetime",
        ["picture"] = "",
        ["source"] = "type media src srcset sizes width height",
        ["img"] = "alt src srcset sizes crossorigin usemap ismap width height referrerpolicy decoding loading " +
                  "fetchpriority align border hspace longdesc lowsrc name vspace",
        ["iframe"] = "src srcdoc name sandbox allow allowfullscreen width height referrerpolicy loading align " +
                     "allowtransparency frameborder framespacing hspace longdesc marginheight marginwidth scrolling vspace",
        ["embed"] = "src type width height align hspace name vspace",
        ["object"] = "data type name form width height align archive border classid code codebase codetype declare " +
                     "hspace standby vspace typemustmatch usemap",
        ["video"] = "src crossorigin poster preload autoplay playsinline loop muted controls width height",
        ["audio"] = "src crossorigin preload autoplay loop muted controls",
        ["track"] = "kind src srclang label default",
        ["map"] = "name",
        ["area"] = "alt coords shape href target download ping rel referrerpolicy nohref type hreflang",
        ["table"] = "align bgcolor border bordercolor cellpadding cellspacing frame height rules summary width",
        ["caption"] = "align",
        ["colgroup"] = "span align char charoff valign width",
        ["col"] = "span align char charoff valign width",
        ["tbody"] = "align char charoff height valign background",
        ["thead"] = "align char charoff height valign background",
        ["tfoot"] = "align char charoff height valign background",
        ["tr"] = "align bgcolor char charoff height valign background",
        ["td"] = "colspan rowspan headers abbr align axis bgcolor char charoff height nowrap valign width scope background",
        ["th"] = "colspan rowspan headers scope abbr align axis bgcolor char charoff height nowrap valign width background",
        ["form"] = "accept-charset action autocomplete enctype method name novalidate target rel",
        ["label"] = "for",
        ["input"] = "accept alpha alt autocomplete checked colorspace dirname disabled form formaction formenctype " +
                    "formmethod formnovalidate formtarget height list max maxlength min minlength multiple name pattern " +
                    "placeholder popovertarget popovertargetaction readonly required size src step type value width " +
                    "align border hspace ismap usemap vspace",
        ["button"] = "command commandfor disabled form formaction formenctype formmethod formnovalidate formtarget " +
                     "name popovertarget popovertargetaction type value",
        ["select"] = "autocomplete disabled form multiple name required size",
        ["datalist"] = "",
        ["optgroup"] = "disabled label",
        ["option"] = "disabled label selected value name",
        ["selectedcontent"] = "",
        ["textarea"] = "autocomplete cols dirname disabled form maxlength minlength name placeholder readonly " +
                       "required rows wrap",
        ["output"] = "for form name",
        ["progress"] = "value max",
        ["meter"] = "value min max low high optimum",
        ["fieldset"] = "disabled form name",
        ["legend"] = "align",
        ["details"] = "name open",
        ["summary"] = "",
        ["dialog"] = "open closedby",
        ["script"] = "src type nomodule async defer crossorigin integrity referrerpolicy blocking fetchpriority " +
                     "charset event for language",
        ["noscript"] = "",
        ["template"] = "shadowrootmode shadowrootdelegatesfocus shadowrootclonable shadowrootserializable",
        ["slot"] = "name",
        ["canvas"] = "width height",
    }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>Obsolete elements, and what to use instead (as the Nu Html Checker tells it).</summary>
    public static readonly FrozenDictionary<string, string> ObsoleteElements = new Dictionary<string, string>(Ci)
    {
        ["acronym"] = "Use the “abbr” element instead.",
        ["applet"] = "Use the “embed” or “object” element instead.",
        ["basefont"] = "Use CSS instead.",
        ["bgsound"] = "Use the “audio” element instead.",
        ["big"] = "Use CSS instead.",
        ["blink"] = "Use CSS instead.",
        ["center"] = "Use CSS instead.",
        ["dir"] = "Use the “ul” element instead.",
        ["font"] = "Use CSS instead.",
        ["frame"] = "Use the “iframe” element and CSS instead.",
        ["frameset"] = "Use the “iframe” element and CSS instead.",
        ["isindex"] = "Use an explicit “form” and text field instead.",
        ["keygen"] = "Use a server-side or JavaScript solution instead.",
        ["listing"] = "Use “pre” and “code” instead.",
        ["marquee"] = "Use CSS or JavaScript instead.",
        ["menuitem"] = "Use other elements and script instead.",
        ["multicol"] = "Use CSS instead.",
        ["nextid"] = "Use GUIDs instead.",
        ["nobr"] = "Use CSS instead.",
        ["noembed"] = "Use the “object” element instead of “embed” when fallback is necessary.",
        ["noframes"] = "Use the “iframe” element and CSS instead.",
        ["param"] = "Use the “data” attribute of the “object” element instead.",
        ["plaintext"] = "Use the “text/plain” MIME type instead.",
        ["rb"] = "Put the ruby base directly inside the “ruby” element instead.",
        ["rtc"] = "Use nested “ruby” elements instead.",
        ["spacer"] = "Use CSS instead.",
        ["strike"] = "Use “del” if it marks an edit, otherwise “s”.",
        ["tt"] = "Use CSS instead.",
        ["xmp"] = "Use “pre” and “code” instead.",
    }.ToFrozenDictionary(Ci);

    /// <summary>
    /// Obsolete attributes: "element attribute" (or "* attribute" for any element), and what to use instead
    /// </summary>
    public static readonly FrozenDictionary<string, string> ObsoleteAttributes = BuildObsoleteAttributes();

    private static FrozenDictionary<string, string> BuildObsoleteAttributes()
    {
        const string css = "Use CSS instead.";
        var obsolete = new Dictionary<string, string>(Ci);

        void Add(string elements, string attributes, string advice)
        {
            foreach (var element in elements.Split(' '))
                foreach (var attribute in attributes.Split(' '))
                    obsolete[element + " " + attribute] = advice;
        }

        // Presentation, which belongs in CSS
        Add("caption col colgroup div embed h1 h2 h3 h4 h5 h6 hr iframe img input legend object p table tbody td tfoot th thead tr",
            "align", css);
        Add("body table td th tr", "bgcolor", css);
        Add("body", "alink background link text vlink marginbottom marginheight marginleft marginright margintop " +
                    "marginwidth bottommargin leftmargin rightmargin topmargin", css);
        Add("tbody thead tfoot tr td th table", "background", css);
        Add("col colgroup tbody td tfoot th thead tr", "char charoff valign", css);
        Add("col colgroup hr pre table td th", "width", css);
        Add("table td th tr tbody thead tfoot", "height", css);
        Add("table", "bordercolor cellpadding cellspacing frame rules", css);
        Add("td th", "nowrap", css);
        Add("dl menu ol ul", "compact", css);
        Add("hr", "color noshade size", css);
        Add("br", "clear", css);
        Add("embed iframe img input object", "hspace vspace", css);
        // (border="0" on img and border="1" on table are obsolete but conforming: ObsoleteRule warns about those)
        Add("img input object table", "border", css);
        Add("li ul", "type", css);
        Add("iframe", "allowtransparency frameborder framespacing marginheight marginwidth scrolling", css);
        // Other obsolete attributes
        Add("a", "name", "Consider putting an “id” attribute on the nearest container instead.");
        Add("a link script", "charset", "Use UTF-8, and an HTTP Content-Type header on the linked resource if needed.");
        Add("a", "coords shape", "Use the “area” element instead of “a” for image maps.");
        Add("a link", "methods", "Use the HTTP OPTIONS feature instead.");
        Add("a link", "rev", "Use the “rel” attribute instead, with an opposite term.");
        Add("a link", "urn", "Specify the preferred persistent identifier using the “href” attribute instead.");
        Add("link", "target", "Omit it.");
        Add("area", "nohref", "Omitting the “href” attribute is sufficient.");
        Add("area", "type hreflang", "Omit it.");
        Add("embed img option", "name", "Use the “id” attribute instead.");
        Add("img iframe", "longdesc", "Use a regular “a” element to link to the description.");
        Add("img", "lowsrc", "Use a progressive JPEG image instead.");
        Add("input", "ismap usemap", "Use the “img” element instead of “input” for image maps.");
        Add("head", "profile", "Omit it.");
        Add("html", "version", "Omit it.");
        Add("html", "manifest", "Use service workers instead.");
        Add("meta", "scheme", "Use only one scheme per field, or make the scheme part of the value.");
        Add("object", "archive classid code codebase codetype", "Use the “data” and “type” attributes instead.");
        Add("object", "declare", "Repeat the “object” element completely each time it is to be reused.");
        Add("object", "standby", "Optimize the linked resource so that it loads quickly.");
        Add("object", "typemustmatch", "Omit it.");
        Add("script", "language", "Omit it (JavaScript is the default).");
        Add("script", "event for", "Use DOM events instead.");
        Add("table", "summary", "Describe the table in a “caption” element, or next to it, instead.");
        Add("td", "abbr", "Use text that begins in an unambiguous and terse manner, or a “title” attribute.");
        Add("td", "scope", "Use a “th” element instead.");
        Add("td th", "axis", "Use the “scope” attribute on the relevant “th” element instead.");
        Add("*", "datasrc datafld dataformatas datapagesize", "Use script and a mechanism such as XMLHttpRequest instead.");
        return obsolete.ToFrozenDictionary(Ci);
    }

    /// <summary>Boolean attributes: present or not; their value must be empty or their own name.</summary>
    public static readonly FrozenSet<string> BooleanAttributes = Set(
        "allowfullscreen alpha async autofocus autoplay checked controls default defer disabled formnovalidate inert " +
        "ismap itemscope loop multiple muted nomodule novalidate open playsinline readonly required reversed selected " +
        "shadowrootdelegatesfocus shadowrootclonable shadowrootserializable noshade nowrap compact declare nohref " +
        "typemustmatch");

    /// <summary>
    /// Attributes with a set of values: "element attribute" (or "* attribute"). Compared without regard to case,
    /// except where noted
    /// </summary>
    public static readonly FrozenDictionary<string, FrozenSet<string>> EnumeratedAttributes =
        new Dictionary<string, string>(Ci)
        {
            ["* dir"] = "ltr rtl auto",
            ["* draggable"] = "true false",
            ["* spellcheck"] = "true false",
            ["* translate"] = "yes no",
            ["* contenteditable"] = "true false plaintext-only",
            ["* hidden"] = "hidden until-found",
            ["* popover"] = "auto manual hint",
            ["* autocapitalize"] = "off none on sentences words characters",
            ["* autocorrect"] = "on off",
            ["* enterkeyhint"] = "enter done go next previous search send",
            ["* inputmode"] = "none text decimal numeric tel search email url",
            ["* writingsuggestions"] = "true false",
            ["input type"] = "hidden text search tel url email password date month week time datetime-local number " +
                             "range color checkbox radio file submit image reset button",
            ["button type"] = "submit reset button",
            ["form method"] = "get post dialog",
            ["form enctype"] = "application/x-www-form-urlencoded multipart/form-data text/plain",
            ["form autocomplete"] = "on off",
            ["img loading"] = "lazy eager",
            ["iframe loading"] = "lazy eager",
            ["img decoding"] = "sync async auto",
            ["* fetchpriority"] = "high low auto",
            ["* crossorigin"] = "anonymous use-credentials",
            ["* referrerpolicy"] = "no-referrer no-referrer-when-downgrade same-origin origin strict-origin " +
                                   "origin-when-cross-origin strict-origin-when-cross-origin unsafe-url",
            ["th scope"] = "row col rowgroup colgroup",
            ["track kind"] = "subtitles captions descriptions chapters metadata",
            ["video preload"] = "none metadata auto",
            ["audio preload"] = "none metadata auto",
            ["textarea wrap"] = "soft hard",
            ["area shape"] = "default rect circle poly",
            ["template shadowrootmode"] = "open closed",
            ["meta http-equiv"] = "content-language content-type default-style refresh set-cookie x-ua-compatible " +
                                  "content-security-policy origin-trial",
            ["dialog closedby"] = "any closerequest none",
            ["button popovertargetaction"] = "toggle show hide",
            ["input popovertargetaction"] = "toggle show hide",
        }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>Enumerated attributes that may also be empty (their empty value stands for one of the values).</summary>
    public static readonly FrozenSet<string> EmptyAllowed = Set(
        "spellcheck translate contenteditable hidden popover crossorigin referrerpolicy autocorrect writingsuggestions " +
        "preload");

    /// <summary>Attributes that hold a URL, which must not contain spaces or backslashes.</summary>
    public static readonly FrozenSet<string> UrlAttributes = Set(
        "a.href area.href link.href base.href img.src script.src iframe.src source.src audio.src video.src embed.src " +
        "track.src input.src form.action blockquote.cite q.cite ins.cite del.cite video.poster object.data " +
        "button.formaction input.formaction");
}
