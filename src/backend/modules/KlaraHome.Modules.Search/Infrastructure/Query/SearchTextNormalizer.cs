using System.Globalization;
using System.Text;

namespace KlaraHome.Modules.Search.Infrastructure.Query;

/// <summary>One term of a parsed query, with everything the store says it also means.</summary>
/// <param name="Term">The word as the shopper typed it, folded to lower case.</param>
/// <param name="Alternatives">The synonyms that apply, folded to lower case. May be empty.</param>
internal sealed record SearchTerm(string Term, IReadOnlyList<string> Alternatives);

/// <summary>
/// A query after the store's own vocabulary has been applied to it.
/// </summary>
/// <param name="Raw">What the shopper typed, trimmed and truncated.</param>
/// <param name="Normalised">The surviving terms, space-joined. What the query log groups on.</param>
/// <param name="Terms">The surviving terms with their synonyms.</param>
/// <param name="TsQuery">
/// The <c>tsquery</c> expression, or null when nothing survived normalisation — a query of nothing
/// but stop words is a query with no text condition, not a query that matches nothing.
/// </param>
internal sealed record NormalizedQuery(
    string Raw,
    string Normalised,
    IReadOnlyList<SearchTerm> Terms,
    string? TsQuery)
{
    /// <summary>Whether there is any text to match on at all.</summary>
    public bool HasText => TsQuery is { Length: > 0 };

    /// <summary>An empty query: a browse page, with filters and no words.</summary>
    public static NormalizedQuery Empty { get; } = new(string.Empty, string.Empty, [], null);
}

/// <summary>
/// Turns what a shopper typed into something PostgreSQL can match on.
/// </summary>
/// <remarks>
/// <para>
/// A pure function over the text and the store's vocabulary, deliberately: parsing a query must not
/// touch a database — the synonyms and stop words are handed in already loaded and cached — and it
/// must be testable without one. It is also the one piece of this module that is worth unit testing
/// while it is being written, because every failure mode is a string that is one character wrong.
/// </para>
/// <para>
/// Punctuation is stripped rather than escaped. <c>tsquery</c> has its own operator grammar —
/// <c>&amp;</c>, <c>|</c>, <c>!</c>, <c>&lt;-&gt;</c> and parentheses — and a shopper who types an
/// apostrophe is not asking for any of it. Removing everything that is not a letter, a digit or a
/// separator means the expression this builds can only ever be the expression it intended, whatever
/// arrives in the query string. The result is still passed as a parameter rather than concatenated.
/// </para>
/// <para>
/// Stop words are dropped from the <em>query</em> and never from the document. A word removed at
/// index time cannot be restored without rebuilding the table, and the day somebody stocks a product
/// genuinely called "Online" is the day that becomes somebody's afternoon.
/// </para>
/// </remarks>
internal static class SearchTextNormalizer
{
    /// <summary>The most terms one query contributes to the expression.</summary>
    /// <remarks>
    /// Each term is a clause, each synonym another, and a pasted paragraph would otherwise become a
    /// sixty-clause query that scans the whole index. Nobody searching a homeware store means more
    /// than eight words; the ones beyond are dropped rather than refused, because refusing a long
    /// query is a worse answer than searching the beginning of it.
    /// </remarks>
    public const int MaxTerms = 8;

    /// <summary>
    /// Parses a query, applying the store's stop words and synonyms.
    /// </summary>
    /// <param name="text">What the shopper typed.</param>
    /// <param name="vocabulary">The store's own words.</param>
    /// <param name="maxLength">The longest input accepted before truncation.</param>
    /// <param name="prefixLastTerm">
    /// Whether the final term matches as a prefix. True for autocomplete, where the shopper is
    /// half-way through a word; false for a search, where they have finished typing and the
    /// stemmer is a better answer than a prefix.
    /// </param>
    public static NormalizedQuery Parse(
        string? text,
        SearchVocabularySnapshot vocabulary,
        int maxLength,
        bool prefixLastTerm)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (string.IsNullOrWhiteSpace(text))
        {
            return NormalizedQuery.Empty;
        }

