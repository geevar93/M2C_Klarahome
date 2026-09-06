using KlaraHome.Modules.Search.Infrastructure.Query;

namespace KlaraHome.UnitTests.Search;

/// <summary>
/// What a shopper typed, turned into something PostgreSQL can match on.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. Every failure mode here is a string that
/// is one character wrong, none of them throws, and each produces a search that quietly returns the
/// wrong thing: an <c>AND</c> where an <c>OR</c> belonged returns nothing, the reverse returns the
/// whole catalogue, and a stray operator character turns a shopper's apostrophe into a syntax error
/// deep inside the database. Reading the produced expression is the only way to know.
/// </remarks>
public sealed class SearchTextNormalizerTests
{
    /// <summary>The longest query the tests offer. Comfortably above anything they type.</summary>
    private const int MaxLength = 120;

    /// <summary>A store that has configured nothing of its own.</summary>
    private static readonly SearchVocabularySnapshot NoVocabulary = SearchVocabularySnapshot.Empty;

    /// <summary>
    /// Terms are joined by AND, so both words have to appear.
    /// </summary>
    /// <remarks>
    /// The behaviour a shopper expects without being able to say so. Getting this and the next test
    /// the other way round produces a search that returns the whole store for two words and nothing
    /// for one.
    /// </remarks>
    [Fact]
    public void Two_words_both_have_to_match()
    {
        var parsed = SearchTextNormalizer.Parse("beige cushion", NoVocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal("beige & cushion", parsed.TsQuery);
        Assert.Equal("beige cushion", parsed.Normalised);
        Assert.True(parsed.HasText);
    }

    /// <summary>A term and its synonyms are alternatives, so any of them matches.</summary>
    [Fact]
    public void A_synonym_is_an_alternative_and_not_a_requirement()
    {
        var vocabulary = Vocabulary(synonyms: new() { ["sofa"] = ["couch", "settee"] });

        var parsed = SearchTextNormalizer.Parse("sofa", vocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal("(sofa | couch | settee)", parsed.TsQuery);
    }

    /// <summary>Synonyms nest inside the AND rather than replacing it.</summary>
    /// <remarks>
    /// "beige sofa" must mean beige AND (sofa OR couch), not (beige AND sofa) OR couch. The second
    /// reading returns every couch in the store regardless of colour, and the parentheses are the
    /// only thing between the two.
    /// </remarks>
    [Fact]
    public void A_synonym_does_not_escape_the_and()
    {
        var vocabulary = Vocabulary(synonyms: new() { ["sofa"] = ["couch"] });

        var parsed = SearchTextNormalizer.Parse("beige sofa", vocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal("beige & (sofa | couch)", parsed.TsQuery);
    }

    /// <summary>A store's own stop words are dropped from the query.</summary>
    [Fact]
    public void A_stop_word_is_dropped()
    {
        var vocabulary = Vocabulary(stopWords: ["buy", "online"]);

        var parsed = SearchTextNormalizer.Parse("buy cushion online", vocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal("cushion", parsed.TsQuery);
        Assert.Equal("cushion", parsed.Normalised);
    }

    /// <summary>
    /// A query of nothing but stop words has no text condition at all.
    /// </summary>
    /// <remarks>
    /// Not a query that matches nothing. "buy online" in a store that ignores both words is a browse
    /// of whatever filters came with it — answering "no results" would be technically true and
    /// useless, and it is the difference between an empty page and the whole catalogue.
    /// </remarks>
    [Fact]
    public void A_query_of_only_stop_words_has_no_text_condition()
    {
        var vocabulary = Vocabulary(stopWords: ["buy", "online"]);

        var parsed = SearchTextNormalizer.Parse("buy online", vocabulary, MaxLength, prefixLastTerm: false);

        Assert.False(parsed.HasText);
        Assert.Null(parsed.TsQuery);
        Assert.Equal("buy online", parsed.Raw);
    }

    /// <summary>
    /// Every character that is not a letter or a digit is removed, not escaped.
    /// </summary>
    /// <remarks>
    /// <c>tsquery</c> has its own operator grammar, and a shopper who types an ampersand is not
    /// asking for any of it. This is the test that says a query string cannot become an expression
    /// the parser did not intend, whatever arrives.
    /// </remarks>
    [Theory]
    [InlineData("cushion & sofa", "cushion & sofa")]
    [InlineData("cushion | sofa", "cushion & sofa")]
    [InlineData("!cushion", "cushion")]
    [InlineData("(cushion:*)", "cushion")]
    [InlineData("mother's day", "mother & s & day")]
    [InlineData("cushion <-> cover", "cushion & cover")]
    public void Operator_characters_are_stripped_rather_than_honoured(string typed, string expected)
    {
        var parsed = SearchTextNormalizer.Parse(typed, NoVocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal(expected, parsed.TsQuery);
    }

    /// <summary>Digits are words too.</summary>
    /// <remarks>
    /// "80x80" and "220v" are how shoppers search for a size and a voltage. A tokenizer that dropped
    /// numbers would lose a large share of the queries a homeware catalogue receives.
    /// </remarks>
    [Fact]
    public void Numbers_survive_tokenising()
    {
        var parsed = SearchTextNormalizer.Parse("80x80 cushion", NoVocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal("80x80 & cushion", parsed.TsQuery);
    }

    /// <summary>Autocomplete prefixes the last term, and only the last.</summary>
    /// <remarks>
    /// The shopper has finished the earlier words and is half-way through this one. Prefixing all of
    /// them would make "cus cov" match far more than it should; prefixing none would mean the box
    /// showed nothing until a whole word was typed.
    /// </remarks>
    [Fact]
    public void Autocomplete_prefixes_only_the_word_being_typed()
    {
        var parsed = SearchTextNormalizer.Parse("cushion cov", NoVocabulary, MaxLength, prefixLastTerm: true);

        Assert.Equal("cushion & cov:*", parsed.TsQuery);
    }

    /// <summary>A synonym is never prefix-matched, even in autocomplete.</summary>
    /// <remarks>
    /// The shopper is part way through typing their own word, not part way through typing ours.
    /// Prefixing an expansion would turn "so" into every word beginning with "couch".
    /// </remarks>
    [Fact]
    public void Autocomplete_does_not_prefix_a_synonym()
    {
        var vocabulary = Vocabulary(synonyms: new() { ["sofa"] = ["couch"] });

        var parsed = SearchTextNormalizer.Parse("sofa", vocabulary, MaxLength, prefixLastTerm: true);

        Assert.Equal("(sofa:* | couch)", parsed.TsQuery);
    }

    /// <summary>A pasted paragraph is truncated rather than refused.</summary>
    /// <remarks>
    /// Each term is a clause, and a sixty-clause query scans the whole index. Searching the beginning
    /// of a long query is a better answer than refusing it.
    /// </remarks>
    [Fact]
    public void A_very_long_query_is_bounded_by_term_count()
    {
        var typed = string.Join(' ', Enumerable.Range(1, 20).Select(index => "word" + index));

        var parsed = SearchTextNormalizer.Parse(typed, NoVocabulary, MaxLength, prefixLastTerm: false);

        Assert.Equal(SearchTextNormalizer.MaxTerms, parsed.Terms.Count);
        Assert.StartsWith("word1 & word2", parsed.TsQuery, StringComparison.Ordinal);
    }

    /// <summary>Case and surrounding space do not change what a query means.</summary>
    [Fact]
    public void Folding_is_case_insensitive_and_trims()
    {
        Assert.Equal("sofa", SearchTextNormalizer.Fold("  SoFa "));
        Assert.Equal(string.Empty, SearchTextNormalizer.Fold(null));
    }

    /// <summary>An empty query is a browse, with no text condition.</summary>
    [Fact]
    public void An_empty_query_is_a_browse()
    {
        var parsed = SearchTextNormalizer.Parse("   ", NoVocabulary, MaxLength, prefixLastTerm: false);

        Assert.False(parsed.HasText);
        Assert.Empty(parsed.Terms);
    }

    /// <summary>Builds a vocabulary snapshot for a test.</summary>
    /// <param name="stopWords">Words this store ignores.</param>
    /// <param name="synonyms">Terms and what they also mean.</param>
    private static SearchVocabularySnapshot Vocabulary(
        string[]? stopWords = null,
        Dictionary<string, string[]>? synonyms = null)
        => new(
            stopWords ?? [],
            (synonyms ?? []).Select(pair =>
                new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, pair.Value)));
}
