using System.Text.RegularExpressions;

namespace ShopDelivery.CatalogScraper.Scraping;

public sealed class RobotsPolicy
{
    private readonly IReadOnlyList<RobotsRule> _rules;

    private RobotsPolicy(
        IReadOnlyList<RobotsRule> rules,
        IReadOnlyList<Uri> sitemaps,
        TimeSpan? crawlDelay)
    {
        _rules = rules;
        Sitemaps = sitemaps;
        CrawlDelay = crawlDelay;
    }

    public IReadOnlyList<Uri> Sitemaps { get; }
    public TimeSpan? CrawlDelay { get; }

    public static RobotsPolicy AllowAll { get; } = new([], [], null);
    public static RobotsPolicy DenyAll { get; } = new([new RobotsRule(false, "/")], [], null);

    public bool IsAllowed(Uri uri)
    {
        var path = uri.PathAndQuery;
        var matches = _rules
            .Where(rule => rule.IsMatch(path))
            .OrderByDescending(rule => rule.Specificity)
            .ThenByDescending(rule => rule.Allow)
            .ToList();

        return matches.Count == 0 || matches[0].Allow;
    }

    public static RobotsPolicy Parse(string content, Uri origin, string userAgent)
    {
        var groups = new List<RobotsGroup>();
        RobotsGroup? current = null;
        var sitemaps = new List<Uri>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0)
                continue;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var directive = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (directive.Equals("sitemap", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(origin, value, out var sitemap)
                    && (sitemap.Scheme == Uri.UriSchemeHttp || sitemap.Scheme == Uri.UriSchemeHttps))
                {
                    sitemaps.Add(sitemap);
                }

                continue;
            }

            if (directive.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null || current.HasDirectives)
                {
                    current = new RobotsGroup();
                    groups.Add(current);
                }

                if (value.Length > 0)
                    current.Agents.Add(value);
                continue;
            }

            if (current is null)
                continue;

            if (directive.Equals("allow", StringComparison.OrdinalIgnoreCase))
            {
                current.HasDirectives = true;
                if (value.Length > 0)
                    current.Rules.Add(new RobotsRule(true, value));
            }
            else if (directive.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                current.HasDirectives = true;
                if (value.Length > 0)
                    current.Rules.Add(new RobotsRule(false, value));
            }
            else if (directive.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase))
            {
                current.HasDirectives = true;
                if (decimal.TryParse(
                        value,
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seconds)
                    && seconds is >= 0 and <= 120)
                {
                    current.CrawlDelay = TimeSpan.FromMilliseconds((double)(seconds * 1_000));
                }
            }
        }

        var agentToken = userAgent.Split(['/', ' ', '('], StringSplitOptions.RemoveEmptyEntries)[0];
        var matches = groups
            .Select(group => new
            {
                Group = group,
                MatchLength = group.Agents
                    .Where(agent => agent == "*"
                                    || agentToken.Contains(agent, StringComparison.OrdinalIgnoreCase))
                    .Select(agent => agent == "*" ? 0 : agent.Length)
                    .DefaultIfEmpty(-1)
                    .Max(),
            })
            .Where(match => match.MatchLength >= 0)
            .ToList();

        if (matches.Count == 0)
            return new RobotsPolicy([], sitemaps, null);

        var bestLength = matches.Max(match => match.MatchLength);
        var selected = matches.Where(match => match.MatchLength == bestLength).ToList();
        return new RobotsPolicy(
            selected.SelectMany(match => match.Group.Rules).ToList(),
            sitemaps.DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToList(),
            selected.Select(match => match.Group.CrawlDelay).Where(delay => delay is not null).Max());
    }

    private sealed class RobotsGroup
    {
        public List<string> Agents { get; } = [];
        public List<RobotsRule> Rules { get; } = [];
        public bool HasDirectives { get; set; }
        public TimeSpan? CrawlDelay { get; set; }
    }

    private sealed class RobotsRule
    {
        private readonly Regex _pattern;

        public RobotsRule(bool allow, string pattern)
        {
            Allow = allow;
            Specificity = pattern.Count(character => character != '*');
            var endAnchored = pattern.EndsWith('$');
            var body = endAnchored ? pattern[..^1] : pattern;
            var regex = Regex.Escape(body).Replace("\\*", ".*");
            _pattern = new Regex(
                $"^{regex}{(endAnchored ? "$" : "")}",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        public bool Allow { get; }
        public int Specificity { get; }
        public bool IsMatch(string path) => _pattern.IsMatch(path);
    }
}