        var raw = text.Trim();

        if (raw.Length > maxLength)
        {
            raw = raw[..maxLength];
        }

        var tokens = Tokenize(raw);
        var terms = new List<SearchTerm>();

        foreach (var token in tokens)
        {
            if (terms.Count == MaxTerms)
            {
                break;
            }

            if (vocabulary.IsStopWord(token))
            {
                continue;
            }

            terms.Add(new SearchTerm(token, vocabulary.Expand(token)));
        }

        if (terms.Count == 0)
        {
            // Everything the shopper typed was noise this store ignores. That is a browse of
            // whatever filters came with it, not a search that found nothing — answering "no
            // results" for a search for "buy online" would be technically true and useless.
            return new NormalizedQuery(raw, string.Empty, [], null);
        }

        var normalised = string.Join(' ', terms.Select(term => term.Term));

        return new NormalizedQuery(raw, normalised, terms, BuildTsQuery(terms, prefixLastTerm));
    }

    /// <summary>
    /// Splits text into lower-case alphanumeric words.
    /// </summary>
    /// <remarks>
    /// Digits are kept and are not a special case: a shopper searching for "80x80" or "220v" is
    /// searching for a size, and a tokenizer that dropped numbers would lose most of the queries a
    /// homeware catalogue receives.
    /// </remarks>
    /// <param name="text">The input.</param>
    public static IReadOnlyList<string> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<string>();
        var builder = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            tokens.Add(builder.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Folds a value for a comparison that ignores case and accents.
    /// </summary>
    /// <remarks>
    /// Used for the vocabulary lookups and for the stored form of a synonym or a stop word, so that
    /// a merchandiser who types "Sofa" and a shopper who types "sofa" mean the same rule.
    /// </remarks>
    /// <param name="value">The value.</param>
    public static string Fold(string? value)
        => value is null ? string.Empty : value.Trim().ToLower(CultureInfo.InvariantCulture);

    /// <summary>
    /// Assembles the <c>tsquery</c>: terms joined by AND, each term ORed with its synonyms.
    /// </summary>
    /// <remarks>
    /// AND between terms and OR within one, which is the behaviour a shopper expects without being
    /// able to say so: "beige cushion" must mean both words, and "sofa" must mean sofa or couch.
    /// Getting these the other way round produces a search that returns the whole store for two
    /// words and nothing for one.
    /// </remarks>
    /// <param name="terms">The surviving terms.</param>
    /// <param name="prefixLastTerm">Whether the final term matches as a prefix.</param>
    private static string BuildTsQuery(List<SearchTerm> terms, bool prefixLastTerm)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < terms.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" & ");
            }

            var term = terms[index];
            var prefix = prefixLastTerm && index == terms.Count - 1;
            var hasAlternatives = term.Alternatives.Count > 0;

            if (hasAlternatives)
            {
                builder.Append('(');
            }

            Append(builder, term.Term, prefix);

            foreach (var alternative in term.Alternatives)
            {
                builder.Append(" | ");

                // A synonym is never prefix-matched, even in autocomplete. The shopper is part way
                // through typing their own word, not part way through typing ours, and prefixing an
                // expansion turns "so" into every word beginning with "couch".
                Append(builder, alternative, prefix: false);
            }

            if (hasAlternatives)
            {
                builder.Append(')');
            }
        }

        return builder.ToString();
    }

    /// <summary>Appends one lexeme, optionally as a prefix match.</summary>
    /// <param name="builder">The expression being built.</param>
    /// <param name="lexeme">The word. Already known to be alphanumeric.</param>
    /// <param name="prefix">Whether it matches as a prefix.</param>
    private static void Append(StringBuilder builder, string lexeme, bool prefix)
    {
        builder.Append(lexeme);

        if (prefix)
        {
            builder.Append(":*");
        }
    }
}
